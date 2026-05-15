using System.Security.Claims;
using System.Text.Json;
using EnterpriseChat.Licensing.Abstractions;
using EnterpriseChat.Protocol.Admin;
using EnterpriseChat.Server.Auth.Hashers;
using EnterpriseChat.Server.Auth.Providers;
using EnterpriseChat.Server.Auth.Providers.MySql;
using EnterpriseChat.Server.Crypto;
using EnterpriseChat.Server.Data;
using EnterpriseChat.Server.Data.Entities;
using EnterpriseChat.Server.Licensing;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace EnterpriseChat.Server.Admin;

internal static class AuthProviderAdminEndpoints
{
    public static void MapAuthProviderAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/admin/auth-providers")
            .WithTags("AdminAuthProviders")
            .RequireAuthorization(p => p.RequireRole(UserRole.Admin.ToString()));

        group.MapGet("/", ListAsync);
        group.MapGet("/{id:int}", GetAsync);
        group.MapPost("/", CreateAsync);
        group.MapPut("/{id:int}", UpdateAsync);
        group.MapDelete("/{id:int}", DeleteAsync);
        group.MapPost("/test", TestConnectionAsync);
        group.MapPost("/introspect", IntrospectAsync);
        group.MapPost("/{id:int}/introspect", IntrospectExistingAsync);
        group.MapPost("/{id:int}/browse", BrowseAsync);
        group.MapPost("/{id:int}/all-ids", AllIdsAsync);
        group.MapPost("/{id:int}/import", ImportAsync);
    }

    private const int MaxAllExternalIds = 10_000;

    private static readonly HashSet<string> BrowseSortColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "username", "email", "externalId",
    };

    private static async Task<IResult> ListAsync(
        IDbContextFactory<ChatDbContext> dbFactory,
        CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await db.AuthProviders
            .OrderBy(p => p.Priority)
            .ThenBy(p => p.Id)
            .AsNoTracking()
            .ToListAsync(ct);

        var summaries = rows.Select(r => new AuthProviderSummary(
            r.Id, r.Kind.ToString(), r.DisplayName, r.IsEnabled, r.Priority,
            r.HashAlgorithm.ToString(), r.PlaintextRiskAcknowledged,
            r.CreatedAt, r.UpdatedAt));
        return Results.Ok(summaries);
    }

    private static async Task<IResult> GetAsync(
        int id,
        IDbContextFactory<ChatDbContext> dbFactory,
        CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.AuthProviders.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, ct);
        if (row is null) return Results.NotFound();

        // El detalle expone config NO sensible. Las credenciales nunca
        // se devuelven en claro, ni siquiera al admin.
        var config = JsonSerializer.Deserialize<JsonElement>(row.ConfigJson);
        return Results.Ok(new AuthProviderDetail(
            row.Id, row.Kind.ToString(), row.DisplayName, row.IsEnabled, row.Priority,
            row.HashAlgorithm.ToString(), row.PlaintextRiskAcknowledged,
            config, row.CreatedAt, row.UpdatedAt));
    }

    private static async Task<IResult> CreateAsync(
        CreateAuthProviderRequest request,
        ClaimsPrincipal principal,
        IDbContextFactory<ChatDbContext> dbFactory,
        AppCrypto crypto,
        AuthProviderRegistry registry,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        if (!TryParseKind(request.Kind, out var kind, out var error))
            return Results.BadRequest(new { error });
        if (!TryParseHash(request.HashAlgorithm, out var hash, out error))
            return Results.BadRequest(new { error });
        if (!ValidatePlaintextAck(hash, request.PlaintextRiskAcknowledged, out error))
            return Results.BadRequest(new { error });
        if (!ValidateProviderPayload(kind, request.Config, out error))
            return Results.BadRequest(new { error });

        var configJson = JsonSerializer.Serialize(request.Config);
        var encryptedSecrets = EncryptIfPresent(crypto, request.Secrets);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = new AuthProviderConfig
        {
            Kind = kind,
            DisplayName = request.DisplayName.Trim(),
            IsEnabled = request.IsEnabled,
            Priority = request.Priority,
            HashAlgorithm = hash,
            PlaintextRiskAcknowledged = request.PlaintextRiskAcknowledged,
            ConfigJson = configJson,
            EncryptedSecretsJson = encryptedSecrets,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.AuthProviders.Add(row);
        db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = ResolveActorId(principal),
            Action = "auth.provider.create",
            Target = row.DisplayName,
            Details = $"kind:{row.Kind}",
        });
        await db.SaveChangesAsync(ct);
        await registry.ReloadAsync(ct);

        return Results.Created($"/admin/auth-providers/{row.Id}",
            new AuthProviderSummary(row.Id, row.Kind.ToString(), row.DisplayName,
                row.IsEnabled, row.Priority, row.HashAlgorithm.ToString(),
                row.PlaintextRiskAcknowledged, row.CreatedAt, row.UpdatedAt));
    }

    private static async Task<IResult> UpdateAsync(
        int id,
        UpdateAuthProviderRequest request,
        ClaimsPrincipal principal,
        IDbContextFactory<ChatDbContext> dbFactory,
        AppCrypto crypto,
        AuthProviderRegistry registry,
        CancellationToken ct)
    {
        if (!TryParseHash(request.HashAlgorithm, out var hash, out var error))
            return Results.BadRequest(new { error });
        if (!ValidatePlaintextAck(hash, request.PlaintextRiskAcknowledged, out error))
            return Results.BadRequest(new { error });

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.AuthProviders.SingleOrDefaultAsync(p => p.Id == id, ct);
        if (row is null) return Results.NotFound();

        if (!ValidateProviderPayload(row.Kind, request.Config, out error))
            return Results.BadRequest(new { error });

        row.DisplayName = request.DisplayName.Trim();
        row.IsEnabled = request.IsEnabled;
        row.Priority = request.Priority;
        row.HashAlgorithm = hash;
        row.PlaintextRiskAcknowledged = request.PlaintextRiskAcknowledged;
        row.ConfigJson = JsonSerializer.Serialize(request.Config);
        if (request.Secrets is not null)
        {
            row.EncryptedSecretsJson = EncryptIfPresent(crypto, request.Secrets);
        }
        row.UpdatedAt = DateTimeOffset.UtcNow;

        db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = ResolveActorId(principal),
            Action = "auth.provider.update",
            Target = row.DisplayName,
            Details = $"kind:{row.Kind}",
        });
        await db.SaveChangesAsync(ct);
        await registry.ReloadAsync(ct);
        return Results.NoContent();
    }

    /// <summary>
    /// Borra el proveedor. El admin elige qué hacer con los usuarios
    /// locales que ese provider había auto-aprovisionado:
    ///   - <c>keep</c>: dejarlos activos como cuentas locales huérfanas
    ///     (FK SetNull). Pueden loguearse si el admin les pone password.
    ///   - <c>deactivate</c>: SetNull + IsActive=false. Seguro por
    ///     defecto: ya no pueden entrar aunque tuvieran password local.
    ///     Reversible.
    ///   - <c>cascade</c>: borrar las filas locales. Peligroso porque
    ///     rompe FKs Message.FromUserId (Restrict). Si hay mensajes
    ///     enviados, el DELETE falla y avisamos al admin.
    /// </summary>
    private static async Task<IResult> DeleteAsync(
        int id,
        string? onProvisionedUsers,
        ClaimsPrincipal principal,
        IDbContextFactory<ChatDbContext> dbFactory,
        AuthProviderRegistry registry,
        CancellationToken ct)
    {
        var mode = onProvisionedUsers?.ToLowerInvariant() ?? "deactivate";
        if (mode is not "keep" and not "deactivate" and not "cascade")
        {
            return Results.BadRequest(new { error = "onProvisionedUsers debe ser 'keep', 'deactivate' o 'cascade'." });
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.AuthProviders.SingleOrDefaultAsync(p => p.Id == id, ct);
        if (row is null) return Results.NotFound();

        var provisioned = await db.Users.Where(u => u.SourceProviderId == id).ToListAsync(ct);
        switch (mode)
        {
            case "deactivate":
                foreach (var u in provisioned)
                {
                    u.IsActive = false;
                    // FK SetNull se aplica automáticamente al borrar el provider.
                }
                break;
            case "cascade":
                if (provisioned.Count > 0)
                {
                    // Comprobamos integridad referencial antes de tirar
                    // el DELETE — si hay mensajes enviados por estos
                    // usuarios, SQLite con OnDelete.Restrict los protege.
                    var ids = provisioned.Select(u => u.Id).ToList();
                    var hasMessages = await db.Messages.AnyAsync(m => ids.Contains(m.FromUserId), ct);
                    if (hasMessages)
                    {
                        return Results.BadRequest(new
                        {
                            error = "No se pueden borrar los usuarios provisionados: tienen mensajes en el chat. Elige 'desactivar' en su lugar.",
                        });
                    }
                    db.Users.RemoveRange(provisioned);
                }
                break;
            // 'keep': nada que hacer, el SetNull los deja huérfanos.
        }

        db.AuthProviders.Remove(row);
        db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = ResolveActorId(principal),
            Action = "auth.provider.delete",
            Target = row.DisplayName,
            Details = $"kind:{row.Kind} mode:{mode} provisioned_users:{provisioned.Count}",
        });
        await db.SaveChangesAsync(ct);
        await registry.ReloadAsync(ct);
        return Results.NoContent();
    }

    /// <summary>
    /// Permite al admin probar la conexión antes de guardar. No persiste
    /// nada en BD. Si TestUsername se proporciona, también prueba la
    /// query SELECT y devuelve si el usuario existe (sin verificar la
    /// contraseña).
    /// </summary>
    private static async Task<IResult> TestConnectionAsync(
        AuthProviderTestRequest request,
        AppCrypto crypto,
        CancellationToken ct)
    {
        if (!TryParseKind(request.Kind, out var kind, out var error))
            return Results.BadRequest(new { error });

        if (kind != AuthProviderKind.Mysql)
        {
            return Results.BadRequest(new { error = $"Test no soportado para {kind} todavía." });
        }

        try
        {
            var pub = DeserializePayload<MySqlProviderPublicConfig>(request.Config)
                ?? throw new InvalidOperationException("Config vacía.");
            var secrets = DeserializePayload<MySqlProviderSecrets>(request.Secrets)
                ?? throw new InvalidOperationException("Secrets vacíos.");

            // Validamos identificadores antes de tocar la red.
            MySqlAuthProvider.BuildSelectSql(pub);

            var probe = new MySqlAuthProvider(
                providerId: 0, displayName: "(test)",
                pub, secrets, HashAlgorithm.Plaintext,
                new PlaintextVerifier()); // Verifier no se usa en el test.

            if (string.IsNullOrEmpty(request.TestUsername))
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(pub.QueryTimeoutSeconds + 2));
                await PingAsync(pub, secrets, cts.Token);
                return Results.Ok(new AuthProviderTestResult(Connected: true, UserFound: null, Detail: null));
            }

            // Probamos la query completa con una contraseña inventada para
            // forzar el SELECT. Lo que nos importa es saber si la fila
            // existe — no verificamos hash.
            var result = await probe.VerifyAsync(request.TestUsername, "__probe__", ct);
            return result.Outcome switch
            {
                AuthOutcome.ProviderError => Results.Ok(new AuthProviderTestResult(false, null, result.FailureDetail)),
                AuthOutcome.UnknownUser   => Results.Ok(new AuthProviderTestResult(true, false, "Usuario no encontrado en la tabla externa.")),
                _                          => Results.Ok(new AuthProviderTestResult(true, true, null)),
            };
        }
        catch (Exception ex)
        {
            return Results.Ok(new AuthProviderTestResult(false, null, ex.Message));
        }
    }

    /// <summary>
    /// Variante de introspect para el flujo de edición: el admin
    /// puede haber modificado host/db/table sin tocar las credenciales.
    /// Si <c>request.Secrets</c> viene vacío o null, descifra los
    /// secretos guardados del provider <paramref name="id"/> y los
    /// reutiliza. Si viene con datos, los usa (caso "el admin cambia
    /// la contraseña a la vez").
    /// </summary>
    private static async Task<IResult> IntrospectExistingAsync(
        int id,
        AuthProviderIntrospectRequest request,
        IDbContextFactory<ChatDbContext> dbFactory,
        AppCrypto crypto,
        CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.AuthProviders.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, ct);
        if (row is null) return Results.NotFound();
        if (row.Kind != AuthProviderKind.Mysql)
            return Results.BadRequest(new { error = $"Introspección no soportada para {row.Kind}." });

        try
        {
            var pub = DeserializePayload<MySqlProviderPublicConfig>(request.Config)
                ?? throw new InvalidOperationException("Config vacía.");

            var bodySecrets = DeserializePayload<MySqlProviderSecrets>(request.Secrets);
            var secrets = (bodySecrets is null || (string.IsNullOrEmpty(bodySecrets.User) && string.IsNullOrEmpty(bodySecrets.Password)))
                ? DescifrarSecretosGuardados(row, crypto)
                : bodySecrets;

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(pub.QueryTimeoutSeconds + 3));

            if (string.IsNullOrWhiteSpace(request.Table))
            {
                var tables = await MySqlAuthProvider.ListTablesAsync(pub, secrets, cts.Token);
                return Results.Ok(new AuthProviderIntrospectResult(tables, null));
            }
            var columns = await MySqlAuthProvider.ListColumnsAsync(pub, secrets, request.Table, cts.Token);
            return Results.Ok(new AuthProviderIntrospectResult(null, columns));
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static MySqlProviderSecrets DescifrarSecretosGuardados(AuthProviderConfig row, AppCrypto crypto)
    {
        if (string.IsNullOrEmpty(row.EncryptedSecretsJson))
        {
            return new MySqlProviderSecrets();
        }
        var json = crypto.DecryptString(row.EncryptedSecretsJson);
        return JsonSerializer.Deserialize<MySqlProviderSecrets>(json, JsonOptions) ?? new MySqlProviderSecrets();
    }

    /// <summary>
    /// Lista paginada de los usuarios de la tabla externa, marcando si
    /// cada uno ya está importado localmente. Permite al admin elegir
    /// quién entra al chat sin tirar de SQL ni esperar a que cada uno
    /// haga su primer login.
    /// </summary>
    private static async Task<IResult> BrowseAsync(
        int id,
        AuthProviderBrowseRequest request,
        IDbContextFactory<ChatDbContext> dbFactory,
        AppCrypto crypto,
        ILicenseValidator licensing,
        CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.AuthProviders.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, ct);
        if (row is null) return Results.NotFound();
        if (row.Kind != AuthProviderKind.Mysql)
        {
            return Results.BadRequest(new { error = $"Browse no soportado para {row.Kind}." });
        }

        try
        {
            var pub = JsonSerializer.Deserialize<MySqlProviderPublicConfig>(row.ConfigJson, JsonOptions)
                ?? throw new InvalidOperationException("ConfigJson inválido.");
            var secretsJson = string.IsNullOrEmpty(row.EncryptedSecretsJson)
                ? "{}" : crypto.DecryptString(row.EncryptedSecretsJson);
            var secrets = JsonSerializer.Deserialize<MySqlProviderSecrets>(secretsJson, JsonOptions)
                ?? new MySqlProviderSecrets();

            if (!string.IsNullOrEmpty(request.Sort) && !BrowseSortColumns.Contains(request.Sort))
            {
                return Results.BadRequest(new { error = $"Columna de orden no admitida: {request.Sort}." });
            }
            var (rows, total) = await MySqlAuthProvider.BrowseAsync(
                pub, secrets, request.Search, request.Page, request.PageSize, ct,
                sort: request.Sort, dir: request.Dir);

            // Marcamos los que ya existen localmente (por externalId).
            var externalIds = rows.Select(r => r.ExternalId).ToList();
            var existing = await db.Users
                .Where(u => u.SourceProviderId == id && u.ExternalId != null && externalIds.Contains(u.ExternalId))
                .Select(u => u.ExternalId!)
                .ToListAsync(ct);
            var existingSet = new HashSet<string>(existing);

            var mapped = rows.Select(r => new AuthProviderBrowseRow(
                ExternalId: r.ExternalId,
                Username: r.Username,
                FullName: r.FullName,
                Email: r.Email,
                AlreadyImported: existingSet.Contains(r.ExternalId))).ToList();

            var active = await LicenseCap.CountActiveUsersAsync(db, ct);
            var max = licensing.Current.MaxConcurrentUsers;

            return Results.Ok(new AuthProviderBrowseResult(
                Rows: mapped, Total: total, Page: request.Page, PageSize: request.PageSize,
                LicenseSlotsAvailable: Math.Max(0, max - active),
                LicenseMaxUsers: max,
                LicenseActiveUsers: active));
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Devuelve solo los external IDs que matchean el filtro (sin
    /// paginar). Usado por la UI para "seleccionar todos los del
    /// filtro" y luego importar. Tope duro 10.000 — si excede, 413
    /// con detalle y la SPA pide refinar.
    /// </summary>
    private static async Task<IResult> AllIdsAsync(
        int id,
        AuthProviderAllIdsRequest request,
        IDbContextFactory<ChatDbContext> dbFactory,
        AppCrypto crypto,
        CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.AuthProviders.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, ct);
        if (row is null) return Results.NotFound();
        if (row.Kind != AuthProviderKind.Mysql)
            return Results.BadRequest(new { error = $"all-ids no soportado para {row.Kind}." });
        if (!string.IsNullOrEmpty(request.Sort) && !BrowseSortColumns.Contains(request.Sort))
            return Results.BadRequest(new { error = $"Columna de orden no admitida: {request.Sort}." });

        try
        {
            var pub = JsonSerializer.Deserialize<MySqlProviderPublicConfig>(row.ConfigJson, JsonOptions)
                ?? throw new InvalidOperationException("ConfigJson inválido.");
            var secretsJson = string.IsNullOrEmpty(row.EncryptedSecretsJson)
                ? "{}" : crypto.DecryptString(row.EncryptedSecretsJson);
            var secrets = JsonSerializer.Deserialize<MySqlProviderSecrets>(secretsJson, JsonOptions)
                ?? new MySqlProviderSecrets();

            var (ids, total) = await MySqlAuthProvider.ListExternalIdsAsync(
                pub, secrets, request.Search, MaxAllExternalIds, ct,
                sort: request.Sort, dir: request.Dir);

            if (total > MaxAllExternalIds)
            {
                return Results.Json(
                    new { error = $"Demasiados resultados ({total}). Refina el filtro: máximo {MaxAllExternalIds}." },
                    statusCode: StatusCodes.Status413PayloadTooLarge);
            }

            return Results.Ok(new AuthProviderAllIdsResult(ids));
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Importa en masa una lista de external IDs. Para cada uno:
    ///   1. Lo busca en la tabla externa para sacar username/email/full_name.
    ///   2. Comprueba que no existe ya localmente (por external_id ni
    ///      por colisión de username).
    ///   3. Comprueba slots de licencia ANTES de crear.
    ///   4. Inserta con SourceProviderId apuntando al provider y un
    ///      PasswordHash sentinela ("external") que el verifier interno
    ///      nunca aceptaría.
    /// Devuelve cuántos se crearon y por qué se saltaron los demás.
    /// </summary>
    private static async Task<IResult> ImportAsync(
        int id,
        AuthProviderImportRequest request,
        ClaimsPrincipal principal,
        IDbContextFactory<ChatDbContext> dbFactory,
        AppCrypto crypto,
        ILicenseValidator licensing,
        CancellationToken ct)
    {
        if (request.ExternalIds is null || request.ExternalIds.Count == 0)
        {
            return Results.BadRequest(new { error = "No se han enviado IDs para importar." });
        }
        if (request.ExternalIds.Count > 500)
        {
            return Results.BadRequest(new { error = "Máximo 500 usuarios por lote de importación." });
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.AuthProviders.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, ct);
        if (row is null) return Results.NotFound();
        if (row.Kind != AuthProviderKind.Mysql)
        {
            return Results.BadRequest(new { error = $"Import no soportado para {row.Kind}." });
        }

        var pub = JsonSerializer.Deserialize<MySqlProviderPublicConfig>(row.ConfigJson, JsonOptions)
            ?? throw new InvalidOperationException("ConfigJson inválido.");
        var secretsJson = string.IsNullOrEmpty(row.EncryptedSecretsJson)
            ? "{}" : crypto.DecryptString(row.EncryptedSecretsJson);
        var secrets = JsonSerializer.Deserialize<MySqlProviderSecrets>(secretsJson, JsonOptions)
            ?? new MySqlProviderSecrets();

        // Pre-cargamos los locales que ya existen para esos external IDs
        // para detectar duplicados sin un SELECT por usuario.
        var requested = new HashSet<string>(request.ExternalIds);
        var alreadyImported = await db.Users
            .Where(u => u.SourceProviderId == id && u.ExternalId != null && requested.Contains(u.ExternalId))
            .Select(u => u.ExternalId!)
            .ToListAsync(ct);
        var alreadyImportedSet = new HashSet<string>(alreadyImported);

        // Cap de licencia: cuántos pueden entrar antes de tocar fondo.
        var capStart = await LicenseCap.CountActiveUsersAsync(db, ct);
        var max = licensing.Current.MaxConcurrentUsers;
        var slotsLeft = Math.Max(0, max - capStart);

        var created = 0;
        var skipped = 0;
        var reasons = new List<string>();
        var actorId = ResolveActorId(principal);

        // Una sola SELECT que trae todos los IDs solicitados (WHERE IN).
        // Mucho más eficiente que iterar y respeta el límite de 500.
        IReadOnlyList<MySqlAuthProvider.BrowseRow> externalRows;
        try
        {
            externalRows = await MySqlAuthProvider.FetchByExternalIdsAsync(
                pub, secrets, request.ExternalIds.ToList(), ct);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = $"Error consultando MySQL: {ex.Message}" });
        }

        var byExternalId = externalRows.ToDictionary(r => r.ExternalId, StringComparer.Ordinal);

        foreach (var externalId in request.ExternalIds)
        {
            if (string.IsNullOrEmpty(externalId))
            {
                skipped++; reasons.Add("ID vacío.");
                continue;
            }
            if (alreadyImportedSet.Contains(externalId))
            {
                skipped++; reasons.Add($"{externalId}: ya importado.");
                continue;
            }
            if (slotsLeft <= 0)
            {
                skipped++; reasons.Add($"{externalId}: sin slots de licencia.");
                continue;
            }
            if (!byExternalId.TryGetValue(externalId, out var match))
            {
                skipped++; reasons.Add($"{externalId}: no encontrado en la tabla externa.");
                continue;
            }
            if (await db.Users.AnyAsync(u => u.Username == match.Username, ct))
            {
                skipped++; reasons.Add($"{externalId}: choca con usuario local existente '{match.Username}'.");
                continue;
            }

            db.Users.Add(new User
            {
                Username = match.Username,
                FullName = string.IsNullOrEmpty(match.FullName) ? match.Username : match.FullName!,
                Email = match.Email,
                Role = UserRole.User,
                PasswordHash = "external",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
                SourceProviderId = id,
                ExternalId = match.ExternalId,
            });
            db.AuditLogs.Add(new AuditLog
            {
                ActorUserId = actorId,
                Action = "user.import",
                Target = match.Username,
                Details = $"provider:#{id} external_id:{match.ExternalId}",
            });
            created++;
            slotsLeft--;
        }

        await db.SaveChangesAsync(ct);
        return Results.Ok(new AuthProviderImportResult(created, skipped, reasons));
    }

    /// <summary>
    /// Devuelve la lista de tablas del MySQL si <c>Table</c> no se pasa,
    /// o las columnas de la tabla indicada si sí se pasa. Usado por el
    /// wizard de la SPA para rellenar selects en vez de obligar al admin
    /// a escribir nombres a mano.
    /// </summary>
    private static async Task<IResult> IntrospectAsync(
        AuthProviderIntrospectRequest request,
        AppCrypto crypto,
        CancellationToken ct)
    {
        if (!TryParseKind(request.Kind, out var kind, out var error))
            return Results.BadRequest(new { error });
        if (kind != AuthProviderKind.Mysql)
            return Results.BadRequest(new { error = $"Introspección no soportada para {kind} todavía." });

        try
        {
            var pub = DeserializePayload<MySqlProviderPublicConfig>(request.Config)
                ?? throw new InvalidOperationException("Config vacía.");
            var secrets = DeserializePayload<MySqlProviderSecrets>(request.Secrets)
                ?? throw new InvalidOperationException("Secrets vacíos.");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(pub.QueryTimeoutSeconds + 3));

            if (string.IsNullOrWhiteSpace(request.Table))
            {
                var tables = await MySqlAuthProvider.ListTablesAsync(pub, secrets, cts.Token);
                return Results.Ok(new AuthProviderIntrospectResult(tables, null));
            }

            var columns = await MySqlAuthProvider.ListColumnsAsync(pub, secrets, request.Table, cts.Token);
            return Results.Ok(new AuthProviderIntrospectResult(null, columns));
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task PingAsync(
        MySqlProviderPublicConfig pub,
        MySqlProviderSecrets secrets,
        CancellationToken ct)
    {
        var dummy = new MySqlAuthProvider(
            providerId: 0, displayName: "(probe)",
            pub, secrets, HashAlgorithm.Plaintext, new PlaintextVerifier());
        // VerifyAsync con username imposible: cierra el ciclo connection→select→read.
        await dummy.VerifyAsync("__probe_unlikely_user__\x00", "x", ct);
    }

    private static bool ValidateProviderPayload(
        AuthProviderKind kind,
        object configPayload,
        out string error)
    {
        error = "";
        switch (kind)
        {
            case AuthProviderKind.Mysql:
                try
                {
                    var pub = DeserializePayload<MySqlProviderPublicConfig>(configPayload);
                    if (pub is null) { error = "Config MySQL vacía."; return false; }
                    if (string.IsNullOrWhiteSpace(pub.Host)) { error = "Host obligatorio."; return false; }
                    if (string.IsNullOrWhiteSpace(pub.Database)) { error = "Database obligatoria."; return false; }
                    MySqlAuthProvider.BuildSelectSql(pub); // valida identificadores
                    return true;
                }
                catch (Exception ex) { error = ex.Message; return false; }
            default:
                error = $"Kind {kind} no soportado todavía.";
                return false;
        }
    }

    private static bool ValidatePlaintextAck(HashAlgorithm hash, bool ack, out string error)
    {
        error = "";
        if (hash == HashAlgorithm.Plaintext && !ack)
        {
            error = "Para usar plaintext hay que marcar la confirmación de riesgo.";
            return false;
        }
        return true;
    }

    /// <summary>
    /// La SPA manda JSON con propiedades en camelCase ("database",
    /// "passwordColumn"); las DTOs C# son PascalCase. Sin
    /// <c>PropertyNameCaseInsensitive</c>, todos los campos llegan vacíos
    /// y los validadores fallan con "Database obligatoria" aunque el
    /// admin haya rellenado el formulario.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static T? DeserializePayload<T>(object? payload)
    {
        if (payload is null) return default;
        if (payload is JsonElement je)
        {
            return je.Deserialize<T>(JsonOptions);
        }
        var json = JsonSerializer.Serialize(payload);
        return JsonSerializer.Deserialize<T>(json, JsonOptions);
    }

    private static string? EncryptIfPresent(AppCrypto crypto, object? secrets)
    {
        if (secrets is null) return null;
        // Normalizamos a camelCase para que el blob descifrado sea
        // intercambiable con lo que envía la SPA y futuros consumidores.
        var json = JsonSerializer.Serialize(secrets, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });
        if (json == "null") return null;
        return crypto.EncryptString(json);
    }

    private static bool TryParseKind(string raw, out AuthProviderKind kind, out string error)
    {
        if (Enum.TryParse(raw, ignoreCase: true, out kind) && kind != AuthProviderKind.Internal)
        {
            error = "";
            return true;
        }
        kind = AuthProviderKind.Internal;
        error = $"Kind '{raw}' no admitido. Internal no se configura.";
        return false;
    }

    private static bool TryParseHash(string raw, out HashAlgorithm hash, out string error)
    {
        if (Enum.TryParse(raw, ignoreCase: true, out hash))
        {
            error = "";
            return true;
        }
        error = $"Algoritmo '{raw}' desconocido.";
        return false;
    }

    private static int? ResolveActorId(ClaimsPrincipal principal)
    {
        var sub = principal.FindFirstValue("sub") ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(sub, out var id) ? id : null;
    }
}

using System.Security.Claims;
using System.Text.Json;
using EnterpriseChat.Protocol.Admin;
using EnterpriseChat.Server.Auth.Hashers;
using EnterpriseChat.Server.Auth.Providers;
using EnterpriseChat.Server.Auth.Providers.MySql;
using EnterpriseChat.Server.Crypto;
using EnterpriseChat.Server.Data;
using EnterpriseChat.Server.Data.Entities;
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
    }

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

    private static async Task<IResult> DeleteAsync(
        int id,
        ClaimsPrincipal principal,
        IDbContextFactory<ChatDbContext> dbFactory,
        AuthProviderRegistry registry,
        CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.AuthProviders.SingleOrDefaultAsync(p => p.Id == id, ct);
        if (row is null) return Results.NotFound();

        db.AuthProviders.Remove(row);
        db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = ResolveActorId(principal),
            Action = "auth.provider.delete",
            Target = row.DisplayName,
            Details = $"kind:{row.Kind}",
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

    private static T? DeserializePayload<T>(object? payload)
    {
        if (payload is null) return default;
        if (payload is JsonElement je)
        {
            return je.Deserialize<T>();
        }
        var json = JsonSerializer.Serialize(payload);
        return JsonSerializer.Deserialize<T>(json);
    }

    private static string? EncryptIfPresent(AppCrypto crypto, object? secrets)
    {
        if (secrets is null) return null;
        var json = JsonSerializer.Serialize(secrets);
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

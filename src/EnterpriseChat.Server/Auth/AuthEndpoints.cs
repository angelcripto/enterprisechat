using EnterpriseChat.Licensing.Abstractions;
using EnterpriseChat.Protocol;
using EnterpriseChat.Server.Auth.Providers;
using EnterpriseChat.Server.Auth.Providers.MySql;
using EnterpriseChat.Server.Bootstrap;
using EnterpriseChat.Server.Data;
using EnterpriseChat.Server.Data.Entities;
using EnterpriseChat.Server.Licensing;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace EnterpriseChat.Server.Auth;

internal static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth").WithTags("Auth");

        group.MapPost("/login", LoginAsync);
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        IDbContextFactory<ChatDbContext> dbFactory,
        AuthProviderRegistry providers,
        IPasswordHasher hasher,
        JwtTokenIssuer issuer,
        ILicenseValidator licensing,
        ILogger<Program> log,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrEmpty(request.Password))
        {
            return Results.BadRequest(new { error = "Usuario y contraseña son obligatorios." });
        }

        // El admin siempre va al provider Internal. Hardcodeado por
        // seguridad: aunque alguien configure MySQL externo con un
        // usuario "admin", nuestra cuenta de rescate local prevalece y
        // no expone los privilegios al sistema externo.
        var isAdminUser = string.Equals(
            request.Username,
            AdminSeeder.DefaultAdminUsername,
            StringComparison.OrdinalIgnoreCase);

        var providerChain = isAdminUser
            ? new IAuthProvider[] { providers.Internal }
            : providers.All.ToArray();

        IAuthProvider? winning = null;
        AuthResult? winningResult = null;
        AuthResult? lastFailure = null;

        foreach (var provider in providerChain)
        {
            AuthResult result;
            try
            {
                result = await provider.VerifyAsync(request.Username, request.Password, ct);
            }
            catch (Exception ex)
            {
                log.LogWarning(
                    ex,
                    "Provider {Provider} ({Kind}) lanzó excepción verificando a {Username}",
                    provider.DisplayName, provider.Kind, request.Username);
                lastFailure = AuthResult.ProviderError(ex.Message);
                break;
            }

            if (result.Succeeded)
            {
                winning = provider;
                winningResult = result;
                break;
            }

            lastFailure = result;
            if (result.Outcome == AuthOutcome.ProviderError)
            {
                // No queremos colapsar silenciosamente al siguiente
                // provider si MySQL está caído — devolvemos 401 igual
                // pero registramos por qué.
                break;
            }
        }

        if (winning is null || winningResult is null)
        {
            await using var dbFail = await dbFactory.CreateDbContextAsync(ct);
            var failedUser = await dbFail.Users
                .Where(u => u.Username == request.Username)
                .Select(u => new { u.Id })
                .SingleOrDefaultAsync(ct);

            var detail = lastFailure?.Outcome switch
            {
                AuthOutcome.ProviderError => $"provider_error:{lastFailure.FailureDetail}",
                AuthOutcome.BadPassword   => "bad_password",
                _                          => "unknown_user",
            };
            await WriteAuditAsync(dbFail, failedUser?.Id, "auth.login.failed", request.Username, detail, ct);
            log.LogInformation("Login fallido ({Detail}): {Username}", detail, request.Username);
            return Results.Unauthorized();
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        // Localizamos / damos de alta la fila local del usuario.
        var localUser = await ResolveLocalUserAsync(db, winning, winningResult, request.Username, licensing, log, ct);
        if (localUser is null || !localUser.IsActive)
        {
            await WriteAuditAsync(db, localUser?.Id, "auth.login.failed", request.Username, "inactive_local_user", ct);
            log.LogInformation("Login rechazado (usuario local inactivo): {Username}", request.Username);
            return Results.Unauthorized();
        }

        if (winningResult.NeedsRehash && winning.Kind == AuthProviderKind.Internal)
        {
            localUser.PasswordHash = hasher.Hash(request.Password);
        }

        localUser.LastLoginAt = DateTimeOffset.UtcNow;
        await WriteAuditAsync(db, localUser.Id, "auth.login.success", localUser.Username, $"provider:{winning.Kind}", ct);
        await db.SaveChangesAsync(ct);

        var token = issuer.Issue(localUser);

        log.LogInformation(
            "Login OK: {Username} (#{UserId}) via {Provider}",
            localUser.Username, localUser.Id, winning.Kind);

        return Results.Ok(new LoginResponse(
            AccessToken: token.AccessToken,
            ExpiresAt: token.ExpiresAt,
            UserId: localUser.Id,
            Username: localUser.Username,
            FullName: localUser.FullName,
            Role: localUser.Role.ToString()));
    }

    /// <summary>
    /// Localiza la fila local del usuario tras un login exitoso. Si el
    /// provider externo lo permite (flag <c>AutoProvision</c> en su
    /// config), creamos la fila local en este momento.
    /// </summary>
    private static async Task<User?> ResolveLocalUserAsync(
        ChatDbContext db,
        IAuthProvider provider,
        AuthResult result,
        string username,
        ILicenseValidator licensing,
        ILogger log,
        CancellationToken ct)
    {
        if (provider.Kind == AuthProviderKind.Internal)
        {
            return await db.Users.SingleOrDefaultAsync(u => u.Username == username, ct);
        }

        // Match preferente por (SourceProviderId, ExternalId) si tenemos
        // externalId; fallback por Username + SourceProviderId.
        User? existing = null;
        if (!string.IsNullOrEmpty(result.ExternalId))
        {
            existing = await db.Users.SingleOrDefaultAsync(
                u => u.SourceProviderId == provider.ProviderId && u.ExternalId == result.ExternalId,
                ct);
        }
        existing ??= await db.Users.SingleOrDefaultAsync(
            u => u.Username == username && u.SourceProviderId == provider.ProviderId,
            ct);

        if (existing is not null)
        {
            // Mantenemos metadatos en sync si el provider los expuso.
            if (!string.IsNullOrEmpty(result.FullName) && existing.FullName != result.FullName)
            {
                existing.FullName = result.FullName;
            }
            if (!string.IsNullOrEmpty(result.Email) && existing.Email != result.Email)
            {
                existing.Email = result.Email;
            }
            return existing;
        }

        if (!await ShouldAutoProvisionAsync(db, provider, ct))
        {
            return null;
        }

        // Colisión: que no exista por (provider, externalId) ni por
        // (provider, username) no garantiza que el Username sea único
        // en la tabla local — puede chocar con un admin o con otro
        // provider. SQLite es UNIQUE en Username; ante choque,
        // rechazamos el login y dejamos el conflicto al admin.
        var collision = await db.Users.AnyAsync(u => u.Username == username, ct);
        if (collision)
        {
            return null;
        }

        // Antes de crear comprobamos el cap de la licencia. Si no hay
        // slot, el login falla con 401 y queda registrado en audit.
        var cap = await LicenseCap.CheckCanAddAsync(db, licensing, extra: 1, ct);
        if (!cap.Allowed)
        {
            log.LogWarning(
                "Auto-provisión denegada para {Username}: límite de cuentas ({Active}/{Max}).",
                username, cap.CurrentActive, cap.Max);
            db.AuditLogs.Add(new AuditLog
            {
                ActorUserId = null,
                Action = "user.autoprovision.denied_license",
                Target = username,
                Details = $"provider:{provider.Kind}#{provider.ProviderId} active={cap.CurrentActive} max={cap.Max}",
            });
            await db.SaveChangesAsync(ct);
            return null;
        }

        var newUser = new User
        {
            Username = username,
            FullName = string.IsNullOrEmpty(result.FullName) ? username : result.FullName!,
            Email = result.Email,
            Role = UserRole.User,
            PasswordHash = "external", // Sentinel: nunca lo verifica nadie.
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            SourceProviderId = provider.ProviderId,
            ExternalId = result.ExternalId,
        };
        db.Users.Add(newUser);
        db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = null,
            Action = "user.autoprovision",
            Target = newUser.Username,
            Details = $"provider:{provider.Kind}#{provider.ProviderId}",
        });
        await db.SaveChangesAsync(ct);
        return newUser;
    }

    private static async Task<bool> ShouldAutoProvisionAsync(
        ChatDbContext db,
        IAuthProvider provider,
        CancellationToken ct)
    {
        // PR 2 solo MySQL tiene flag; el resto (Http/Csv) lo añadirán.
        if (provider.Kind != AuthProviderKind.Mysql) return false;

        var row = await db.AuthProviders
            .Where(p => p.Id == provider.ProviderId)
            .Select(p => new { p.ConfigJson })
            .SingleOrDefaultAsync(ct);
        if (row is null) return false;

        try
        {
            var pub = JsonSerializer.Deserialize<MySqlProviderPublicConfig>(
                row.ConfigJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return pub?.AutoProvision ?? false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static async Task WriteAuditAsync(
        ChatDbContext db,
        int? actorUserId,
        string action,
        string? target,
        string? detail,
        CancellationToken ct)
    {
        db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = actorUserId,
            Action = action,
            Target = target,
            Details = detail
        });
        await db.SaveChangesAsync(ct);
    }
}

using EnterpriseChat.Protocol;
using EnterpriseChat.Server.Auth.Providers;
using EnterpriseChat.Server.Bootstrap;
using EnterpriseChat.Server.Data;
using EnterpriseChat.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;

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
        var localUser = await ResolveLocalUserAsync(db, winning, winningResult, request.Username, ct);
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
    /// Localiza la fila local del usuario tras un login exitoso.
    /// Para el provider Internal el usuario ya existe (lo creó el admin
    /// o el seeder). Para providers externos, PR 1 NO autoprovisiona —
    /// si el usuario no tiene cuenta local todavía, devolvemos null y
    /// el endpoint responde 401. El autoaprovisionamiento se añade en
    /// PRs siguientes junto con la importación CSV / sync MySQL.
    /// </summary>
    private static async Task<User?> ResolveLocalUserAsync(
        ChatDbContext db,
        IAuthProvider provider,
        AuthResult result,
        string username,
        CancellationToken ct)
    {
        if (provider.Kind == AuthProviderKind.Internal)
        {
            return await db.Users.SingleOrDefaultAsync(u => u.Username == username, ct);
        }

        // Match preferente por (SourceProviderId, ExternalId) si tenemos
        // externalId; fallback por Username + SourceProviderId.
        if (!string.IsNullOrEmpty(result.ExternalId))
        {
            var byExternal = await db.Users.SingleOrDefaultAsync(
                u => u.SourceProviderId == provider.ProviderId && u.ExternalId == result.ExternalId,
                ct);
            if (byExternal is not null) return byExternal;
        }

        return await db.Users.SingleOrDefaultAsync(
            u => u.Username == username && u.SourceProviderId == provider.ProviderId,
            ct);
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

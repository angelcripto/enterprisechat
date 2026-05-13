using EnterpriseChat.Protocol;
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
        IPasswordHasher hasher,
        JwtTokenIssuer issuer,
        ILogger<Program> log,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrEmpty(request.Password))
        {
            return Results.BadRequest(new { error = "Usuario y contraseña son obligatorios." });
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var user = await db.Users
            .Where(u => u.Username == request.Username && u.IsActive)
            .SingleOrDefaultAsync(ct);

        if (user is null)
        {
            await WriteAuditAsync(db, actorUserId: null, action: "auth.login.failed", target: request.Username, "unknown_user", ct);
            log.LogInformation("Login fallido (usuario desconocido): {Username}", request.Username);
            return Results.Unauthorized();
        }

        var verify = hasher.Verify(request.Password, user.PasswordHash);
        if (!verify.Success)
        {
            await WriteAuditAsync(db, user.Id, "auth.login.failed", user.Username, "bad_password", ct);
            log.LogInformation("Login fallido (password): {Username}", request.Username);
            return Results.Unauthorized();
        }

        if (verify.NeedsRehash)
        {
            user.PasswordHash = hasher.Hash(request.Password);
        }

        user.LastLoginAt = DateTimeOffset.UtcNow;
        await WriteAuditAsync(db, user.Id, "auth.login.success", user.Username, null, ct);
        await db.SaveChangesAsync(ct);

        var token = issuer.Issue(user);

        log.LogInformation("Login OK: {Username} (#{UserId})", user.Username, user.Id);

        return Results.Ok(new LoginResponse(
            AccessToken: token.AccessToken,
            ExpiresAt: token.ExpiresAt,
            UserId: user.Id,
            Username: user.Username,
            FullName: user.FullName,
            Role: user.Role.ToString()));
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

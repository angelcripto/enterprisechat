using EnterpriseChat.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseChat.Server.Auth.Providers;

/// <summary>
/// Provider por defecto: lee usuarios de la BD SQLite local del server.
/// Es el único que también <em>escribe</em> hash (rotación de cost factor
/// BCrypt) — el endpoint de login se encarga del SaveChanges; este
/// método solo señaliza <see cref="AuthResult.NeedsRehash"/>.
///
/// Importante: el admin SIEMPRE vive aquí (ver <c>AdminSeeder</c>). El
/// endpoint de login fuerza el uso de este provider cuando
/// <c>username == "admin"</c> aunque haya otros providers habilitados,
/// para no depender de un MySQL externo si se cae.
/// </summary>
public sealed class InternalAuthProvider : IAuthProvider
{
    public const int InternalProviderId = 0;

    private readonly IDbContextFactory<ChatDbContext> _dbFactory;
    private readonly IPasswordHasher _hasher;

    public InternalAuthProvider(IDbContextFactory<ChatDbContext> dbFactory, IPasswordHasher hasher)
    {
        _dbFactory = dbFactory;
        _hasher = hasher;
    }

    public AuthProviderKind Kind => AuthProviderKind.Internal;
    public int ProviderId => InternalProviderId;
    public string DisplayName => "Interno (SQLite local)";

    public async Task<AuthResult> VerifyAsync(string username, string password, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var user = await db.Users
            .Where(u => u.Username == username && u.IsActive)
            .Select(u => new { u.Id, u.PasswordHash })
            .SingleOrDefaultAsync(ct);

        if (user is null)
        {
            return AuthResult.UnknownUser();
        }

        var verify = _hasher.Verify(password, user.PasswordHash);
        if (!verify.Success)
        {
            return AuthResult.BadPassword();
        }

        // ExternalId queda null para usuarios locales: ya tenemos su PK.
        return AuthResult.Success(externalId: null, needsRehash: verify.NeedsRehash);
    }
}

using EnterpriseChat.Server.Auth;
using EnterpriseChat.Server.Data;
using EnterpriseChat.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseChat.Server.Bootstrap;

internal static class AdminSeeder
{
    public const string AdminPasswordConfigKey = "EnterpriseChat:Bootstrap:AdminPassword";
    public const string DefaultAdminUsername = "admin";

    /// <summary>
    /// Usuario sistema que recibe la reasignación de mensajes /
    /// adjuntos / salas creadas cuando un user se borra en hard. No
    /// se usa para login (IsActive=false, PasswordHash sentinela) y
    /// nunca aparece en directorios.
    /// </summary>
    public const string DeletedUserUsername = "_deleted";

    public static async Task SeedAdminIfEmptyAsync(
        IServiceProvider services,
        IConfiguration config,
        ILogger logger,
        CancellationToken ct = default)
    {
        var factory = services.GetRequiredService<IDbContextFactory<ChatDbContext>>();
        await using var db = await factory.CreateDbContextAsync(ct);

        // Seed del usuario sistema _deleted en CADA arranque (es idempotente):
        // lo necesitamos disponible aunque la BD ya tenga otros usuarios para
        // poder hacer hard delete.
        var deletedUser = await db.Users.SingleOrDefaultAsync(u => u.Username == DeletedUserUsername, ct);
        if (deletedUser is null)
        {
            db.Users.Add(new User
            {
                Username = DeletedUserUsername,
                FullName = "Usuario eliminado",
                Role = UserRole.User,
                PasswordHash = "(system)",
                IsActive = false,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Usuario sistema '{Name}' creado para anonimización.", DeletedUserUsername);
        }

        // Solo el "primer admin" si NO hay otros usuarios humanos.
        var anyHuman = await db.Users.AnyAsync(u => u.Username != DeletedUserUsername, ct);
        if (anyHuman)
        {
            return;
        }

        var adminPassword = config[AdminPasswordConfigKey];
        if (string.IsNullOrWhiteSpace(adminPassword))
        {
            logger.LogWarning(
                "No hay usuarios y {Key} no está definido. Configura una contraseña inicial para crear el admin.",
                AdminPasswordConfigKey);
            return;
        }

        var hasher = services.GetRequiredService<IPasswordHasher>();
        var admin = new User
        {
            Username = DefaultAdminUsername,
            FullName = "Administrador",
            Role = UserRole.Admin,
            PasswordHash = hasher.Hash(adminPassword),
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            // SourceProviderId = null marca al admin como usuario local
            // de rescate: AuthEndpoints lo enruta SIEMPRE al provider
            // Internal aunque haya providers externos configurados.
            SourceProviderId = null,
            ExternalId = null,
        };
        db.Users.Add(admin);
        db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = null,
            Action = "user.bootstrap",
            Target = admin.Username
        });
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Usuario admin inicial creado ({Username}). Cambia su contraseña tras el primer login.",
            admin.Username);
    }
}

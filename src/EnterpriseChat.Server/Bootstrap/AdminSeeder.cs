using EnterpriseChat.Server.Auth;
using EnterpriseChat.Server.Data;
using EnterpriseChat.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseChat.Server.Bootstrap;

internal static class AdminSeeder
{
    public const string AdminPasswordConfigKey = "EnterpriseChat:Bootstrap:AdminPassword";
    public const string DefaultAdminUsername = "admin";

    public static async Task SeedAdminIfEmptyAsync(
        IServiceProvider services,
        IConfiguration config,
        ILogger logger,
        CancellationToken ct = default)
    {
        var factory = services.GetRequiredService<IDbContextFactory<ChatDbContext>>();
        await using var db = await factory.CreateDbContextAsync(ct);

        var anyUser = await db.Users.AnyAsync(ct);
        if (anyUser)
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
            CreatedAt = DateTimeOffset.UtcNow
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

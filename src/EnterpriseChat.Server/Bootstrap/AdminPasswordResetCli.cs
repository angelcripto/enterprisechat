using EnterpriseChat.Server.Auth;
using EnterpriseChat.Server.Data;
using EnterpriseChat.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseChat.Server.Bootstrap;

/// <summary>
/// CLI helper to recover access when the persisted admin password no longer
/// matches anything the operator can type. Invoked with
/// <c>--reset-admin-password &lt;newpass&gt;</c>: the process opens the database,
/// rewrites the BCrypt hash of the <c>admin</c> account and exits without
/// starting Kestrel.
///
/// Intentionally separate from <see cref="AdminSeeder"/> because the seeder
/// short-circuits when any user exists — by design — so it cannot reset an
/// existing account.
/// </summary>
internal static class AdminPasswordResetCli
{
    private const string Flag = "--reset-admin-password";

    public static bool TryExtractPassword(string[] args, out string newPassword, out string[] remaining)
    {
        newPassword = string.Empty;
        var list = new List<string>(args.Length);
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].Equals(Flag, StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    throw new ArgumentException($"{Flag} requires a password argument.");
                }
                newPassword = args[i + 1];
                i++; // skip the value too
                continue;
            }
            list.Add(args[i]);
        }
        remaining = list.ToArray();
        return !string.IsNullOrEmpty(newPassword);
    }

    public static async Task<int> RunAsync(string newPassword)
    {
        if (newPassword.Length < 4)
        {
            Console.Error.WriteLine("La nueva contraseña debe tener al menos 4 caracteres.");
            return 2;
        }

        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        builder.Services.AddChatPersistence(builder.Configuration);
        builder.Services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();

        await using var app = builder.Build();
        await app.Services.InitializeChatDatabaseAsync();

        var factory = app.Services.GetRequiredService<IDbContextFactory<ChatDbContext>>();
        var hasher = app.Services.GetRequiredService<IPasswordHasher>();

        await using var db = await factory.CreateDbContextAsync();
        var admin = await db.Users
            .Where(u => u.Username == AdminSeeder.DefaultAdminUsername)
            .SingleOrDefaultAsync();

        if (admin is null)
        {
            // No admin row at all: seed one fresh with the requested password so
            // the operator does not have to wipe the database manually.
            admin = new User
            {
                Username = AdminSeeder.DefaultAdminUsername,
                FullName = "Administrador",
                Role = UserRole.Admin,
                PasswordHash = hasher.Hash(newPassword),
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            };
            db.Users.Add(admin);
            db.AuditLogs.Add(new AuditLog { ActorUserId = null, Action = "user.reset.created", Target = admin.Username });
            await db.SaveChangesAsync();
            Console.WriteLine($"Usuario '{AdminSeeder.DefaultAdminUsername}' creado con la nueva contraseña.");
            return 0;
        }

        admin.PasswordHash = hasher.Hash(newPassword);
        admin.IsActive = true;
        db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = admin.Id,
            Action = "user.password.reset",
            Target = admin.Username,
            Details = "via --reset-admin-password CLI"
        });
        await db.SaveChangesAsync();

        Console.WriteLine($"Contraseña de '{admin.Username}' restablecida correctamente.");
        return 0;
    }
}

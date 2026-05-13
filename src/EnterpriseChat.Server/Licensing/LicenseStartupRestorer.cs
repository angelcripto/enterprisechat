using EnterpriseChat.Licensing.Abstractions;
using EnterpriseChat.Server.Data;
using EnterpriseChat.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseChat.Server.Licensing;

/// <summary>
/// On startup, if the local DB has an active <see cref="LicenseRecord"/>, hand
/// its raw token to <see cref="ILicenseAdministrator"/>. The Pro plugin will
/// re-validate signature and expiry; the Free stub will simply do nothing
/// since this code path only runs when a Pro license was already applied.
/// </summary>
internal static class LicenseStartupRestorer
{
    public static async Task RestoreAsync(IServiceProvider services, ILogger logger, CancellationToken ct = default)
    {
        var admin = services.GetRequiredService<ILicenseAdministrator>();
        var factory = services.GetRequiredService<IDbContextFactory<ChatDbContext>>();

        await admin.RestoreFromStorageAsync(ct);

        await using var db = await factory.CreateDbContextAsync(ct);
        // SQLite cannot ORDER BY DateTimeOffset; rely on the monotonic Id instead.
        var active = await db.Licenses
            .Where(l => l.Status == LicenseRecordStatus.Active)
            .OrderByDescending(l => l.Id)
            .FirstOrDefaultAsync(ct);

        if (active is null)
        {
            logger.LogInformation("Sin licencia Pro almacenada; servidor arranca en edición Free.");
            return;
        }

        var result = await admin.ApplyAsync(active.RawToken, ct);
        if (result.Success)
        {
            logger.LogInformation(
                "Licencia Pro restaurada para {LicensedTo} ({MaxUsers} usuarios, expira {ExpiresAt:O}).",
                active.LicensedTo,
                active.MaxUsers,
                active.ExpiresAt);
        }
        else
        {
            logger.LogWarning(
                "Licencia almacenada para {LicensedTo} dejó de ser válida: {Reason}. Pasa a estado Superseded.",
                active.LicensedTo,
                result.ErrorMessage);
            active.Status = LicenseRecordStatus.Superseded;
            await db.SaveChangesAsync(ct);
        }
    }
}

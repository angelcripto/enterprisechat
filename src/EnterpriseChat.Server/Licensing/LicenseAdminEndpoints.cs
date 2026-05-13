using System.Globalization;
using System.Security.Claims;
using EnterpriseChat.Licensing.Abstractions;
using EnterpriseChat.Protocol.Licensing;
using EnterpriseChat.Server.Data;
using EnterpriseChat.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseChat.Server.Licensing;

internal static class LicenseAdminEndpoints
{
    public static void MapLicenseAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/admin/license")
            .WithTags("Licensing")
            .RequireAuthorization(p => p.RequireRole(UserRole.Admin.ToString()));

        group.MapPost("/", ApplyAsync);
        group.MapDelete("/", ClearAsync);
    }

    private static async Task<IResult> ApplyAsync(
        ApplyLicenseRequest request,
        ILicenseAdministrator admin,
        IDbContextFactory<ChatDbContext> dbFactory,
        ClaimsPrincipal principal,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Serial))
        {
            return Results.BadRequest(new ApplyLicenseResponse(
                Success: false,
                ErrorMessage: "Falta el serial.",
                Info: null));
        }

        var result = await admin.ApplyAsync(request.Serial.Trim(), ct);

        if (result.Success && result.Info is { } info)
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            // Mark any previously active license as superseded.
            var actives = await db.Licenses
                .Where(l => l.Status == LicenseRecordStatus.Active)
                .ToListAsync(ct);
            foreach (var a in actives)
            {
                a.Status = LicenseRecordStatus.Superseded;
            }

            // jti uniqueness — if this serial was applied previously and cleared,
            // re-activating it just flips status back.
            var existing = await db.Licenses
                .FirstOrDefaultAsync(l => l.Jti == (info.LicenseId ?? ""), ct);
            if (existing is null)
            {
                db.Licenses.Add(new LicenseRecord
                {
                    Jti = info.LicenseId ?? Guid.NewGuid().ToString("N"),
                    RawToken = request.Serial.Trim(),
                    LicensedTo = info.LicensedTo,
                    MaxUsers = info.MaxConcurrentUsers,
                    IssuedAt = DateTimeOffset.UtcNow,
                    ExpiresAt = info.ExpiresAt ?? DateTimeOffset.MaxValue,
                    AppliedAt = DateTimeOffset.UtcNow,
                    AppliedByUserId = TryGetActorId(principal),
                    Status = LicenseRecordStatus.Active
                });
            }
            else
            {
                existing.Status = LicenseRecordStatus.Active;
                existing.RawToken = request.Serial.Trim();
                existing.AppliedAt = DateTimeOffset.UtcNow;
                existing.AppliedByUserId = TryGetActorId(principal);
                existing.LicensedTo = info.LicensedTo;
                existing.MaxUsers = info.MaxConcurrentUsers;
                existing.ExpiresAt = info.ExpiresAt ?? DateTimeOffset.MaxValue;
            }

            db.AuditLogs.Add(new AuditLog
            {
                ActorUserId = TryGetActorId(principal),
                Action = "license.apply",
                Target = info.LicensedTo,
                Details = $"jti={info.LicenseId}, max_users={info.MaxConcurrentUsers}, expires={info.ExpiresAt:O}"
            });

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        return Results.Json(
            new ApplyLicenseResponse(result.Success, result.ErrorMessage, result.Info),
            statusCode: result.Success ? 200 : 400);
    }

    private static async Task<IResult> ClearAsync(
        ILicenseAdministrator admin,
        IDbContextFactory<ChatDbContext> dbFactory,
        ClaimsPrincipal principal,
        CancellationToken ct)
    {
        await admin.ClearAsync(ct);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var actives = await db.Licenses
            .Where(l => l.Status == LicenseRecordStatus.Active)
            .ToListAsync(ct);
        foreach (var a in actives)
        {
            a.Status = LicenseRecordStatus.Cleared;
        }
        db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = TryGetActorId(principal),
            Action = "license.clear",
            Target = null
        });
        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }

    private static int? TryGetActorId(ClaimsPrincipal principal)
    {
        var raw = principal.FindFirstValue("sub") ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(raw, CultureInfo.InvariantCulture, out var id) ? id : null;
    }
}

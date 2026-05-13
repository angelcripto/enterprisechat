using System.Globalization;
using System.Security.Claims;
using EnterpriseChat.Protocol.Files;
using EnterpriseChat.Server.Data;
using EnterpriseChat.Server.Data.Entities;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseChat.Server.Files;

internal static class FileEndpoints
{
    public const string AttachmentsSubDir = "data/attachments";
    public const long DefaultMaxBytes = 10L * 1024 * 1024; // 10 MB

    public static void MapFileEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/files").WithTags("Files").RequireAuthorization();

        group.MapPost("/", UploadAsync)
            .DisableAntiforgery();
        group.MapGet("/{id:long}", DownloadAsync);
    }

    private static async Task<IResult> UploadAsync(
        [FromForm] IFormFile file,
        ClaimsPrincipal principal,
        IDbContextFactory<ChatDbContext> dbFactory,
        IConfiguration config,
        IWebHostEnvironment env,
        CancellationToken ct)
    {
        if (file is null || file.Length <= 0)
        {
            return Results.BadRequest(new { error = "Archivo vacío." });
        }

        var maxBytes = config.GetValue<long?>("EnterpriseChat:Server:MaxAttachmentSizeBytes") ?? DefaultMaxBytes;
        if (file.Length > maxBytes)
        {
            return Results.BadRequest(new
            {
                error = $"El archivo supera el máximo permitido ({maxBytes / (1024 * 1024)} MB)."
            });
        }

        var meRaw = principal.FindFirstValue("sub") ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(meRaw, CultureInfo.InvariantCulture, out var meId))
        {
            return Results.Unauthorized();
        }

        var attachmentsDir = Path.Combine(env.ContentRootPath, AttachmentsSubDir);
        Directory.CreateDirectory(attachmentsDir);

        var safeName = SanitizeFileName(file.FileName);
        var storageName = $"{Guid.NewGuid():N}_{safeName}";
        var fullPath = Path.Combine(attachmentsDir, storageName);
        await using (var fs = File.Create(fullPath))
        {
            await file.CopyToAsync(fs, ct);
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var entity = new Attachment
        {
            FileName = safeName,
            MimeType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
            SizeBytes = file.Length,
            StoragePath = storageName,
            UploadedByUserId = meId,
            UploadedAt = DateTimeOffset.UtcNow
        };
        db.Attachments.Add(entity);
        db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = meId,
            Action = "file.upload",
            Target = entity.FileName,
            Details = entity.SizeBytes.ToString(CultureInfo.InvariantCulture)
        });
        await db.SaveChangesAsync(ct);

        return Results.Ok(new AttachmentSummary(
            Id: entity.Id,
            FileName: entity.FileName,
            MimeType: entity.MimeType,
            SizeBytes: entity.SizeBytes,
            UploadedByUserId: entity.UploadedByUserId,
            UploadedAt: entity.UploadedAt));
    }

    private static async Task<IResult> DownloadAsync(
        long id,
        ClaimsPrincipal principal,
        IDbContextFactory<ChatDbContext> dbFactory,
        IWebHostEnvironment env,
        CancellationToken ct)
    {
        var meRaw = principal.FindFirstValue("sub") ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(meRaw, CultureInfo.InvariantCulture, out var meId))
        {
            return Results.Unauthorized();
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var attachment = await db.Attachments.FindAsync(new object?[] { id }, ct);
        if (attachment is null)
        {
            return Results.NotFound();
        }

        // Authorize: uploader OR recipient/room member of a message linked to this attachment.
        var allowed = attachment.UploadedByUserId == meId
            || await db.Messages.AnyAsync(m => m.AttachmentId == id &&
                (m.FromUserId == meId
                 || m.ToUserId == meId
                 || (m.RoomId != null && db.RoomMembers.Any(rm => rm.RoomId == m.RoomId && rm.UserId == meId))), ct);

        if (!allowed)
        {
            return Results.Forbid();
        }

        var fullPath = Path.Combine(env.ContentRootPath, AttachmentsSubDir, attachment.StoragePath);
        if (!File.Exists(fullPath))
        {
            return Results.NotFound();
        }

        var stream = File.OpenRead(fullPath);
        return Results.File(stream, attachment.MimeType, attachment.FileName, enableRangeProcessing: true);
    }

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "archivo.bin";
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Where(c => !invalid.Contains(c)).ToArray());
        if (cleaned.Length > 200) cleaned = cleaned[..200];
        return string.IsNullOrWhiteSpace(cleaned) ? "archivo.bin" : cleaned;
    }
}

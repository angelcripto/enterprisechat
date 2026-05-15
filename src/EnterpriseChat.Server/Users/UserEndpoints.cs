using System.Globalization;
using System.Security.Claims;
using EnterpriseChat.Protocol;
using EnterpriseChat.Server.Data;
using EnterpriseChat.Server.Licensing;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseChat.Server.Users;

internal static class UserEndpoints
{
    /// <summary>Avatars live on disk so they can be served as static-ish content
    /// without hitting the database on every render. The DB only stores the
    /// file name.</summary>
    private const string AvatarFolder = "data/avatars";

    private static readonly HashSet<string> AllowedAvatarTypes =
        new(StringComparer.OrdinalIgnoreCase) { "image/png", "image/jpeg", "image/webp" };
    private const long MaxAvatarBytes = 2 * 1024 * 1024;

    public static void MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/users").WithTags("Users").RequireAuthorization();

        group.MapGet("/", ListUsersAsync);

        // Avatar serving is intentionally public so <img src> tags from the SPA
        // (which can't set Authorization headers easily) work without an auth
        // header. The file content is non-sensitive (a profile picture) and
        // the URL space is bounded by integer user IDs.
        var avatars = app.MapGroup("/users").WithTags("Users");
        avatars.MapGet("/{id:int}/avatar", GetAvatarAsync).AllowAnonymous();

        group.MapPost("/me/avatar", UploadOwnAvatarAsync).DisableAntiforgery();
        group.MapDelete("/me/avatar", DeleteOwnAvatarAsync);
    }

    private static async Task<IResult> ListUsersAsync(
        ClaimsPrincipal principal,
        IDbContextFactory<ChatDbContext> dbFactory,
        ConcurrentSessionCounter sessions,
        CancellationToken ct)
    {
        var meRaw = principal.FindFirstValue("sub") ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var meId = int.TryParse(meRaw, CultureInfo.InvariantCulture, out var parsed) ? parsed : (int?)null;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var users = await db.Users
            .Where(u => u.IsActive)
            .Include(u => u.Department)
            .OrderBy(u => u.FullName)
            .Select(u => new
            {
                u.Id,
                u.Username,
                u.FullName,
                Department = u.Department != null ? u.Department.Name : null,
                Role = u.Role.ToString(),
                HasAvatar = u.AvatarFileName != null,
            })
            .ToListAsync(ct);

        // Per-peer unread DM count for the caller. SQLite + EF can do this in
        // one query — group all unread DMs addressed to me by their author.
        // Rooms aren't tracked yet (would need a per-user read cursor) so
        // their unread count is left at 0 by the caller.
        var unreadByPeer = meId is null
            ? new Dictionary<int, int>()
            : await db.Messages
                .Where(m => m.ToUserId == meId && m.ReadAt == null && m.FromUserId != meId)
                .GroupBy(m => m.FromUserId)
                .Select(g => new { PeerId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.PeerId, x => x.Count, ct);

        // Peers con los que el usuario actual ha intercambiado algún DM.
        // El sidebar usa esto para mostrar solo conversaciones reales en
        // "Mensajes directos" en lugar de todo el directorio.
        var dmPeers = meId is null
            ? new HashSet<int>()
            : new HashSet<int>(await db.Messages
                .Where(m => (m.FromUserId == meId && m.ToUserId != null)
                         || (m.ToUserId == meId && m.FromUserId != meId))
                .Select(m => m.FromUserId == meId ? m.ToUserId!.Value : m.FromUserId)
                .Distinct()
                .ToListAsync(ct));

        // Include self so the SPA can render the current user's avatar.
        var result = users
            .Select(u => new UserSummary(
                Id: u.Id,
                Username: u.Username,
                FullName: u.FullName,
                Department: u.Department,
                Role: u.Role,
                IsOnline: sessions.IsOnline(u.Id) || u.Id == meId,
                HasAvatar: u.HasAvatar,
                UnreadDirectMessages: unreadByPeer.TryGetValue(u.Id, out var cnt) ? cnt : 0,
                HasDmConversation: dmPeers.Contains(u.Id)))
            .ToArray();

        return Results.Ok(result);
    }

    private static async Task<IResult> GetAvatarAsync(
        int id,
        IDbContextFactory<ChatDbContext> dbFactory,
        CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var fileName = await db.Users
            .Where(u => u.Id == id && u.IsActive)
            .Select(u => u.AvatarFileName)
            .FirstOrDefaultAsync(ct);
        if (string.IsNullOrEmpty(fileName))
        {
            return Results.NotFound();
        }
        var path = Path.Combine(AvatarFolder, fileName);
        if (!File.Exists(path))
        {
            return Results.NotFound();
        }
        var contentType = Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "image/jpeg",
        };
        return Results.File(path, contentType, enableRangeProcessing: true);
    }

    private static async Task<IResult> UploadOwnAvatarAsync(
        ClaimsPrincipal principal,
        IFormFile file,
        IDbContextFactory<ChatDbContext> dbFactory,
        CancellationToken ct)
    {
        var meId = ParseUserId(principal);
        if (meId is null) return Results.Unauthorized();

        if (file.Length == 0) return Results.BadRequest(new { error = "Archivo vacío." });
        if (file.Length > MaxAvatarBytes) return Results.BadRequest(new { error = "Máximo 2 MB." });
        if (!AllowedAvatarTypes.Contains(file.ContentType)) return Results.BadRequest(new { error = "Solo PNG, JPEG o WebP." });

        Directory.CreateDirectory(AvatarFolder);
        var ext = file.ContentType switch
        {
            "image/png" => ".png",
            "image/webp" => ".webp",
            _ => ".jpg",
        };
        var fileName = $"{meId}{ext}";
        var fullPath = Path.Combine(AvatarFolder, fileName);

        await using (var fs = File.Create(fullPath))
        {
            await file.CopyToAsync(fs, ct);
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var user = await db.Users.FirstAsync(u => u.Id == meId, ct);
        // Different extension → previous file orphaned, clean it.
        if (!string.IsNullOrEmpty(user.AvatarFileName) && user.AvatarFileName != fileName)
        {
            var old = Path.Combine(AvatarFolder, user.AvatarFileName);
            if (File.Exists(old)) { try { File.Delete(old); } catch { /* best-effort */ } }
        }
        user.AvatarFileName = fileName;
        await db.SaveChangesAsync(ct);

        return Results.Ok(new { hasAvatar = true });
    }

    private static async Task<IResult> DeleteOwnAvatarAsync(
        ClaimsPrincipal principal,
        IDbContextFactory<ChatDbContext> dbFactory,
        CancellationToken ct)
    {
        var meId = ParseUserId(principal);
        if (meId is null) return Results.Unauthorized();

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var user = await db.Users.FirstAsync(u => u.Id == meId, ct);
        if (!string.IsNullOrEmpty(user.AvatarFileName))
        {
            var path = Path.Combine(AvatarFolder, user.AvatarFileName);
            if (File.Exists(path)) { try { File.Delete(path); } catch { /* best-effort */ } }
            user.AvatarFileName = null;
            await db.SaveChangesAsync(ct);
        }
        return Results.NoContent();
    }

    private static int? ParseUserId(ClaimsPrincipal principal)
    {
        var raw = principal.FindFirstValue("sub") ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(raw, CultureInfo.InvariantCulture, out var v) ? v : null;
    }
}

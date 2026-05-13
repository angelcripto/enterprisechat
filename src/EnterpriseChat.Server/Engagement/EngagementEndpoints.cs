using System.Globalization;
using System.Security.Claims;
using EnterpriseChat.Protocol;
using EnterpriseChat.Protocol.Files;
using EnterpriseChat.Server.Data;
using EnterpriseChat.Server.Data.Entities;
using EnterpriseChat.Server.Hubs;
using EnterpriseChat.Server.Licensing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseChat.Server.Engagement;

/// <summary>
/// Endpoints that power the SPA "engagement" surface: pinned messages,
/// reactions, the shared-files panel of a room, and the metrics widget.
///
/// They live in one file because they share the same authorisation pattern
/// (member of the room) and the same persistence factory; splitting would
/// require duplicating the helpers.
/// </summary>
internal static class EngagementEndpoints
{
    public static void MapEngagementEndpoints(this IEndpointRouteBuilder app)
    {
        var rooms = app.MapGroup("/rooms").RequireAuthorization();
        rooms.MapGet("/{roomId:int}/files", ListRoomFilesAsync);
        rooms.MapGet("/{roomId:int}/pinned", ListPinnedAsync);
        rooms.MapPost("/{roomId:int}/pinned/{messageId:long}", PinAsync);
        rooms.MapDelete("/{roomId:int}/pinned/{messageId:long}", UnpinAsync);

        var messages = app.MapGroup("/messages").RequireAuthorization();
        messages.MapGet("/{messageId:long}/reactions", ListReactionsAsync);
        messages.MapPost("/{messageId:long}/reactions", ToggleReactionAsync);

        app.MapGet("/metrics", GetMetricsAsync).RequireAuthorization();
    }

    // -----------------------------------------------------------------
    //  Room files
    // -----------------------------------------------------------------
    private static async Task<IResult> ListRoomFilesAsync(
        int roomId,
        ClaimsPrincipal principal,
        IDbContextFactory<ChatDbContext> dbFactory,
        CancellationToken ct)
    {
        var meId = ParseUserId(principal);
        if (meId is null) return Results.Unauthorized();

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var isMember = await db.RoomMembers.AnyAsync(m => m.RoomId == roomId && m.UserId == meId, ct);
        if (!isMember) return Results.Forbid();

        var rows = await db.Messages
            .Where(m => m.RoomId == roomId && m.AttachmentId != null)
            .OrderByDescending(m => m.Id)
            .Take(200)
            .Select(m => new AttachmentSummary(
                m.Attachment!.Id,
                m.Attachment.FileName,
                m.Attachment.MimeType,
                m.Attachment.SizeBytes,
                m.Attachment.UploadedByUserId,
                m.Attachment.UploadedAt))
            .ToListAsync(ct);

        return Results.Ok(rows);
    }

    // -----------------------------------------------------------------
    //  Pinned
    // -----------------------------------------------------------------
    private static async Task<IResult> ListPinnedAsync(
        int roomId,
        ClaimsPrincipal principal,
        IDbContextFactory<ChatDbContext> dbFactory,
        CancellationToken ct)
    {
        var meId = ParseUserId(principal);
        if (meId is null) return Results.Unauthorized();

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var isMember = await db.RoomMembers.AnyAsync(m => m.RoomId == roomId && m.UserId == meId, ct);
        if (!isMember) return Results.Forbid();

        var rows = await db.PinnedMessages
            .Where(p => p.RoomId == roomId)
            .Include(p => p.Message)
            .OrderByDescending(p => p.PinnedAt)
            .Select(p => new PinnedSummary(
                p.RoomId,
                p.MessageId,
                p.PinnedByUserId,
                p.PinnedAt,
                p.Message.FromUserId,
                p.Message.Body,
                p.Message.SentAt))
            .ToListAsync(ct);
        // Named args here are fine because this is outside an expression tree.
        return Results.Ok(rows);
    }

    private static async Task<IResult> PinAsync(
        int roomId,
        long messageId,
        ClaimsPrincipal principal,
        IDbContextFactory<ChatDbContext> dbFactory,
        IHubContext<ChatHub, IChatClient> hub,
        CancellationToken ct)
    {
        var meId = ParseUserId(principal);
        if (meId is null) return Results.Unauthorized();

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var isMember = await db.RoomMembers.AnyAsync(m => m.RoomId == roomId && m.UserId == meId, ct);
        if (!isMember) return Results.Forbid();

        var msgExists = await db.Messages.AnyAsync(m => m.Id == messageId && m.RoomId == roomId, ct);
        if (!msgExists) return Results.NotFound();

        var already = await db.PinnedMessages.AnyAsync(p => p.RoomId == roomId && p.MessageId == messageId, ct);
        if (!already)
        {
            db.PinnedMessages.Add(new PinnedMessage
            {
                RoomId = roomId,
                MessageId = messageId,
                PinnedByUserId = meId.Value,
                PinnedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync(ct);
        }

        await hub.Clients.Group(ChatHub.RoomGroupName(roomId))
            .OnPinnedChanged(roomId, messageId, pinned: true);
        return Results.NoContent();
    }

    private static async Task<IResult> UnpinAsync(
        int roomId,
        long messageId,
        ClaimsPrincipal principal,
        IDbContextFactory<ChatDbContext> dbFactory,
        IHubContext<ChatHub, IChatClient> hub,
        CancellationToken ct)
    {
        var meId = ParseUserId(principal);
        if (meId is null) return Results.Unauthorized();

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var isMember = await db.RoomMembers.AnyAsync(m => m.RoomId == roomId && m.UserId == meId, ct);
        if (!isMember) return Results.Forbid();

        var entity = await db.PinnedMessages.FirstOrDefaultAsync(p => p.RoomId == roomId && p.MessageId == messageId, ct);
        if (entity is not null)
        {
            db.PinnedMessages.Remove(entity);
            await db.SaveChangesAsync(ct);
        }

        await hub.Clients.Group(ChatHub.RoomGroupName(roomId))
            .OnPinnedChanged(roomId, messageId, pinned: false);
        return Results.NoContent();
    }

    // -----------------------------------------------------------------
    //  Reactions
    // -----------------------------------------------------------------
    private static async Task<IResult> ListReactionsAsync(
        long messageId,
        ClaimsPrincipal principal,
        IDbContextFactory<ChatDbContext> dbFactory,
        CancellationToken ct)
    {
        var meId = ParseUserId(principal);
        if (meId is null) return Results.Unauthorized();

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        // The caller must be able to see the message (member of the room or
        // recipient/author of a DM). We let the existing message access
        // filter through and short-circuit on visibility.
        var visible = await db.Messages
            .AnyAsync(m => m.Id == messageId
                && (m.FromUserId == meId
                    || m.ToUserId == meId
                    || (m.RoomId != null && db.RoomMembers.Any(r => r.RoomId == m.RoomId && r.UserId == meId))), ct);
        if (!visible) return Results.Forbid();

        var grouped = await db.MessageReactions
            .Where(r => r.MessageId == messageId)
            .GroupBy(r => r.Emoji)
            .Select(g => new
            {
                Emoji = g.Key,
                Count = g.Count(),
                Mine = g.Any(r => r.UserId == meId),
            })
            .ToListAsync(ct);

        var result = grouped
            .Select(g => new ReactionSummary(messageId, g.Emoji, g.Count, g.Mine))
            .ToArray();
        return Results.Ok(result);
    }

    public sealed record ToggleReactionRequest(string Emoji);

    private static async Task<IResult> ToggleReactionAsync(
        long messageId,
        ToggleReactionRequest body,
        ClaimsPrincipal principal,
        IDbContextFactory<ChatDbContext> dbFactory,
        IHubContext<ChatHub, IChatClient> hub,
        CancellationToken ct)
    {
        var meId = ParseUserId(principal);
        if (meId is null) return Results.Unauthorized();

        var emoji = body.Emoji?.Trim();
        if (string.IsNullOrEmpty(emoji) || emoji.Length > 32) return Results.BadRequest();

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var msg = await db.Messages.FirstOrDefaultAsync(m => m.Id == messageId, ct);
        if (msg is null) return Results.NotFound();

        var canSee = msg.FromUserId == meId
            || msg.ToUserId == meId
            || (msg.RoomId != null && await db.RoomMembers.AnyAsync(r => r.RoomId == msg.RoomId && r.UserId == meId, ct));
        if (!canSee) return Results.Forbid();

        var existing = await db.MessageReactions
            .FirstOrDefaultAsync(r => r.MessageId == messageId && r.UserId == meId && r.Emoji == emoji, ct);
        bool added;
        if (existing is null)
        {
            db.MessageReactions.Add(new MessageReaction
            {
                MessageId = messageId,
                UserId = meId.Value,
                Emoji = emoji,
                ReactedAt = DateTimeOffset.UtcNow,
            });
            added = true;
        }
        else
        {
            db.MessageReactions.Remove(existing);
            added = false;
        }
        await db.SaveChangesAsync(ct);

        // Broadcast to room members for room messages; to peer + self for DMs.
        if (msg.RoomId is int rid)
        {
            await hub.Clients.Group(ChatHub.RoomGroupName(rid))
                .OnReactionChanged(messageId, meId.Value, emoji, added);
        }
        else
        {
            await hub.Clients.User(msg.FromUserId.ToString(CultureInfo.InvariantCulture))
                .OnReactionChanged(messageId, meId.Value, emoji, added);
            if (msg.ToUserId is int peer)
            {
                await hub.Clients.User(peer.ToString(CultureInfo.InvariantCulture))
                    .OnReactionChanged(messageId, meId.Value, emoji, added);
            }
        }

        return Results.Ok(new { added });
    }

    // -----------------------------------------------------------------
    //  Metrics
    // -----------------------------------------------------------------
    private static async Task<IResult> GetMetricsAsync(
        IDbContextFactory<ChatDbContext> dbFactory,
        ConcurrentSessionCounter sessions,
        EnterpriseChat.Licensing.Abstractions.ILicenseValidator licensing,
        CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var activeUsers = await db.Users.CountAsync(u => u.IsActive, ct);
        var maxUsers = licensing.Current.MaxConcurrentUsers;
        var messages = await db.Messages.CountAsync(ct);
        var rooms = await db.Rooms.CountAsync(ct);
        var storageUsed = ComputeStorageUsed();
        // Quota: 1 GB for Free, 10 GB for Pro — placeholder until a real
        // quota table exists.
        var quota = licensing.Current.Edition.ToString() == "Pro"
            ? 10L * 1024 * 1024 * 1024
            : 1L * 1024 * 1024 * 1024;
        _ = sessions;
        return Results.Ok(new ServerMetrics(
            ActiveUsers: activeUsers,
            MaxUsers: maxUsers,
            StorageUsedBytes: storageUsed,
            StorageQuotaBytes: quota,
            MessageCount: messages,
            RoomCount: rooms));
    }

    private static long ComputeStorageUsed()
    {
        try
        {
            var folders = new[] { "data/attachments", "data/avatars" };
            long total = 0;
            foreach (var folder in folders)
            {
                if (!Directory.Exists(folder)) continue;
                foreach (var f in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
                {
                    try { total += new FileInfo(f).Length; } catch { /* race */ }
                }
            }
            return total;
        }
        catch
        {
            return 0;
        }
    }

    private static int? ParseUserId(ClaimsPrincipal principal)
    {
        var raw = principal.FindFirstValue("sub") ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(raw, CultureInfo.InvariantCulture, out var v) ? v : null;
    }
}

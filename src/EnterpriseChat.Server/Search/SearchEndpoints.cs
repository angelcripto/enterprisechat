using System.Globalization;
using System.Security.Claims;
using EnterpriseChat.Protocol.Search;
using EnterpriseChat.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseChat.Server.Search;

internal static class SearchEndpoints
{
    public static void MapSearchEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/search").WithTags("Search").RequireAuthorization();
        group.MapGet("/", SearchAsync);
    }

    private static async Task<IResult> SearchAsync(
        string q,
        int limit,
        ClaimsPrincipal principal,
        IDbContextFactory<ChatDbContext> dbFactory,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
        {
            return Results.BadRequest(new { error = "Consulta demasiado corta (mín 2 caracteres)." });
        }
        if (limit is <= 0 or > 200) limit = 50;

        var meRaw = principal.FindFirstValue("sub") ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(meRaw, CultureInfo.InvariantCulture, out var meId))
        {
            return Results.Unauthorized();
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var myRoomIds = await db.RoomMembers
            .Where(m => m.UserId == meId)
            .Select(m => m.RoomId)
            .ToListAsync(ct);

        var needle = $"%{q.Trim()}%";

        var rows = await db.Messages
            .Where(m =>
                EF.Functions.Like(m.Body, needle)
                && (m.FromUserId == meId
                    || m.ToUserId == meId
                    || (m.RoomId != null && myRoomIds.Contains(m.RoomId.Value))))
            .Include(m => m.FromUser)
            .OrderByDescending(m => m.Id)
            .Take(limit)
            .Select(m => new
            {
                m.Id,
                m.FromUserId,
                FromUsername = m.FromUser.Username,
                m.ToUserId,
                m.RoomId,
                RoomName = m.RoomId != null ? db.Rooms.Where(r => r.Id == m.RoomId).Select(r => r.Name).FirstOrDefault() : null,
                m.Body,
                m.SentAt
            })
            .ToListAsync(ct);

        var hits = rows.Select(r => new SearchHit(
            ServerId: r.Id,
            FromUserId: r.FromUserId,
            FromUsername: r.FromUsername,
            ToUserId: r.ToUserId,
            RoomId: r.RoomId,
            RoomName: r.RoomName,
            Body: r.Body,
            SentAt: r.SentAt)).ToArray();

        return Results.Ok(new SearchResponse(q, hits));
    }
}

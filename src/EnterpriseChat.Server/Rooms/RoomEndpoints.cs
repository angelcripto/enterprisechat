using System.Globalization;
using System.Security.Claims;
using EnterpriseChat.Protocol.Rooms;
using EnterpriseChat.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseChat.Server.Rooms;

internal static class RoomEndpoints
{
    public static void MapRoomEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/rooms").WithTags("Rooms").RequireAuthorization();
        group.MapGet("/", ListRoomsAsync);
    }

    private static async Task<IResult> ListRoomsAsync(
        ClaimsPrincipal principal,
        IDbContextFactory<ChatDbContext> dbFactory,
        CancellationToken ct)
    {
        var meRaw = principal.FindFirstValue("sub") ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(meRaw, CultureInfo.InvariantCulture, out var meId))
        {
            return Results.Unauthorized();
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var memberRoomIds = await db.RoomMembers
            .Where(m => m.UserId == meId)
            .Select(m => m.RoomId)
            .ToListAsync(ct);

        var rooms = await db.Rooms
            .Where(r => !r.IsPrivate || memberRoomIds.Contains(r.Id))
            .OrderBy(r => r.Name)
            .Select(r => new
            {
                r.Id,
                r.Name,
                r.IsPrivate,
                r.CreatedByUserId,
                r.CreatedAt,
                MemberCount = r.Members.Count
            })
            .ToListAsync(ct);

        var result = rooms.Select(r => new RoomSummary(
            Id: r.Id,
            Name: r.Name,
            IsPrivate: r.IsPrivate,
            CreatedByUserId: r.CreatedByUserId,
            CreatedAt: r.CreatedAt,
            IsMember: memberRoomIds.Contains(r.Id),
            MemberCount: r.MemberCount)).ToArray();

        return Results.Ok(result);
    }
}

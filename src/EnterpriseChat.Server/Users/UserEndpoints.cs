using System.Globalization;
using System.Security.Claims;
using EnterpriseChat.Protocol;
using EnterpriseChat.Server.Data;
using EnterpriseChat.Server.Licensing;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseChat.Server.Users;

internal static class UserEndpoints
{
    public static void MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/users").WithTags("Users").RequireAuthorization();

        group.MapGet("/", ListUsersAsync);
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
                Role = u.Role.ToString()
            })
            .ToListAsync(ct);

        var result = users
            .Where(u => u.Id != meId)
            .Select(u => new UserSummary(
                Id: u.Id,
                Username: u.Username,
                FullName: u.FullName,
                Department: u.Department,
                Role: u.Role,
                IsOnline: sessions.IsOnline(u.Id)))
            .ToArray();

        return Results.Ok(result);
    }
}

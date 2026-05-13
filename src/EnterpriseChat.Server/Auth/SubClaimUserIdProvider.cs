using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;

namespace EnterpriseChat.Server.Auth;

/// <summary>
/// SignalR's default provider reads <see cref="ClaimTypes.NameIdentifier"/>,
/// but our JWT keeps the standard <c>sub</c> claim verbatim because
/// <c>MapInboundClaims = false</c>. This adapter reads <c>sub</c> (falling
/// back to <see cref="ClaimTypes.NameIdentifier"/>) so that
/// <c>Clients.User(...)</c> can route messages to the right connections.
/// </summary>
public sealed class SubClaimUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection)
    {
        var user = connection.User;
        return user?.FindFirstValue("sub")
            ?? user?.FindFirstValue(ClaimTypes.NameIdentifier);
    }
}

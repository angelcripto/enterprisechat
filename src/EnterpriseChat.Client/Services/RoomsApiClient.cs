using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EnterpriseChat.Protocol.Rooms;

namespace EnterpriseChat.Client.Services;

public sealed class RoomsApiClient(HttpClient http, SessionContext session)
{
    public async Task<IReadOnlyList<RoomSummary>> ListAsync(CancellationToken ct = default)
    {
        if (session.Login is null)
        {
            throw new InvalidOperationException("No hay sesión iniciada.");
        }
        var endpoint = new Uri(new Uri(session.ServerUrl.TrimEnd('/') + "/"), "rooms");
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.Login.AccessToken);
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var list = await response.Content.ReadFromJsonAsync<List<RoomSummary>>(ct);
        return list ?? [];
    }
}

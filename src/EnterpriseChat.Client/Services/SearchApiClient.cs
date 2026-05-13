using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EnterpriseChat.Protocol.Search;

namespace EnterpriseChat.Client.Services;

public sealed class SearchApiClient(HttpClient http, SessionContext session)
{
    public async Task<SearchResponse> SearchAsync(string query, int limit = 50, CancellationToken ct = default)
    {
        if (session.Login is null)
        {
            throw new InvalidOperationException("No hay sesión iniciada.");
        }

        var encoded = Uri.EscapeDataString(query);
        var endpoint = new Uri(new Uri(session.ServerUrl.TrimEnd('/') + "/"), $"search?q={encoded}&limit={limit}");
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.Login.AccessToken);
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<SearchResponse>(ct);
        return payload ?? new SearchResponse(query, []);
    }
}

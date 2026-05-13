using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EnterpriseChat.Licensing.Abstractions;
using EnterpriseChat.Protocol.Licensing;

namespace EnterpriseChat.Client.Services;

public sealed class LicenseApiClient(HttpClient http, SessionContext session)
{
    public async Task<LicenseInfo> GetCurrentAsync(CancellationToken ct = default)
    {
        if (session.Login is null)
        {
            throw new InvalidOperationException("Sesión no iniciada.");
        }
        var endpoint = new Uri(new Uri(session.ServerUrl.TrimEnd('/') + "/"), "license");
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.Login.AccessToken);
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<LicenseInfo>(ct))!;
    }

    public async Task<ApplyResult> ApplyAsync(string serial, CancellationToken ct = default)
    {
        var response = await SendAsync(HttpMethod.Post, "admin/license",
            new ApplyLicenseRequest(serial), ct);

        if (response.StatusCode is HttpStatusCode.OK or HttpStatusCode.BadRequest)
        {
            var payload = await response.Content.ReadFromJsonAsync<ApplyLicenseResponse>(ct);
            return new ApplyResult(payload?.Success ?? false, payload?.ErrorMessage);
        }
        response.EnsureSuccessStatusCode();
        return new ApplyResult(true, null);
    }

    public async Task ClearAsync(CancellationToken ct = default)
    {
        var response = await SendAsync(HttpMethod.Delete, "admin/license", body: null, ct);
        response.EnsureSuccessStatusCode();
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string relative, object? body, CancellationToken ct)
    {
        if (session.Login is null)
        {
            throw new InvalidOperationException("Sesión no iniciada.");
        }
        var uri = new Uri(new Uri(session.ServerUrl.TrimEnd('/') + "/"), relative);
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.Login.AccessToken);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }
        return await http.SendAsync(request, ct);
    }
}

public sealed record ApplyResult(bool Success, string? ErrorMessage);

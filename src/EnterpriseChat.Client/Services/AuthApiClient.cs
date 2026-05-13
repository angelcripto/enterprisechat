using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using EnterpriseChat.Protocol;

namespace EnterpriseChat.Client.Services;

public sealed class AuthApiClient(HttpClient http)
{
    public async Task<HealthCheckResult> CheckHealthAsync(string serverUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(serverUrl))
        {
            return new HealthCheckResult(false, "URL del servidor vacía.");
        }
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(4));
            var endpoint = new Uri(new Uri(serverUrl.TrimEnd('/') + "/"), "healthz");
            using var response = await http.GetAsync(endpoint, cts.Token);
            return response.IsSuccessStatusCode
                ? new HealthCheckResult(true, null)
                : new HealthCheckResult(false, $"Respuesta HTTP {(int)response.StatusCode}.");
        }
        catch (TaskCanceledException)
        {
            return new HealthCheckResult(false, "Tiempo de espera agotado.");
        }
        catch (HttpRequestException ex)
        {
            return new HealthCheckResult(false, ex.Message);
        }
    }

    public async Task<LoginResult> LoginAsync(string serverUrl, string username, string password, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverUrl);

        var endpoint = new Uri(new Uri(serverUrl.TrimEnd('/') + "/"), "auth/login");
        HttpResponseMessage response;
        try
        {
            response = await http.PostAsJsonAsync(endpoint, new LoginRequest(username, password), ct);
        }
        catch (HttpRequestException ex)
        {
            return LoginResult.Network(ex.Message);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            return LoginResult.Network($"Tiempo de espera agotado: {ex.Message}");
        }

        return response.StatusCode switch
        {
            HttpStatusCode.OK => LoginResult.Success((await response.Content.ReadFromJsonAsync<LoginResponse>(ct))!),
            HttpStatusCode.Unauthorized => LoginResult.BadCredentials(),
            HttpStatusCode.BadRequest => LoginResult.BadRequest(),
            _ => LoginResult.Network($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}")
        };
    }
}

public sealed record HealthCheckResult(bool IsReachable, string? ErrorMessage);

public abstract record LoginResult
{
    public sealed record SuccessResult(LoginResponse Response) : LoginResult;
    public sealed record BadCredentialsResult : LoginResult;
    public sealed record BadRequestResult : LoginResult;
    public sealed record NetworkResult(string Message) : LoginResult;

    public static LoginResult Success(LoginResponse r) => new SuccessResult(r);
    public static LoginResult BadCredentials() => new BadCredentialsResult();
    public static LoginResult BadRequest() => new BadRequestResult();
    public static LoginResult Network(string msg) => new NetworkResult(msg);
}

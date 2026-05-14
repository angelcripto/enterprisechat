using System.Net.Http;
using System.Net.Http.Json;

namespace EnterpriseChat.Server.Licensing;

/// <summary>
/// HTTP client that talks to the EnterpriseChat licensing backend (Slim/PHP).
/// All license validation lives there; this client just submits the serial
/// plus our server identity and stores the returned short-TTL token.
///
/// Configured via <c>EnterpriseChat:Licensing:ActivationUrl</c>. If the URL
/// is missing the server stays in Free mode and never phones home.
///
/// Uses <see cref="IHttpClientFactory"/> so the surrounding singleton
/// (<see cref="RemoteLicenseAdministrator"/>) does not capture a transient
/// HttpClient over its full lifetime.
/// </summary>
public sealed class LicenseActivationClient(
    IHttpClientFactory httpFactory,
    IConfiguration config,
    ILogger<LicenseActivationClient> log)
{
    public const string HttpClientName = "licensing";

    /// <summary>
    /// Licenses are issued and validated by the production backend only. The
    /// dev environment also activates here so the activation flow is tested
    /// end-to-end with real signatures, not a local mock. Override via config
    /// (<c>EnterpriseChat:Licensing:ActivationUrl</c>) is allowed but heavily
    /// discouraged in production setups.
    /// </summary>
    public const string DefaultActivationUrl = "https://enterprisechat.es/activate";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ActivationUrl);

    public string ActivationUrl =>
        config["EnterpriseChat:Licensing:ActivationUrl"] is { Length: > 0 } overriden
            ? overriden
            : DefaultActivationUrl;

    public async Task<ActivationResponse?> ActivateAsync(string serial, CancellationToken ct = default)
    {
        var identity = ServerIdentity.Current;
        var payload = new
        {
            serial,
            hostname = identity.Hostname,
            mac_hash = identity.MacHash
        };

        HttpResponseMessage response;
        try
        {
            using var client = httpFactory.CreateClient(HttpClientName);
            response = await client.PostAsJsonAsync(ActivationUrl, payload, ct);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Activación falló por error de red.");
            return new ActivationResponse(
                Success: false,
                Error: $"No se pudo contactar con el servidor de licencias: {ex.Message}",
                Jwt: null, Jti: null, LicensedTo: null, MaxUsers: 0,
                Features: Array.Empty<string>(), HeartbeatSeconds: 0,
                Edition: null);
        }

        var raw = await response.Content.ReadFromJsonAsync<ActivationWire>(cancellationToken: ct);
        if (raw is null)
        {
            return new ActivationResponse(
                Success: false,
                Error: $"Respuesta no válida del servidor (HTTP {(int)response.StatusCode}).",
                Jwt: null, Jti: null, LicensedTo: null, MaxUsers: 0,
                Features: Array.Empty<string>(), HeartbeatSeconds: 0, Edition: null);
        }

        return new ActivationResponse(
            Success: raw.Success,
            Error: raw.Error,
            Jwt: raw.Jwt,
            Jti: raw.Jti,
            LicensedTo: raw.LicensedTo,
            MaxUsers: raw.MaxUsers,
            Features: raw.Features ?? Array.Empty<string>(),
            HeartbeatSeconds: raw.HeartbeatSeconds > 0 ? raw.HeartbeatSeconds : 1800,
            Edition: raw.Edition);
    }

    private sealed record ActivationWire(
        bool Success,
        string? Error,
        string? Jwt,
        string? Jti,
        [property: System.Text.Json.Serialization.JsonPropertyName("licensed_to")] string? LicensedTo,
        [property: System.Text.Json.Serialization.JsonPropertyName("max_users")] int MaxUsers,
        string[]? Features,
        [property: System.Text.Json.Serialization.JsonPropertyName("heartbeat_seconds")] int HeartbeatSeconds,
        string? Edition);
}

public sealed record ActivationResponse(
    bool Success,
    string? Error,
    string? Jwt,
    string? Jti,
    string? LicensedTo,
    int MaxUsers,
    string[] Features,
    int HeartbeatSeconds,
    string? Edition);

using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace EnterpriseChat.TrayMonitor.Services;

/// <summary>
/// Cliente HTTP que consulta los endpoints públicos del propio servidor
/// EnterpriseChat (loopback). Usado para verificar que el proceso está
/// vivo aunque el SCM aún reporte "Running" — si el servicio se ha
/// quedado colgado dejando el puerto libre, /healthz fallará y la UI
/// lo marcará en amarillo.
///
/// No usa autenticación: ambos endpoints (/healthz y /license) son
/// públicos por diseño.
/// </summary>
public sealed class HealthClient : IDisposable
{
    private readonly HttpClient _http;

    public HealthClient()
    {
        _http = new HttpClient
        {
            BaseAddress = new Uri("http://127.0.0.1:5080/"),
            Timeout = TimeSpan.FromSeconds(3),
        };
    }

    public async Task<bool> IsHealthyAsync(CancellationToken ct = default)
    {
        try
        {
            using var rsp = await _http.GetAsync("healthz", ct).ConfigureAwait(false);
            return rsp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<LicenseSnapshot?> GetLicenseAsync(CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<LicenseSnapshot>("license", ct).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    public void Dispose() => _http.Dispose();
}

/// <summary>
/// Forma del JSON que devuelve el endpoint <c>/license</c> del servidor.
/// El enum <c>edition</c> viaja como entero (0 = Free, 1 = Pro) — la UI
/// lo traduce a string al renderizar.
/// </summary>
public sealed record LicenseSnapshot
{
    [JsonPropertyName("edition")]
    public int Edition { get; init; }

    [JsonPropertyName("maxConcurrentUsers")]
    public int MaxConcurrentUsers { get; init; }

    [JsonPropertyName("expiresAt")]
    public DateTimeOffset? ExpiresAt { get; init; }

    [JsonPropertyName("licensedTo")]
    public string? LicensedTo { get; init; }

    [JsonPropertyName("licenseId")]
    public string? LicenseId { get; init; }

    public string EditionLabel => Edition switch
    {
        0 => "Free",
        1 => "Pro",
        _ => "Unknown",
    };
}

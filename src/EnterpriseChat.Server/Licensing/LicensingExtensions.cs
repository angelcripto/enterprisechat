using EnterpriseChat.Licensing.Abstractions;

namespace EnterpriseChat.Server.Licensing;

/// <summary>
/// Wires the online licensing pipeline. There is no longer a plugin DLL
/// loaded via reflection — the PHP backend is the sole authority for
/// validation. The server holds a shared <see cref="RemoteLicenseState"/>,
/// a validator that reads it, an HTTP client that talks to the backend and
/// a background heartbeat that re-activates every <c>heartbeat_seconds</c>.
/// </summary>
internal static class LicensingExtensions
{
    public static IServiceCollection AddEnterpriseChatLicensing(
        this IServiceCollection services,
        IConfiguration _config,
        IHostEnvironment _env)
    {
        services.AddSingleton<RemoteLicenseState>();
        services.AddSingleton<ILicenseValidator, RemoteLicenseValidator>();
        // Named HttpClient resolved through IHttpClientFactory inside the
        // singleton activation client; that avoids capturing a transient
        // HttpClient in a long-lived consumer.
        services.AddHttpClient(LicenseActivationClient.HttpClientName, c =>
        {
            c.Timeout = TimeSpan.FromSeconds(15);
            c.DefaultRequestHeaders.UserAgent.ParseAdd("EnterpriseChat-Server/1.0");
        });
        services.AddSingleton<LicenseActivationClient>();
        services.AddSingleton<ILicenseAdministrator, RemoteLicenseAdministrator>();
        services.AddHostedService<LicenseHeartbeatService>();
        return services;
    }
}

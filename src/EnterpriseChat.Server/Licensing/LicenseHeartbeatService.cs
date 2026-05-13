using EnterpriseChat.Licensing.Abstractions;
using Microsoft.Extensions.Hosting;

namespace EnterpriseChat.Server.Licensing;

/// <summary>
/// Periodically re-activates the current serial against the licensing
/// backend so revocations and binding changes take effect within an hour
/// instead of requiring a server restart.
///
/// Interval is taken from the backend's response (<c>heartbeat_seconds</c>);
/// failure paths just log and retry next tick. If the validator hasn't
/// heard back successfully for 24h, <see cref="RemoteLicenseState"/>
/// silently falls back to Free.
/// </summary>
public sealed class LicenseHeartbeatService(
    RemoteLicenseState state,
    ILicenseAdministrator administrator,
    ILogger<LicenseHeartbeatService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Initial delay so we don't fight the startup restorer.
        try { await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken); }
        catch (TaskCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            var serial = state.Serial;
            if (!string.IsNullOrEmpty(serial))
            {
                try
                {
                    var result = await administrator.ApplyAsync(serial, stoppingToken);
                    if (result.Success)
                    {
                        log.LogDebug("Heartbeat OK ({LicensedTo}).", result.Info?.LicensedTo);
                    }
                    else
                    {
                        log.LogWarning("Heartbeat falló: {Reason}", result.ErrorMessage);
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    log.LogWarning(ex, "Heartbeat lanzó excepción inesperada.");
                }
            }

            try { await Task.Delay(state.HeartbeatInterval, stoppingToken); }
            catch (TaskCanceledException) { break; }
        }
    }
}

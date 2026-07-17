using System.ServiceProcess;

namespace EnterpriseChat.TrayMonitor.Services;

/// <summary>
/// Wrapper sobre <see cref="ServiceController"/> para inspeccionar y
/// gobernar el servicio Windows "EnterpriseChat" desde la UI.
///
/// Si el proceso no es admin, las operaciones de escritura (Start, Stop,
/// Restart) lanzan <see cref="InvalidOperationException"/> /
/// <see cref="UnauthorizedAccessException"/>. La UI captura y muestra el
/// botón "Relanzar como administrador" en lugar de propagar la excepción.
/// </summary>
public sealed class WindowsServiceClient
{
    public const string ServiceName = "EnterpriseChat";
    private static readonly TimeSpan ActionTimeout = TimeSpan.FromSeconds(30);

    public bool IsInstalled()
    {
        try
        {
            using var sc = new ServiceController(ServiceName);
            _ = sc.Status;
            return true;
        }
        catch (InvalidOperationException)
        {
            // ServiceController lanza InvalidOperationException cuando el
            // servicio no existe. Esto es el caso "solo TrayMonitor sin
            // instalador" — devolvemos false silenciosamente.
            return false;
        }
    }

    public ServiceStatus GetStatus()
    {
        try
        {
            using var sc = new ServiceController(ServiceName);
            return sc.Status switch
            {
                ServiceControllerStatus.Running => ServiceStatus.Running,
                ServiceControllerStatus.Stopped => ServiceStatus.Stopped,
                ServiceControllerStatus.StartPending => ServiceStatus.Starting,
                ServiceControllerStatus.StopPending => ServiceStatus.Stopping,
                ServiceControllerStatus.ContinuePending or
                ServiceControllerStatus.PausePending or
                ServiceControllerStatus.Paused => ServiceStatus.Pending,
                _ => ServiceStatus.Unknown,
            };
        }
        catch (InvalidOperationException)
        {
            return ServiceStatus.NotInstalled;
        }
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        await Task.Run(() =>
        {
            using var sc = new ServiceController(ServiceName);
            if (sc.Status is ServiceControllerStatus.Running or ServiceControllerStatus.StartPending)
            {
                return;
            }
            sc.Start();
            sc.WaitForStatus(ServiceControllerStatus.Running, ActionTimeout);
        }, ct);
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        await Task.Run(() =>
        {
            using var sc = new ServiceController(ServiceName);
            if (sc.Status is ServiceControllerStatus.Stopped or ServiceControllerStatus.StopPending)
            {
                return;
            }
            sc.Stop();
            sc.WaitForStatus(ServiceControllerStatus.Stopped, ActionTimeout);
        }, ct);
    }

    public async Task RestartAsync(CancellationToken ct = default)
    {
        await StopAsync(ct);
        await StartAsync(ct);
    }
}

public enum ServiceStatus
{
    NotInstalled,
    Stopped,
    Starting,
    Running,
    Stopping,
    Pending,
    Unknown,
}

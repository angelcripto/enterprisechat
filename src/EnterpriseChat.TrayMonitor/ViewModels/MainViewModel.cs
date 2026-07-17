using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EnterpriseChat.TrayMonitor.Services;

namespace EnterpriseChat.TrayMonitor.ViewModels;

/// <summary>
/// ViewModel principal del TrayMonitor. Refresca estado del servicio +
/// licencia cada N segundos y expone comandos para Start / Stop / Restart
/// / Open admin / Refresh / Relaunch as administrator.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private const string AdminUrl = "http://localhost:5080/";

    private readonly WindowsServiceClient _service = new();
    private readonly HealthClient _health = new();
    private readonly LogTail _logTail;
    private readonly AdminPasswordResetClient _passwordReset;
    private readonly DispatcherTimer _poll;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusLabel))]
    [NotifyPropertyChangedFor(nameof(StatusColor))]
    [NotifyPropertyChangedFor(nameof(IsRunning))]
    [NotifyPropertyChangedFor(nameof(ServiceInstalled))]
    [NotifyCanExecuteChangedFor(nameof(StartServiceCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopServiceCommand))]
    [NotifyCanExecuteChangedFor(nameof(RestartServiceCommand))]
    [NotifyCanExecuteChangedFor(nameof(ChangeAdminPasswordCommand))]
    private ServiceStatus _status = ServiceStatus.Unknown;

    [ObservableProperty]
    private bool _isHealthy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartServiceCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopServiceCommand))]
    [NotifyCanExecuteChangedFor(nameof(RestartServiceCommand))]
    private bool _isAdmin;

    [ObservableProperty]
    private string _licenseLabel = "—";

    [ObservableProperty]
    private string _maxUsersLabel = "—";

    [ObservableProperty]
    private string _lastLog = string.Empty;

    [ObservableProperty]
    private string _busyMessage = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartServiceCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopServiceCommand))]
    [NotifyCanExecuteChangedFor(nameof(RestartServiceCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshNowCommand))]
    [NotifyCanExecuteChangedFor(nameof(ChangeAdminPasswordCommand))]
    private bool _isBusy;

    public string TrayTooltip => $"EnterpriseChat — {StatusLabel}";

    public bool ServiceInstalled => Status != ServiceStatus.NotInstalled;
    public bool IsRunning => Status == ServiceStatus.Running;

    public string StatusLabel => Status switch
    {
        ServiceStatus.NotInstalled => "Servicio no instalado",
        ServiceStatus.Running => "Activo",
        ServiceStatus.Stopped => "Detenido",
        ServiceStatus.Starting => "Arrancando…",
        ServiceStatus.Stopping => "Deteniéndose…",
        ServiceStatus.Pending => "Esperando…",
        _ => "Desconocido",
    };

    public string StatusColor => Status switch
    {
        ServiceStatus.Running => "#22863a",
        ServiceStatus.Stopped or ServiceStatus.NotInstalled => "#cb2431",
        ServiceStatus.Starting or ServiceStatus.Stopping or
        ServiceStatus.Pending => "#b08800",
        _ => "#6a737d",
    };

    public MainViewModel()
    {
        _isAdmin = ElevationDetector.IsRunningAsAdministrator();

        // Localización del install dir. Por defecto, la carpeta donde corre
        // el TrayMonitor (ej. C:\Program Files\EnterpriseChat\TrayMonitor)
        // tiene como padre el install dir del servidor.
        // AppContext.BaseDirectory funciona con PublishSingleFile (Assembly.Location no).
        var trayDir = AppContext.BaseDirectory;
        var serverDir = InstallDirLocator.FindServerInstallDir(trayDir);
        _logTail = new LogTail(serverDir);
        _passwordReset = new AdminPasswordResetClient(serverDir);

        _poll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _poll.Tick += async (_, _) => await RefreshAsync().ConfigureAwait(true);
    }

    public async Task InitializeAsync()
    {
        await RefreshAsync().ConfigureAwait(true);
        // Forzar primera re-evaluación de CanExecute tras descubrir si el
        // binario del server existe (el constructor solo asigna campos).
        ChangeAdminPasswordCommand.NotifyCanExecuteChanged();
        _poll.Start();
    }

    private async Task RefreshAsync()
    {
        Status = _service.GetStatus();
        IsHealthy = await _health.IsHealthyAsync().ConfigureAwait(true);

        var license = await _health.GetLicenseAsync().ConfigureAwait(true);
        if (license is null)
        {
            LicenseLabel = "—";
            MaxUsersLabel = "—";
        }
        else
        {
            LicenseLabel = license.EditionLabel;
            MaxUsersLabel = license.MaxConcurrentUsers.ToString();
        }

        var lines = _logTail.ReadLastLines(maxLines: 12);
        LastLog = string.Join(Environment.NewLine, lines);

        OnPropertyChanged(nameof(TrayTooltip));
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartServiceAsync()
    {
        await RunServiceOpAsync("Arrancando servicio…", _service.StartAsync);
    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    private async Task StopServiceAsync()
    {
        await RunServiceOpAsync("Deteniendo servicio…", _service.StopAsync);
    }

    [RelayCommand(CanExecute = nameof(CanRestart))]
    private async Task RestartServiceAsync()
    {
        await RunServiceOpAsync("Reiniciando servicio…", _service.RestartAsync);
    }

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshNowAsync()
    {
        await RefreshAsync().ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(CanChangePassword))]
    private async Task ChangeAdminPasswordAsync()
    {
        var dialog = new ChangePasswordDialog { Owner = Application.Current.MainWindow };
        var ok = dialog.ShowDialog();
        if (ok != true)
        {
            return;
        }
        var newPwd = dialog.AcceptedPassword;
        if (string.IsNullOrEmpty(newPwd))
        {
            return;
        }

        IsBusy = true;
        BusyMessage = "Cambiando contraseña del administrador…";
        try
        {
            var r = await _passwordReset.RunAsync(newPwd).ConfigureAwait(true);
            if (r.Ok)
            {
                MessageBox.Show(
                    "Contraseña del administrador cambiada correctamente.",
                    "EnterpriseChat",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(
                    "No se pudo cambiar la contraseña:\n\n" + r.Message,
                    "EnterpriseChat",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        finally
        {
            BusyMessage = string.Empty;
            IsBusy = false;
            // Sobrescribir la referencia local; no podemos forzar que el
            // string-interner lo libere pero al menos no lo guardamos.
            newPwd = string.Empty;
        }
    }

    // No exigimos ServiceInstalled: el CLI --reset-admin-password reescribe
    // el hash BCrypt directamente en chat.db, asi que funciona aunque el
    // servicio este Stopped o ni siquiera registrado en el SCM (caso util
    // si el operador esta recuperando acceso despues de borrar el
    // servicio pero conservar los datos).
    private bool CanChangePassword() => !IsBusy && _passwordReset.ServerBinaryExists();

    [RelayCommand]
    private void OpenAdmin()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = AdminUrl,
                UseShellExecute = true,
            });
        }
        catch
        {
            // El navegador del usuario está rotísimo si esto falla. No hay
            // mucho que podamos hacer aparte de no caernos.
        }
    }

    [RelayCommand]
    private void RelaunchAsAdmin()
    {
        // Environment.ProcessPath devuelve la ruta del .exe que arranca el
        // proceso (correcto con PublishSingleFile + DLLs). Assembly.Location
        // devuelve string vacío en single-file y dispara IL3000.
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = true,
                Verb = "runas",
            });
            // Cerramos la instancia actual (no admin) tras lanzar la elevada.
            Application.Current.Shutdown();
        }
        catch
        {
            // El usuario canceló UAC. No hacemos nada.
        }
    }

    private bool CanStart() => !IsBusy && IsAdmin && Status == ServiceStatus.Stopped;
    private bool CanStop() => !IsBusy && IsAdmin && Status == ServiceStatus.Running;
    private bool CanRestart() => !IsBusy && IsAdmin && Status == ServiceStatus.Running;
    private bool CanRefresh() => !IsBusy;

    private async Task RunServiceOpAsync(string busyText, Func<CancellationToken, Task> op)
    {
        IsBusy = true;
        BusyMessage = busyText;
        try
        {
            await op(CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Error en operación de servicio",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            BusyMessage = string.Empty;
            IsBusy = false;
            await RefreshAsync().ConfigureAwait(true);
        }
    }
}

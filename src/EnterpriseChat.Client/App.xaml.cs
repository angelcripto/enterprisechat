using System.IO;
using System.Windows;
using EnterpriseChat.Client.Services;
using EnterpriseChat.Client.ViewModels;
using EnterpriseChat.Client.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Wpf.Ui.Appearance;

namespace EnterpriseChat.Client;

public partial class App : Application
{
    private IHost? _host;

    public IServiceProvider Services => _host?.Services
        ?? throw new InvalidOperationException("Host no inicializado.");

    protected override async void OnStartup(StartupEventArgs e)
    {
        var logsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EnterpriseChat",
            "logs");
        Directory.CreateDirectory(logsDir);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .WriteTo.File(Path.Combine(logsDir, "client-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14)
            .CreateLogger();

        // Global exception handlers — WPF crashes are normally silent in Release.
        DispatcherUnhandledException += (s, ex) =>
        {
            Log.Fatal(ex.Exception, "Excepción no controlada en el dispatcher.");
            MessageBox.Show(
                $"Error inesperado:\n\n{ex.Exception.Message}\n\nDetalles en %LOCALAPPDATA%\\EnterpriseChat\\logs",
                "EnterpriseChat",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            ex.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (s, ex) =>
        {
            Log.Fatal(ex.ExceptionObject as Exception, "Excepción no controlada en el AppDomain (IsTerminating={Terminating}).", ex.IsTerminating);
            Log.CloseAndFlush();
        };
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, ex) =>
        {
            Log.Error(ex.Exception, "Excepción no observada en una Task.");
            ex.SetObserved();
        };

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<SettingsStore>();
                services.AddSingleton<SessionContext>();
                services.AddHttpClient<AuthApiClient>(c => c.Timeout = TimeSpan.FromSeconds(10));
                services.AddHttpClient<UsersApiClient>(c => c.Timeout = TimeSpan.FromSeconds(15));
                services.AddHttpClient<AdminApiClient>(c => c.Timeout = TimeSpan.FromSeconds(15));
                services.AddHttpClient<RoomsApiClient>(c => c.Timeout = TimeSpan.FromSeconds(15));
                services.AddHttpClient<SearchApiClient>(c => c.Timeout = TimeSpan.FromSeconds(20));
                services.AddHttpClient<AttachmentApiClient>(c => c.Timeout = TimeSpan.FromMinutes(5));
                services.AddHttpClient<LicenseApiClient>(c => c.Timeout = TimeSpan.FromSeconds(10));
                services.AddSingleton<ChatClient>();
                services.AddTransient<LoginViewModel>();
                services.AddTransient<MainViewModel>();
                services.AddTransient<ConnectionSettingsViewModel>();
                services.AddTransient<AdminViewModel>();
                services.AddTransient<CreateRoomViewModel>();
                services.AddTransient<SearchViewModel>();
                services.AddTransient<LicenseViewModel>();
                services.AddTransient<LoginWindow>();
                services.AddTransient<MainWindow>();
                services.AddTransient<ConnectionSettingsWindow>();
                services.AddTransient<AdminWindow>();
                services.AddTransient<SearchWindow>();
                services.AddTransient<WelcomeWindow>();
            })
            .UseSerilog()
            .Build();

        await _host.StartAsync();

        ApplicationThemeManager.Apply(ApplicationTheme.Dark);

        var loginWindow = _host.Services.GetRequiredService<LoginWindow>();
        var result = loginWindow.ShowDialog();
        if (result != true)
        {
            Shutdown();
            return;
        }

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;
        mainWindow.Closed += (_, _) => Shutdown();
        mainWindow.Show();

        var mainVm = (MainViewModel)mainWindow.DataContext;
        _ = mainVm.InitializeAsync().ContinueWith(_ =>
        {
            Dispatcher.Invoke(() => MaybeShowWelcomeAsync(mainWindow, mainVm));
        }, TaskScheduler.Default);

        base.OnStartup(e);
    }

    private void MaybeShowWelcomeAsync(MainWindow mainWindow, MainViewModel mainVm)
    {
        if (!mainVm.IsFreeEdition)
        {
            return;
        }

        var flagPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "EnterpriseChat",
            "welcomed.flag");
        if (File.Exists(flagPath))
        {
            return;
        }

        var welcome = Services.GetRequiredService<WelcomeWindow>();
        welcome.Owner = mainWindow;
        welcome.ShowDialog();

        // Mark as shown regardless of action; resurfaces only if the file is deleted.
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(flagPath)!);
            File.WriteAllText(flagPath, DateTime.UtcNow.ToString("O"));
        }
        catch
        {
            // Flag is just UX hint, not critical.
        }

        if (welcome.Result == WelcomeWindow.WelcomeAction.HaveSerial && mainVm.IsAdmin)
        {
            var admin = Services.GetRequiredService<AdminWindow>();
            admin.Owner = mainWindow;
            admin.ShowDialog();
            _ = mainVm.RefreshLicenseAsync();
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopAsync(TimeSpan.FromSeconds(3));
            _host.Dispose();
        }
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}

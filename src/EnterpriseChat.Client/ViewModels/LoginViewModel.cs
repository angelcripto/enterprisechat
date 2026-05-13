using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EnterpriseChat.Client.Services;

namespace EnterpriseChat.Client.ViewModels;

public sealed partial class LoginViewModel(
    AuthApiClient auth,
    SettingsStore settingsStore,
    SessionContext session) : ObservableObject
{
    /// <summary>Read-only display of the configured server (edited via the connection settings dialog).</summary>
    [ObservableProperty]
    private string _serverUrl = "http://localhost:5080";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
    private string _username = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
    private string _password = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>True once /healthz responded 2xx with the configured server URL.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
    private bool _isServerReachable;

    /// <summary>True while the initial health-check is in flight; gates the UI.</summary>
    [ObservableProperty]
    private bool _isCheckingServer;

    [ObservableProperty]
    private string? _serverStatusMessage;

    public event Action? LoginSucceeded;

    public async Task InitializeAsync()
    {
        var settings = settingsStore.Load();
        ServerUrl = settings.ServerUrl;
        Username = settings.LastUsername ?? "";
        await CheckServerAsync();
    }

    [RelayCommand]
    public async Task CheckServerAsync()
    {
        IsCheckingServer = true;
        ServerStatusMessage = "Verificando conexión con el servidor…";
        try
        {
            var result = await auth.CheckHealthAsync(ServerUrl);
            IsServerReachable = result.IsReachable;
            ServerStatusMessage = result.IsReachable
                ? null
                : $"Servidor inaccesible: {result.ErrorMessage}";
        }
        finally
        {
            IsCheckingServer = false;
        }
    }

    private bool CanLogin() => !IsBusy
        && IsServerReachable
        && !string.IsNullOrWhiteSpace(Username)
        && !string.IsNullOrEmpty(Password);

    [RelayCommand(CanExecute = nameof(CanLogin))]
    private async Task LoginAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await auth.LoginAsync(ServerUrl, Username, Password);
            switch (result)
            {
                case LoginResult.SuccessResult success:
                    session.Set(success.Response, ServerUrl);
                    settingsStore.Save(new ChatSettings { ServerUrl = ServerUrl, LastUsername = Username });
                    LoginSucceeded?.Invoke();
                    break;
                case LoginResult.BadCredentialsResult:
                    ErrorMessage = "Usuario o contraseña incorrectos.";
                    break;
                case LoginResult.BadRequestResult:
                    ErrorMessage = "Faltan campos obligatorios.";
                    break;
                case LoginResult.NetworkResult net:
                    ErrorMessage = $"No se pudo conectar: {net.Message}";
                    IsServerReachable = false;
                    break;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }
}

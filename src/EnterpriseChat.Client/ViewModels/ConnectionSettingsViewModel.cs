using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EnterpriseChat.Client.Services;

namespace EnterpriseChat.Client.ViewModels;

public sealed partial class ConnectionSettingsViewModel(SettingsStore store) : ObservableObject
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _serverUrl = "http://localhost:5080";

    [ObservableProperty]
    private string? _validationError;

    public event Action? Saved;
    public event Action? Cancelled;

    public void Load()
    {
        var settings = store.Load();
        ServerUrl = settings.ServerUrl;
    }

    private bool CanSave() =>
        !string.IsNullOrWhiteSpace(ServerUrl)
        && Uri.TryCreate(ServerUrl, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        if (!CanSave())
        {
            ValidationError = "URL inválida (debe empezar por http:// o https://).";
            return;
        }
        var settings = store.Load();
        settings.ServerUrl = ServerUrl.Trim();
        store.Save(settings);
        Saved?.Invoke();
    }

    [RelayCommand]
    private void Cancel() => Cancelled?.Invoke();
}

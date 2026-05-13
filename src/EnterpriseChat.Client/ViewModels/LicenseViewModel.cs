using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EnterpriseChat.Client.Services;
using EnterpriseChat.Licensing.Abstractions;
using Microsoft.Extensions.Logging;

namespace EnterpriseChat.Client.ViewModels;

public sealed partial class LicenseViewModel(
    LicenseApiClient api,
    ILogger<LicenseViewModel> log) : ObservableObject
{
    [ObservableProperty]
    private LicenseInfo? _current;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private string _serialInput = "";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _statusMessage;

    public bool IsFree => Current?.Edition == LicenseEdition.Free;
    public bool IsPro => Current?.Edition == LicenseEdition.Pro;

    public string EditionLabel => Current?.Edition switch
    {
        LicenseEdition.Pro => "Pro",
        _ => "Free"
    };

    public string CapacityLabel => Current is null
        ? "—"
        : Current.MaxConcurrentUsers.ToString(System.Globalization.CultureInfo.CurrentCulture);

    public string ExpiresLabel => Current?.ExpiresAt is { } e
        ? e.ToLocalTime().ToString("dd/MM/yyyy", System.Globalization.CultureInfo.CurrentCulture)
        : "Sin expiración";

    public string LicensedToLabel => Current?.LicensedTo ?? "—";

    partial void OnCurrentChanged(LicenseInfo? value)
    {
        OnPropertyChanged(nameof(IsFree));
        OnPropertyChanged(nameof(IsPro));
        OnPropertyChanged(nameof(EditionLabel));
        OnPropertyChanged(nameof(CapacityLabel));
        OnPropertyChanged(nameof(ExpiresLabel));
        OnPropertyChanged(nameof(LicensedToLabel));
    }

    public async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            Current = await api.GetCurrentAsync();
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Error cargando licencia.");
            StatusMessage = $"No se pudo cargar la licencia: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanApply() => !IsBusy && !string.IsNullOrWhiteSpace(SerialInput) && SerialInput.Trim().Length > 20;

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        IsBusy = true;
        StatusMessage = null;
        try
        {
            var result = await api.ApplyAsync(SerialInput.Trim());
            if (result.Success)
            {
                SerialInput = "";
                StatusMessage = "Licencia aplicada correctamente.";
                await RefreshAsync();
            }
            else
            {
                StatusMessage = result.ErrorMessage ?? "No se pudo aplicar el serial.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ClearAsync()
    {
        var confirm = System.Windows.MessageBox.Show(
            "¿Quitar la licencia activa? El servidor volverá a la edición Free (10 usuarios).",
            "EnterpriseChat",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question);
        if (confirm != System.Windows.MessageBoxResult.Yes) return;

        IsBusy = true;
        try
        {
            await api.ClearAsync();
            StatusMessage = "Licencia retirada. Servidor en edición Free.";
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}

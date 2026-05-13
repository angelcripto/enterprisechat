using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace EnterpriseChat.Client.ViewModels;

public sealed partial class CreateRoomViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private string _name = "";

    [ObservableProperty]
    private bool _isPrivate;

    public string? ResultName { get; private set; }
    public bool ResultIsPrivate { get; private set; }

    public event Action? Confirmed;
    public event Action? Cancelled;

    private bool CanCreate() =>
        !string.IsNullOrWhiteSpace(Name) && Name.Length <= 64;

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private void Create()
    {
        ResultName = Name.Trim();
        ResultIsPrivate = IsPrivate;
        Confirmed?.Invoke();
    }

    [RelayCommand]
    private void Cancel() => Cancelled?.Invoke();
}

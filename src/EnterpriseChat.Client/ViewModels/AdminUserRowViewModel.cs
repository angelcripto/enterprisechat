using CommunityToolkit.Mvvm.ComponentModel;
using EnterpriseChat.Protocol.Admin;

namespace EnterpriseChat.Client.ViewModels;

public sealed partial class AdminUserRowViewModel : ObservableObject
{
    public AdminUserRowViewModel(AdminUserDetail src)
    {
        Update(src);
    }

    public int Id { get; private set; }

    [ObservableProperty] private string _username = "";
    [ObservableProperty] private string _fullName = "";
    [ObservableProperty] private string? _email;
    [ObservableProperty] private int? _departmentId;
    [ObservableProperty] private string? _departmentName;
    [ObservableProperty] private string _role = "User";
    [ObservableProperty] private bool _isActive = true;
    [ObservableProperty] private DateTimeOffset? _lastLoginAt;

    public string LastLoginDisplay => LastLoginAt?.ToLocalTime().ToString("dd/MM/yyyy HH:mm",
        System.Globalization.CultureInfo.CurrentCulture) ?? "Nunca";

    public void Update(AdminUserDetail src)
    {
        Id = src.Id;
        Username = src.Username;
        FullName = src.FullName;
        Email = src.Email;
        DepartmentId = src.DepartmentId;
        DepartmentName = src.DepartmentName;
        Role = src.Role;
        IsActive = src.IsActive;
        LastLoginAt = src.LastLoginAt;
        OnPropertyChanged(nameof(LastLoginDisplay));
    }
}

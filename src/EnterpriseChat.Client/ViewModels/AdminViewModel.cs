using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EnterpriseChat.Client.Services;
using EnterpriseChat.Protocol.Admin;
using Microsoft.Extensions.Logging;

namespace EnterpriseChat.Client.ViewModels;

public sealed partial class AdminViewModel(
    AdminApiClient adminApi,
    LicenseViewModel license,
    ILogger<AdminViewModel> log) : ObservableObject
{
    public LicenseViewModel License { get; } = license;

    public ObservableCollection<AdminUserRowViewModel> Users { get; } = [];
    public ObservableCollection<DepartmentSummary> Departments { get; } = [];

    [ObservableProperty] private AdminUserRowViewModel? _selectedUser;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _statusMessage;

    // Create-user form fields
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateUserCommand))]
    private string _newUsername = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateUserCommand))]
    private string _newPassword = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateUserCommand))]
    private string _newFullName = "";

    [ObservableProperty] private string _newEmail = "";
    [ObservableProperty] private DepartmentSummary? _newDepartment;
    [ObservableProperty] private string _newRole = "User";

    [ObservableProperty] private string _newDepartmentName = "";

    public IReadOnlyList<string> AvailableRoles { get; } = ["User", "Admin"];

    public async Task RefreshAsync()
    {
        IsBusy = true;
        StatusMessage = null;
        try
        {
            await License.RefreshAsync();
            var (users, deps) = (await adminApi.ListUsersAsync(), await adminApi.ListDepartmentsAsync());

            Departments.Clear();
            foreach (var d in deps)
            {
                Departments.Add(d);
            }

            Users.Clear();
            foreach (var u in users)
            {
                Users.Add(new AdminUserRowViewModel(u));
            }
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Fallo cargando datos de admin.");
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanCreateUser() =>
        !IsBusy
        && !string.IsNullOrWhiteSpace(NewUsername)
        && !string.IsNullOrEmpty(NewPassword)
        && !string.IsNullOrWhiteSpace(NewFullName);

    [RelayCommand(CanExecute = nameof(CanCreateUser))]
    private async Task CreateUserAsync()
    {
        IsBusy = true;
        StatusMessage = null;
        try
        {
            var req = new CreateUserRequest(
                Username: NewUsername.Trim(),
                Password: NewPassword,
                FullName: NewFullName.Trim(),
                Email: string.IsNullOrWhiteSpace(NewEmail) ? null : NewEmail.Trim(),
                DepartmentId: NewDepartment?.Id,
                Role: NewRole);
            var created = await adminApi.CreateUserAsync(req);
            Users.Add(new AdminUserRowViewModel(created));
            NewUsername = NewPassword = NewFullName = NewEmail = "";
            StatusMessage = $"Usuario '{created.Username}' creado.";
        }
        catch (AdminApiException ex)
        {
            StatusMessage = ex.Message;
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Error creando usuario.");
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ResetPasswordAsync(AdminUserRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        var dialog = new Wpf.Ui.Controls.MessageBox
        {
            Title = "Reset de contraseña",
            Content = $"Introduce la nueva contraseña para '{row.Username}'.\n(Por ahora se asignará una contraseña fija 'reset-1234'; cámbiala tras iniciar sesión.)",
            PrimaryButtonText = "Aplicar",
            CloseButtonText = "Cancelar"
        };
        var result = await dialog.ShowDialogAsync();
        if (result != Wpf.Ui.Controls.MessageBoxResult.Primary)
        {
            return;
        }

        try
        {
            await adminApi.ResetPasswordAsync(row.Id, "reset-1234");
            StatusMessage = $"Contraseña de '{row.Username}' reseteada a 'reset-1234'.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DeactivateAsync(AdminUserRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        var confirm = System.Windows.MessageBox.Show(
            $"¿Desactivar al usuario '{row.Username}'?",
            "Confirmar",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await adminApi.DeactivateUserAsync(row.Id);
            row.IsActive = false;
            StatusMessage = $"Usuario '{row.Username}' desactivado.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ToggleActiveAsync(AdminUserRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }
        try
        {
            var newActive = !row.IsActive;
            await adminApi.UpdateUserAsync(row.Id, new UpdateUserRequest(
                FullName: row.FullName,
                Email: row.Email,
                DepartmentId: row.DepartmentId,
                Role: row.Role,
                IsActive: newActive));
            row.IsActive = newActive;
            StatusMessage = $"Usuario '{row.Username}' {(newActive ? "activado" : "desactivado")}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task CreateDepartmentAsync()
    {
        if (string.IsNullOrWhiteSpace(NewDepartmentName))
        {
            return;
        }
        try
        {
            var dep = await adminApi.CreateDepartmentAsync(NewDepartmentName.Trim());
            Departments.Add(dep);
            NewDepartmentName = "";
            StatusMessage = $"Departamento '{dep.Name}' creado.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
    }
}

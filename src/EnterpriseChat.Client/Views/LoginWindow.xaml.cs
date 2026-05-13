using System.Windows;
using EnterpriseChat.Client.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Wpf.Ui.Controls;

namespace EnterpriseChat.Client.Views;

public partial class LoginWindow : FluentWindow
{
    private readonly LoginViewModel _viewModel;
    private readonly IServiceProvider _services;

    public LoginWindow(LoginViewModel viewModel, IServiceProvider services)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _services = services;
        DataContext = viewModel;
        Loaded += OnLoaded;
        viewModel.LoginSucceeded += OnLoginSucceeded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.InitializeAsync();
        if (_viewModel.IsServerReachable)
        {
            PasswordField.Focus();
        }
    }

    private void OnLoginSucceeded()
    {
        Dispatcher.Invoke(() =>
        {
            DialogResult = true;
            Close();
        });
    }

    private void PasswordField_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is Wpf.Ui.Controls.PasswordBox pb)
        {
            _viewModel.Password = pb.Password;
        }
    }

    private async void ConfigureServer_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = _services.GetRequiredService<ConnectionSettingsWindow>();
        dialog.Owner = this;
        if (dialog.ShowDialog() == true)
        {
            await _viewModel.InitializeAsync();
        }
    }
}

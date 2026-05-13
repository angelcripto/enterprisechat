using System.ComponentModel;
using System.Windows;
using EnterpriseChat.Client.ViewModels;
using EnterpriseChat.Client.Views;
using Microsoft.Extensions.DependencyInjection;
using Wpf.Ui.Controls;

namespace EnterpriseChat.Client;

public partial class MainWindow : FluentWindow
{
    private readonly IServiceProvider _services;
    private bool _allowClose;

    public MainWindow(MainViewModel viewModel, IServiceProvider services)
    {
        InitializeComponent();
        DataContext = viewModel;
        _services = services;
        // Set tray menu header in code-behind to avoid a cyclic XAML binding
        // (ContextMenu inside NotifyIcon cannot reference its container's DataContext).
        TrayUserMenuItem.Header = viewModel.CurrentUserDisplay;
    }

    private void MainWindow_OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }
        // Hide instead of closing — keep the SignalR connection alive in tray.
        e.Cancel = true;
        Hide();
        ShowInTaskbar = false;
    }

    private void TrayShow_OnClick(object sender, RoutedEventArgs e)
    {
        if (!IsVisible)
        {
            Show();
        }
        ShowInTaskbar = true;
        WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private void TrayLogout_OnClick(object sender, RoutedEventArgs e)
    {
        var confirm = System.Windows.MessageBox.Show(
            "¿Cerrar sesión? Se desconectará del servidor.",
            "EnterpriseChat",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question);
        if (confirm != System.Windows.MessageBoxResult.Yes)
        {
            return;
        }
        _allowClose = true;
        System.Windows.Application.Current.Shutdown();
    }

    private void TrayExit_OnClick(object sender, RoutedEventArgs e)
    {
        _allowClose = true;
        System.Windows.Application.Current.Shutdown();
    }

    private void ConfigureServer_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = _services.GetRequiredService<ConnectionSettingsWindow>();
        dialog.Owner = this;
        if (dialog.ShowDialog() == true)
        {
            System.Windows.MessageBox.Show(
                "Configuración guardada. El nuevo servidor se utilizará la próxima vez que inicies sesión.",
                "EnterpriseChat",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
        }
    }

    private void OpenAdmin_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = _services.GetRequiredService<AdminWindow>();
        dialog.Owner = this;
        dialog.ShowDialog();
    }

    private void OpenSearch_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = _services.GetRequiredService<SearchWindow>();
        dialog.Owner = this;
        dialog.Show();
    }
}

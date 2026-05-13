using System.Windows;
using EnterpriseChat.Client.ViewModels;
using Wpf.Ui.Controls;

namespace EnterpriseChat.Client.Views;

public partial class ConnectionSettingsWindow : FluentWindow
{
    private readonly ConnectionSettingsViewModel _viewModel;

    public ConnectionSettingsWindow(ConnectionSettingsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Loaded += (_, _) => _viewModel.Load();
        viewModel.Saved += () => Dispatcher.Invoke(() => { DialogResult = true; Close(); });
        viewModel.Cancelled += () => Dispatcher.Invoke(() => { DialogResult = false; Close(); });
    }
}

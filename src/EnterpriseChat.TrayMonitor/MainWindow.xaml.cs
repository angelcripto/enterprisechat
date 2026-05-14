using System.ComponentModel;
using System.Windows;
using EnterpriseChat.TrayMonitor.ViewModels;
using Wpf.Ui.Controls;

namespace EnterpriseChat.TrayMonitor;

public partial class MainWindow : FluentWindow
{
    private bool _allowClose;
    private readonly MainViewModel _vm;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel();
        DataContext = _vm;
        Loaded += async (_, _) => await _vm.InitializeAsync();
    }

    /// <summary>
    /// Patrón "X = minimizar a la bandeja". El cliente WPF usa el mismo
    /// flujo (MainWindow.xaml.cs:25-35). Solo dejamos cerrar de verdad
    /// cuando el menú "Salir" pone <c>_allowClose = true</c>.
    /// </summary>
    private void MainWindow_OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }
        e.Cancel = true;
        Hide();
        ShowInTaskbar = false;
    }

    /// <summary>
    /// Cuando el usuario minimiza la ventana usando el botón "_", la
    /// escondemos del taskbar también para no duplicar presencia con la
    /// bandeja.
    /// </summary>
    private void MainWindow_OnStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            Hide();
            ShowInTaskbar = false;
        }
    }

    private void TrayShow_OnClick(object sender, RoutedEventArgs e)
    {
        Show();
        ShowInTaskbar = true;
        WindowState = WindowState.Normal;
        Activate();
    }

    private void TrayExit_OnClick(object sender, RoutedEventArgs e)
    {
        _allowClose = true;
        System.Windows.Application.Current.Shutdown();
    }
}

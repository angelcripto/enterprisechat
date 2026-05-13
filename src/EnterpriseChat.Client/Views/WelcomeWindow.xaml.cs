using System.Windows;
using Wpf.Ui.Controls;

namespace EnterpriseChat.Client.Views;

public partial class WelcomeWindow : FluentWindow
{
    public enum WelcomeAction { Later, BuyPro, HaveSerial }

    public WelcomeAction Result { get; private set; } = WelcomeAction.Later;

    public WelcomeWindow()
    {
        InitializeComponent();
    }

    private void BuyPro_OnClick(object sender, RoutedEventArgs e)
    {
        Result = WelcomeAction.BuyPro;
        DialogResult = true;
        Close();
    }

    private void HaveSerial_OnClick(object sender, RoutedEventArgs e)
    {
        Result = WelcomeAction.HaveSerial;
        DialogResult = true;
        Close();
    }

    private void Later_OnClick(object sender, RoutedEventArgs e)
    {
        Result = WelcomeAction.Later;
        DialogResult = false;
        Close();
    }
}

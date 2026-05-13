using EnterpriseChat.Client.ViewModels;
using Wpf.Ui.Controls;

namespace EnterpriseChat.Client.Views;

public partial class CreateRoomWindow : FluentWindow
{
    public CreateRoomWindow(CreateRoomViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.Confirmed += () => Dispatcher.Invoke(() => { DialogResult = true; Close(); });
        viewModel.Cancelled += () => Dispatcher.Invoke(() => { DialogResult = false; Close(); });
    }
}

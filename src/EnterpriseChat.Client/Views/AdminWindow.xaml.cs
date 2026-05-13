using EnterpriseChat.Client.ViewModels;
using Wpf.Ui.Controls;

namespace EnterpriseChat.Client.Views;

public partial class AdminWindow : FluentWindow
{
    public AdminWindow(AdminViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.RefreshAsync();
    }
}

using EnterpriseChat.Client.ViewModels;
using Wpf.Ui.Controls;

namespace EnterpriseChat.Client.Views;

public partial class SearchWindow : FluentWindow
{
    public SearchWindow(SearchViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}

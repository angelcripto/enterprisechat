using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EnterpriseChat.Client.Services;
using EnterpriseChat.Protocol.Search;

namespace EnterpriseChat.Client.ViewModels;

public sealed partial class SearchViewModel(SearchApiClient api) : ObservableObject
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    private string _query = "";

    [ObservableProperty] private bool _isSearching;
    [ObservableProperty] private string? _statusMessage;

    public ObservableCollection<SearchHit> Hits { get; } = [];

    private bool CanSearch() => !IsSearching && !string.IsNullOrWhiteSpace(Query) && Query.Trim().Length >= 2;

    [RelayCommand(CanExecute = nameof(CanSearch))]
    private async Task SearchAsync()
    {
        IsSearching = true;
        StatusMessage = null;
        try
        {
            var response = await api.SearchAsync(Query.Trim());
            Hits.Clear();
            foreach (var hit in response.Hits)
            {
                Hits.Add(hit);
            }
            if (Hits.Count == 0)
            {
                StatusMessage = "Sin resultados.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsSearching = false;
        }
    }
}

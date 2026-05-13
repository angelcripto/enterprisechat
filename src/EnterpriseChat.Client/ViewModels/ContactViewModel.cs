using CommunityToolkit.Mvvm.ComponentModel;
using EnterpriseChat.Protocol;

namespace EnterpriseChat.Client.ViewModels;

public sealed partial class ContactViewModel : ObservableObject
{
    public ContactViewModel(UserSummary summary)
    {
        Id = summary.Id;
        Username = summary.Username;
        FullName = summary.FullName;
        Department = summary.Department;
        Role = summary.Role;
        _isOnline = summary.IsOnline;
    }

    public int Id { get; }
    public string Username { get; }
    public string FullName { get; }
    public string? Department { get; }
    public string Role { get; }

    [ObservableProperty]
    private bool _isOnline;

    [ObservableProperty]
    private int _unreadCount;

    public bool HasUnread => UnreadCount > 0;
    partial void OnUnreadCountChanged(int value) => OnPropertyChanged(nameof(HasUnread));

    /// <summary>
    /// Subtitle line shown under the full name. Shows <c>@username</c> always
    /// (so two contacts with identical real names remain distinguishable) and
    /// appends the department when set.
    /// </summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Department)
        ? $"@{Username}"
        : $"@{Username} · {Department}";
}

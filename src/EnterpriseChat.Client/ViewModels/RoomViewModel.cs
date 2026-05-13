using CommunityToolkit.Mvvm.ComponentModel;
using EnterpriseChat.Protocol.Rooms;

namespace EnterpriseChat.Client.ViewModels;

public sealed partial class RoomViewModel : ObservableObject
{
    public RoomViewModel(RoomSummary src)
    {
        Update(src);
    }

    public int Id { get; private set; }

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private bool _isPrivate;
    [ObservableProperty] private bool _isMember;
    [ObservableProperty] private int _memberCount;

    [ObservableProperty] private int _unreadCount;
    public bool HasUnread => UnreadCount > 0;
    partial void OnUnreadCountChanged(int value) => OnPropertyChanged(nameof(HasUnread));

    public string Subtitle => IsPrivate
        ? $"#{Name} · privada · {MemberCount}"
        : $"#{Name} · {MemberCount}";

    public void Update(RoomSummary src)
    {
        Id = src.Id;
        Name = src.Name;
        IsPrivate = src.IsPrivate;
        IsMember = src.IsMember;
        MemberCount = src.MemberCount;
        OnPropertyChanged(nameof(Subtitle));
    }
}

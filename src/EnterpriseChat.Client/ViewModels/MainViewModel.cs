using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EnterpriseChat.Client.Services;
using EnterpriseChat.Client.Views;
using EnterpriseChat.Licensing.Abstractions;
using EnterpriseChat.Protocol;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EnterpriseChat.Client.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly SessionContext _session;
    private readonly UsersApiClient _usersApi;
    private readonly RoomsApiClient _roomsApi;
    private readonly LicenseApiClient _licenseApi;
    private readonly ChatClient _chatClient;
    private readonly IServiceProvider _services;
    private readonly ILogger<MainViewModel> _log;

    public MainViewModel(
        SessionContext session,
        UsersApiClient usersApi,
        RoomsApiClient roomsApi,
        LicenseApiClient licenseApi,
        ChatClient chatClient,
        IServiceProvider services,
        ILogger<MainViewModel> log)
    {
        _session = session;
        _usersApi = usersApi;
        _roomsApi = roomsApi;
        _licenseApi = licenseApi;
        _chatClient = chatClient;
        _services = services;
        _log = log;

        ContactsView = CollectionViewSource.GetDefaultView(Contacts);
        ContactsView.Filter = obj => !ShowOnlineOnly
            || (obj is ContactViewModel c && c.IsOnline);
    }

    public ObservableCollection<ContactViewModel> Contacts { get; } = [];
    public ICollectionView ContactsView { get; }

    public ObservableCollection<RoomViewModel> Rooms { get; } = [];

    [ObservableProperty]
    private ContactViewModel? _selectedContact;

    [ObservableProperty]
    private RoomViewModel? _selectedRoom;

    /// <summary>
    /// Current conversation. <see cref="ChatConversationViewModel"/> for DMs,
    /// <see cref="RoomConversationViewModel"/> for rooms. <c>object?</c> so the
    /// XAML can use one DataTemplate per concrete type.
    /// </summary>
    [ObservableProperty]
    private object? _activeConversation;

    [ObservableProperty]
    private string _connectionState = "Conectando…";

    [ObservableProperty]
    private string? _statusBanner;

    [ObservableProperty]
    private bool _showOnlineOnly;

    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    private LicenseInfo? _licenseInfo;

    public bool IsFreeEdition => LicenseInfo?.Edition == LicenseEdition.Free;
    public string LicenseBannerText
    {
        get
        {
            var cap = LicenseInfo?.MaxConcurrentUsers ?? 10;
            return $"🆓 Estás usando EnterpriseChat Free · Límite: {cap} usuarios concurrentes";
        }
    }
    partial void OnLicenseInfoChanged(LicenseInfo? value)
    {
        OnPropertyChanged(nameof(IsFreeEdition));
        OnPropertyChanged(nameof(LicenseBannerText));
    }

    partial void OnShowOnlineOnlyChanged(bool value) => ContactsView.Refresh();

    public string CurrentUserDisplay =>
        _session.Login is { } l ? $"{l.FullName} ({l.Username})" : "";

    public string EditionLabel => _session.Login?.Role == "Admin" ? "Admin" : "Usuario";

    public string TrayLabel => _session.Login is { } l
        ? $"EnterpriseChat — {l.Username}"
        : "EnterpriseChat";

    public bool IsAdmin => _session.Login?.Role == "Admin";

    public string ServerUrl => _session.ServerUrl;

    async partial void OnSelectedContactChanged(ContactViewModel? value)
    {
        if (value is null)
        {
            if (ActiveConversation is ChatConversationViewModel)
            {
                ActiveConversation = null;
            }
            return;
        }
        SelectedRoom = null;
        value.UnreadCount = 0;
        var vm = ActivatorUtilities.CreateInstance<ChatConversationViewModel>(_services, value);
        ActiveConversation = vm;
        await vm.LoadHistoryAsync();
    }

    async partial void OnSelectedRoomChanged(RoomViewModel? value)
    {
        if (value is null)
        {
            if (ActiveConversation is RoomConversationViewModel)
            {
                ActiveConversation = null;
            }
            return;
        }
        if (!value.IsMember)
        {
            try
            {
                await _chatClient.JoinRoomAsync(value.Id);
                value.IsMember = true;
            }
            catch (Exception ex)
            {
                StatusBanner = $"No se pudo entrar a la sala: {ex.Message}";
                SelectedRoom = null;
                return;
            }
        }
        SelectedContact = null;
        value.UnreadCount = 0;
        var vm = ActivatorUtilities.CreateInstance<RoomConversationViewModel>(_services, value);
        ActiveConversation = vm;
        await vm.LoadHistoryAsync();
    }

    public async Task InitializeAsync()
    {
        IsLoading = true;
        _chatClient.MessageReceived += OnMessageReceived;
        _chatClient.PresenceChanged += OnPresenceChanged;
        _chatClient.RoomMembershipChanged += OnRoomMembershipChanged;
        _chatClient.MessageRead += OnMessageRead;
        _chatClient.TypingReceived += OnTypingReceived;
        _chatClient.LicenseDenied += OnLicenseDenied;
        _chatClient.StateChanged += OnStateChanged;

        try
        {
            await _chatClient.ConnectAsync();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Fallo conectando al hub.");
            StatusBanner = $"No se pudo conectar al servidor: {ex.Message}";
        }

        await RefreshContactsAsync();
        await RefreshRoomsAsync();
        await RefreshLicenseAsync();
        IsLoading = false;
    }

    [RelayCommand]
    public async Task RefreshLicenseAsync()
    {
        try
        {
            LicenseInfo = await _licenseApi.GetCurrentAsync();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "No se pudo leer la información de licencia.");
        }
    }

    [RelayCommand]
    private async Task RefreshContactsAsync()
    {
        try
        {
            var users = await _usersApi.ListAsync();
            Contacts.Clear();
            foreach (var user in users.OrderByDescending(u => u.IsOnline).ThenBy(u => u.FullName))
            {
                Contacts.Add(new ContactViewModel(user));
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error listando usuarios.");
            StatusBanner = $"No se pudo cargar la lista de contactos: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RefreshRoomsAsync()
    {
        try
        {
            var rooms = await _roomsApi.ListAsync();
            Rooms.Clear();
            foreach (var r in rooms.OrderByDescending(r => r.IsMember).ThenBy(r => r.Name))
            {
                Rooms.Add(new RoomViewModel(r));
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error listando salas.");
            StatusBanner = $"No se pudieron cargar las salas: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task CreateRoomAsync()
    {
        var vm = _services.GetRequiredService<CreateRoomViewModel>();
        var dialog = new CreateRoomWindow(vm) { Owner = Application.Current.MainWindow };
        if (dialog.ShowDialog() != true || vm.ResultName is null)
        {
            return;
        }
        try
        {
            var newId = await _chatClient.CreateRoomAsync(vm.ResultName, vm.ResultIsPrivate);
            await RefreshRoomsAsync();
            var created = Rooms.FirstOrDefault(r => r.Id == newId);
            if (created is not null)
            {
                SelectedRoom = created;
            }
        }
        catch (Exception ex)
        {
            StatusBanner = $"No se pudo crear la sala: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task LeaveRoomAsync(RoomViewModel? room)
    {
        if (room is null) return;
        try
        {
            await _chatClient.LeaveRoomAsync(room.Id);
            room.IsMember = false;
            if (SelectedRoom?.Id == room.Id)
            {
                SelectedRoom = null;
            }
            await RefreshRoomsAsync();
        }
        catch (Exception ex)
        {
            StatusBanner = $"No se pudo salir de la sala: {ex.Message}";
        }
    }

    private void OnMessageReceived(ChatMessage message)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            // Route to active conversation regardless of type.
            switch (ActiveConversation)
            {
                case ChatConversationViewModel dm:
                    dm.ApplyIncoming(message);
                    break;
                case RoomConversationViewModel room:
                    room.ApplyIncoming(message);
                    break;
            }

            // Skip notification for own outbound messages.
            if (message.FromUserId == _session.Login?.UserId)
            {
                return;
            }

            if (message.RoomId is int roomId)
            {
                if (ActiveConversation is RoomConversationViewModel openRoom && openRoom.Room.Id == roomId)
                {
                    return;
                }
                var rvm = Rooms.FirstOrDefault(r => r.Id == roomId);
                if (rvm is not null)
                {
                    rvm.UnreadCount++;
                    StatusBanner = $"Nuevo mensaje en #{rvm.Name}.";
                }
            }
            else
            {
                if (ActiveConversation is ChatConversationViewModel openDm
                    && openDm.Peer.Id == message.FromUserId)
                {
                    return;
                }
                var contact = Contacts.FirstOrDefault(c => c.Id == message.FromUserId);
                if (contact is not null)
                {
                    contact.UnreadCount++;
                    StatusBanner = $"Nuevo mensaje de {contact.FullName}.";
                }
            }
        });
    }

    private void OnPresenceChanged(int userId, bool online)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var contact = Contacts.FirstOrDefault(c => c.Id == userId);
            if (contact is not null)
            {
                contact.IsOnline = online;
                ContactsView.Refresh();
            }
        });
    }

    private void OnRoomMembershipChanged(int roomId, int userId, bool joined)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var room = Rooms.FirstOrDefault(r => r.Id == roomId);
            if (room is null) return;
            room.MemberCount = Math.Max(0, room.MemberCount + (joined ? 1 : -1));
            if (userId == _session.Login?.UserId)
            {
                room.IsMember = joined;
            }
        });
    }

    private void OnMessageRead(long serverId, int byUserId, DateTimeOffset readAt)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            switch (ActiveConversation)
            {
                case ChatConversationViewModel dm:
                    dm.ApplyRead(serverId, readAt);
                    break;
                case RoomConversationViewModel room:
                    room.ApplyRead(serverId, readAt);
                    break;
            }
        });
    }

    private void OnTypingReceived(int fromUserId, int? toUserId, int? roomId)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (fromUserId == _session.Login?.UserId) return;
            switch (ActiveConversation)
            {
                case ChatConversationViewModel dm when dm.Peer.Id == fromUserId && toUserId == _session.Login?.UserId:
                    dm.ApplyTyping();
                    break;
                case RoomConversationViewModel room when room.Room.Id == roomId:
                    room.ApplyTyping();
                    break;
            }
        });
    }

    private void OnLicenseDenied(string reason)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            StatusBanner = $"Conexión rechazada por licencia: {reason}";
            MessageBox.Show(reason, "EnterpriseChat - licencia", MessageBoxButton.OK, MessageBoxImage.Warning);
        });
    }

    private void OnStateChanged(HubConnectionState state)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            ConnectionState = state switch
            {
                HubConnectionState.Connected => "Conectado",
                HubConnectionState.Connecting => "Conectando…",
                HubConnectionState.Reconnecting => "Reconectando…",
                HubConnectionState.Disconnected => "Desconectado",
                _ => state.ToString()
            };
        });
    }
}

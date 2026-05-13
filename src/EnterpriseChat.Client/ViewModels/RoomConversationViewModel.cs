using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EnterpriseChat.Client.Services;
using EnterpriseChat.Protocol;

namespace EnterpriseChat.Client.ViewModels;

public sealed partial class RoomConversationViewModel : ObservableObject
{
    private static readonly TimeSpan TypingPing = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan TypingTtl = TimeSpan.FromSeconds(3);

    private readonly ChatClient _chatClient;
    private readonly SessionContext _session;
    private readonly AttachmentApiClient _attachments;
    private DateTimeOffset _lastTypingPingSent = DateTimeOffset.MinValue;
    private DateTimeOffset _typingUntil = DateTimeOffset.MinValue;
    private readonly DispatcherTimer _typingTimer;

    public RoomConversationViewModel(RoomViewModel room, ChatClient chatClient, SessionContext session, AttachmentApiClient attachments)
    {
        Room = room;
        _chatClient = chatClient;
        _session = session;
        _attachments = attachments;
        _typingTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _typingTimer.Tick += (_, _) =>
        {
            if (IsSomeoneTyping && DateTimeOffset.UtcNow > _typingUntil)
            {
                IsSomeoneTyping = false;
                _typingTimer.Stop();
            }
        };
    }

    public RoomViewModel Room { get; }

    public ObservableCollection<MessageViewModel> Messages { get; } = [];

    [ObservableProperty] private string _draft = "";
    [ObservableProperty] private bool _isHistoryLoading;
    [ObservableProperty] private bool _isSomeoneTyping;

    public string TypingIndicator => "Alguien está escribiendo…";

    public bool CanSend => !string.IsNullOrWhiteSpace(Draft);

    partial void OnDraftChanged(string value)
    {
        SendCommand.NotifyCanExecuteChanged();
        if (string.IsNullOrWhiteSpace(value)) return;
        var now = DateTimeOffset.UtcNow;
        if (now - _lastTypingPingSent < TypingPing) return;
        _lastTypingPingSent = now;
        _ = _chatClient.NotifyTypingAsync(toUserId: null, roomId: Room.Id);
    }

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        var body = Draft.Trim();
        if (string.IsNullOrWhiteSpace(body)) return;
        Draft = "";
        try
        {
            await _chatClient.SendRoomMessageAsync(Room.Id, body);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo enviar el mensaje: {ex.Message}", "EnterpriseChat",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    [RelayCommand]
    private async Task AttachFileAsync()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Adjuntar archivo",
            Multiselect = false
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var attachment = await _attachments.UploadAsync(dlg.FileName);
            await _chatClient.SendRoomMessageWithAttachmentAsync(Room.Id, Draft.Trim(), attachment.Id);
            Draft = "";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo enviar el adjunto: {ex.Message}", "EnterpriseChat",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    [RelayCommand]
    private async Task DownloadAttachmentAsync(MessageViewModel? message)
    {
        if (message?.AttachmentId is not long id) return;
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Guardar adjunto",
            FileName = message.AttachmentFileName ?? "archivo"
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            await _attachments.DownloadToAsync(id, dlg.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo descargar: {ex.Message}", "EnterpriseChat",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    [RelayCommand]
    private async Task ViewAttachmentAsync(MessageViewModel? message)
    {
        if (message?.AttachmentId is not long id) return;
        await AttachmentViewer.ViewAsync(_attachments, id, message.AttachmentFileName);
    }

    public async Task LoadHistoryAsync()
    {
        if (IsHistoryLoading) return;
        IsHistoryLoading = true;
        try
        {
            var history = await _chatClient.GetRoomHistoryAsync(Room.Id, limit: 50);
            Messages.Clear();
            var meId = _session.Login?.UserId ?? 0;
            foreach (var msg in history)
            {
                Messages.Add(new MessageViewModel(msg, meId));
            }
            await MarkLatestAsReadAsync();
        }
        finally
        {
            IsHistoryLoading = false;
        }
    }

    public void ApplyIncoming(ChatMessage message)
    {
        if (message.RoomId != Room.Id) return;
        if (Messages.Any(m => m.ServerId == message.ServerId)) return;
        var meId = _session.Login?.UserId ?? 0;
        var vm = new MessageViewModel(message, meId);
        Messages.Add(vm);

        if (!vm.IsFromMe && vm.ServerId is long id)
        {
            _ = _chatClient.MarkAsReadAsync(id);
        }
    }

    public void ApplyRead(long serverId, DateTimeOffset readAt)
    {
        var match = Messages.FirstOrDefault(m => m.ServerId == serverId);
        if (match is not null && match.ReadAt is null)
        {
            match.ReadAt = readAt;
        }
    }

    public void ApplyTyping()
    {
        _typingUntil = DateTimeOffset.UtcNow + TypingTtl;
        IsSomeoneTyping = true;
        if (!_typingTimer.IsEnabled)
        {
            _typingTimer.Start();
        }
    }

    private async Task MarkLatestAsReadAsync()
    {
        var latestIncoming = Messages.LastOrDefault(m => !m.IsFromMe);
        if (latestIncoming?.ServerId is long id)
        {
            await _chatClient.MarkAsReadAsync(id);
        }
    }
}

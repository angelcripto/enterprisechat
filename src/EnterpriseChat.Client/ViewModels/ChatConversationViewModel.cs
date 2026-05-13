using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EnterpriseChat.Client.Services;
using EnterpriseChat.Protocol;

namespace EnterpriseChat.Client.ViewModels;

public sealed partial class ChatConversationViewModel : ObservableObject
{
    private static readonly TimeSpan TypingPing = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan TypingTtl = TimeSpan.FromSeconds(3);

    private readonly ChatClient _chatClient;
    private readonly SessionContext _session;
    private readonly AttachmentApiClient _attachments;
    private DateTimeOffset _lastTypingPingSent = DateTimeOffset.MinValue;
    private DateTimeOffset _peerTypingUntil = DateTimeOffset.MinValue;
    private readonly DispatcherTimer _typingTimer;

    public ChatConversationViewModel(ContactViewModel peer, ChatClient chatClient, SessionContext session, AttachmentApiClient attachments)
    {
        Peer = peer;
        _chatClient = chatClient;
        _session = session;
        _attachments = attachments;
        _typingTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _typingTimer.Tick += (_, _) =>
        {
            if (IsPeerTyping && DateTimeOffset.UtcNow > _peerTypingUntil)
            {
                IsPeerTyping = false;
                _typingTimer.Stop();
            }
        };
    }

    public ContactViewModel Peer { get; }

    public ObservableCollection<MessageViewModel> Messages { get; } = [];

    [ObservableProperty]
    private string _draft = "";

    [ObservableProperty]
    private bool _isHistoryLoading;

    [ObservableProperty]
    private bool _isPeerTyping;

    public string TypingIndicator => $"{Peer.FullName} está escribiendo…";

    public bool CanSend => !string.IsNullOrWhiteSpace(Draft);

    partial void OnDraftChanged(string value)
    {
        SendCommand.NotifyCanExecuteChanged();
        // Throttle typing notifications to the peer.
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }
        var now = DateTimeOffset.UtcNow;
        if (now - _lastTypingPingSent < TypingPing)
        {
            return;
        }
        _lastTypingPingSent = now;
        _ = _chatClient.NotifyTypingAsync(toUserId: Peer.Id, roomId: null);
    }

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        var body = Draft.Trim();
        if (string.IsNullOrWhiteSpace(body)) return;
        Draft = "";
        try
        {
            await _chatClient.SendDirectMessageAsync(Peer.Id, body);
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
            await _chatClient.SendDirectMessageWithAttachmentAsync(Peer.Id, Draft.Trim(), attachment.Id);
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
            var history = await _chatClient.GetDirectHistoryAsync(Peer.Id, limit: 50);
            Messages.Clear();
            var meId = _session.Login?.UserId ?? 0;
            foreach (var msg in history)
            {
                Messages.Add(new MessageViewModel(msg, meId));
            }
            await MarkLatestIncomingAsReadAsync();
        }
        finally
        {
            IsHistoryLoading = false;
        }
    }

    public void ApplyIncoming(ChatMessage message)
    {
        if (message.RoomId is not null) return;
        var meId = _session.Login?.UserId ?? 0;
        var relevant = (message.FromUserId == Peer.Id && message.ToUserId == meId)
            || (message.FromUserId == meId && message.ToUserId == Peer.Id);
        if (!relevant) return;
        if (Messages.Any(m => m.ServerId == message.ServerId))
        {
            return;
        }
        var vm = new MessageViewModel(message, meId);
        Messages.Add(vm);

        // Mark incoming as read immediately since the conversation is open.
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
        _peerTypingUntil = DateTimeOffset.UtcNow + TypingTtl;
        IsPeerTyping = true;
        if (!_typingTimer.IsEnabled)
        {
            _typingTimer.Start();
        }
    }

    private async Task MarkLatestIncomingAsReadAsync()
    {
        var latestIncoming = Messages.LastOrDefault(m => !m.IsFromMe);
        if (latestIncoming?.ServerId is long id)
        {
            await _chatClient.MarkAsReadAsync(id);
        }
    }
}

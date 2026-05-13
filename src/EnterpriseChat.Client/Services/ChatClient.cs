using EnterpriseChat.Protocol;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

namespace EnterpriseChat.Client.Services;

/// <summary>
/// Wraps <see cref="HubConnection"/> so view-models talk to a small typed API
/// without having to know about SignalR reconnect semantics. Raises events on
/// any thread; consumers must marshal to UI thread themselves.
/// </summary>
public sealed class ChatClient(SessionContext session, ILogger<ChatClient> log) : IAsyncDisposable
{
    private HubConnection? _connection;

    public event Action<ChatMessage>? MessageReceived;
    public event Action<int, bool>? PresenceChanged;
    public event Action<int, int, bool>? RoomMembershipChanged;
    public event Action<long, int, DateTimeOffset>? MessageRead;
    public event Action<int, int?, int?>? TypingReceived;
    public event Action<string>? LicenseDenied;
    public event Action<HubConnectionState>? StateChanged;

    public HubConnectionState State => _connection?.State ?? HubConnectionState.Disconnected;

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }

        if (session.Login is null)
        {
            throw new InvalidOperationException("Login pendiente.");
        }

        var hubUrl = new Uri(new Uri(session.ServerUrl.TrimEnd('/') + "/"), "hubs/chat");

        _connection = new HubConnectionBuilder()
            .WithUrl(hubUrl, opts =>
            {
                opts.AccessTokenProvider = () => Task.FromResult<string?>(session.Login.AccessToken);
            })
            .WithAutomaticReconnect(new[] { TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30) })
            .Build();

        _connection.On<ChatMessage>(nameof(IChatClient.OnMessageReceived), msg => MessageReceived?.Invoke(msg));
        _connection.On<int, bool>(nameof(IChatClient.OnPresenceChanged), (userId, online) => PresenceChanged?.Invoke(userId, online));
        _connection.On<int, int, bool>(nameof(IChatClient.OnRoomMembershipChanged),
            (roomId, userId, joined) => RoomMembershipChanged?.Invoke(roomId, userId, joined));
        _connection.On<long, int, DateTimeOffset>(nameof(IChatClient.OnMessageRead),
            (serverId, byUserId, readAt) => MessageRead?.Invoke(serverId, byUserId, readAt));
        _connection.On<int, int?, int?>(nameof(IChatClient.OnTyping),
            (fromUserId, toUserId, roomId) => TypingReceived?.Invoke(fromUserId, toUserId, roomId));
        _connection.On<string>(nameof(IChatClient.OnLicenseDenied), reason => LicenseDenied?.Invoke(reason));

        _connection.Reconnecting += ex =>
        {
            log.LogWarning(ex, "Reconectando…");
            StateChanged?.Invoke(HubConnectionState.Reconnecting);
            return Task.CompletedTask;
        };
        _connection.Reconnected += _ =>
        {
            log.LogInformation("Reconectado.");
            StateChanged?.Invoke(HubConnectionState.Connected);
            return Task.CompletedTask;
        };
        _connection.Closed += ex =>
        {
            log.LogInformation(ex, "Conexión cerrada.");
            StateChanged?.Invoke(HubConnectionState.Disconnected);
            return Task.CompletedTask;
        };

        await _connection.StartAsync(ct);
        StateChanged?.Invoke(HubConnectionState.Connected);
    }

    public async Task<long> SendDirectMessageAsync(int toUserId, string body, CancellationToken ct = default)
    {
        if (_connection is null || _connection.State != HubConnectionState.Connected)
        {
            throw new InvalidOperationException("No hay conexión activa con el servidor.");
        }
        return await _connection.InvokeAsync<long>("SendDirectMessage", toUserId, body, ct);
    }

    public async Task<IReadOnlyCollection<ChatMessage>> GetDirectHistoryAsync(int peerUserId, int limit = 50, long beforeServerId = long.MaxValue, CancellationToken ct = default)
    {
        if (_connection is null || _connection.State != HubConnectionState.Connected)
        {
            return [];
        }
        return await _connection.InvokeAsync<IReadOnlyCollection<ChatMessage>>(
            "GetDirectHistory", peerUserId, limit, beforeServerId, ct);
    }

    public async Task<int> CreateRoomAsync(string name, bool isPrivate, CancellationToken ct = default)
    {
        EnsureConnected();
        return await _connection!.InvokeAsync<int>("CreateRoom", name, isPrivate, ct);
    }

    public async Task JoinRoomAsync(int roomId, CancellationToken ct = default)
    {
        EnsureConnected();
        await _connection!.InvokeAsync("JoinRoom", roomId, ct);
    }

    public async Task LeaveRoomAsync(int roomId, CancellationToken ct = default)
    {
        EnsureConnected();
        await _connection!.InvokeAsync("LeaveRoom", roomId, ct);
    }

    public async Task<long> SendRoomMessageAsync(int roomId, string body, CancellationToken ct = default)
    {
        EnsureConnected();
        return await _connection!.InvokeAsync<long>("SendRoomMessage", roomId, body, ct);
    }

    public async Task<long> SendDirectMessageWithAttachmentAsync(int toUserId, string body, long attachmentId, CancellationToken ct = default)
    {
        EnsureConnected();
        return await _connection!.InvokeAsync<long>("SendDirectMessageWithAttachment", toUserId, body, attachmentId, ct);
    }

    public async Task<long> SendRoomMessageWithAttachmentAsync(int roomId, string body, long attachmentId, CancellationToken ct = default)
    {
        EnsureConnected();
        return await _connection!.InvokeAsync<long>("SendRoomMessageWithAttachment", roomId, body, attachmentId, ct);
    }

    public async Task<IReadOnlyCollection<ChatMessage>> GetRoomHistoryAsync(int roomId, int limit = 50, long beforeServerId = long.MaxValue, CancellationToken ct = default)
    {
        if (_connection is null || _connection.State != HubConnectionState.Connected)
        {
            return [];
        }
        return await _connection.InvokeAsync<IReadOnlyCollection<ChatMessage>>(
            "GetRoomHistory", roomId, limit, beforeServerId, ct);
    }

    public async Task MarkAsReadAsync(long serverId, CancellationToken ct = default)
    {
        if (_connection is null || _connection.State != HubConnectionState.Connected)
        {
            return;
        }
        try
        {
            await _connection.InvokeAsync("MarkAsRead", serverId, ct);
        }
        catch (Exception)
        {
            // Read receipts are best-effort; swallow transport errors.
        }
    }

    public async Task NotifyTypingAsync(int? toUserId, int? roomId, CancellationToken ct = default)
    {
        if (_connection is null || _connection.State != HubConnectionState.Connected)
        {
            return;
        }
        try
        {
            await _connection.InvokeAsync("Typing", toUserId, roomId, ct);
        }
        catch (Exception)
        {
            // Typing pings are best-effort.
        }
    }

    private void EnsureConnected()
    {
        if (_connection is null || _connection.State != HubConnectionState.Connected)
        {
            throw new InvalidOperationException("No hay conexión activa con el servidor.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
            _connection = null;
        }
    }
}

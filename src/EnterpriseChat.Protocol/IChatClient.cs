namespace EnterpriseChat.Protocol;

/// <summary>
/// Methods the server invokes on connected clients via SignalR. The client
/// registers handlers for each method and the strongly-typed hub on the
/// server uses this interface to dispatch.
/// </summary>
public interface IChatClient
{
    Task OnMessageReceived(ChatMessage message);

    /// <summary>
    /// Broadcast to all clients when another user connects or disconnects.
    /// </summary>
    Task OnPresenceChanged(int userId, bool isOnline);

    /// <summary>Pushed to room members when somebody joins or leaves the room.</summary>
    Task OnRoomMembershipChanged(int roomId, int userId, bool joined);

    /// <summary>
    /// Sent to the original sender (and to other recipients of a room message)
    /// when the message has been marked as read by <paramref name="byUserId"/>.
    /// </summary>
    Task OnMessageRead(long serverId, int byUserId, DateTimeOffset readAt);

    /// <summary>
    /// Sent to peer (DM) or room members (room) while the sender is typing.
    /// Throttled by the sender; recipients hide indicator after ~3s of silence.
    /// </summary>
    Task OnTyping(int fromUserId, int? toUserId, int? roomId);

    /// <summary>
    /// Sent immediately before the server aborts the connection because the
    /// licence cap was reached. The client should display <paramref name="reason"/>
    /// to the user and stop reconnect attempts.
    /// </summary>
    Task OnLicenseDenied(string reason);

    /// <summary>
    /// Notifies room members that the pinned-message set changed. The client
    /// re-fetches GET /rooms/{id}/pinned because the message body may be too
    /// large to push through the hub.
    /// </summary>
    Task OnPinnedChanged(int roomId, long messageId, bool pinned);

    /// <summary>
    /// Notifies room members (or DM peer) about a reaction toggle. UI updates
    /// the local count immediately and re-fetches if it drifts.
    /// </summary>
    Task OnReactionChanged(long messageId, int userId, string emoji, bool added);
}

using System.ComponentModel.DataAnnotations.Schema;

namespace EnterpriseChat.Server.Data.Entities;

/// <summary>
/// Marker pinning a message to a room. Composite key (RoomId, MessageId) so a
/// message is pinned at most once per room. We track who pinned and when for
/// activity / audit purposes; un-pinning just deletes the row.
/// </summary>
public sealed class PinnedMessage
{
    public int RoomId { get; set; }

    [ForeignKey(nameof(RoomId))]
    public Room Room { get; set; } = null!;

    public long MessageId { get; set; }

    [ForeignKey(nameof(MessageId))]
    public Message Message { get; set; } = null!;

    public int PinnedByUserId { get; set; }

    [ForeignKey(nameof(PinnedByUserId))]
    public User PinnedBy { get; set; } = null!;

    public DateTimeOffset PinnedAt { get; set; } = DateTimeOffset.UtcNow;
}

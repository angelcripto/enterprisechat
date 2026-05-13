using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EnterpriseChat.Server.Data.Entities;

public sealed class Message
{
    /// <summary>Server-assigned monotonic id; surfaced on the wire as <c>ServerId</c>.</summary>
    public long Id { get; set; }

    public int FromUserId { get; set; }

    [ForeignKey(nameof(FromUserId))]
    public User FromUser { get; set; } = null!;

    /// <summary>Set for direct (1:1) messages.</summary>
    public int? ToUserId { get; set; }

    [ForeignKey(nameof(ToUserId))]
    public User? ToUser { get; set; }

    /// <summary>Set for room/channel messages (Phase 3+). Mutually exclusive with <see cref="ToUserId"/>.</summary>
    public int? RoomId { get; set; }

    [Required, MaxLength(4096)]
    public string Body { get; set; } = null!;

    public DateTimeOffset SentAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>First time the message was pushed out over a live SignalR connection.</summary>
    public DateTimeOffset? DeliveredAt { get; set; }

    public DateTimeOffset? ReadAt { get; set; }

    public long? AttachmentId { get; set; }

    [ForeignKey(nameof(AttachmentId))]
    public Attachment? Attachment { get; set; }
}

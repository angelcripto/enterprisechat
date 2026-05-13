using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EnterpriseChat.Server.Data.Entities;

/// <summary>
/// Single emoji reaction by a user on a message. Composite key
/// (MessageId, UserId, Emoji) means a user can drop several distinct emojis
/// on the same message but not the same emoji twice (the second tap on the
/// same emoji un-reacts client-side).
/// </summary>
public sealed class MessageReaction
{
    public long MessageId { get; set; }

    [ForeignKey(nameof(MessageId))]
    public Message Message { get; set; } = null!;

    public int UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public User User { get; set; } = null!;

    /// <summary>Unicode emoji (1-16 grapheme clusters), stored as the raw glyph.</summary>
    [Required, MaxLength(32)]
    public string Emoji { get; set; } = null!;

    public DateTimeOffset ReactedAt { get; set; } = DateTimeOffset.UtcNow;
}

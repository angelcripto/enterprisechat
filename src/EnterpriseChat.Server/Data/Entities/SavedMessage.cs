using System.ComponentModel.DataAnnotations.Schema;

namespace EnterpriseChat.Server.Data.Entities;

/// <summary>
/// Bookmark: a message the user wants to revisit later. Single record per
/// (user, message) so toggling un-saves cleanly. Survives if the original
/// message is deleted only because we cascade.
/// </summary>
public sealed class SavedMessage
{
    public int UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public User User { get; set; } = null!;

    public long MessageId { get; set; }

    [ForeignKey(nameof(MessageId))]
    public Message Message { get; set; } = null!;

    public DateTimeOffset SavedAt { get; set; } = DateTimeOffset.UtcNow;
}

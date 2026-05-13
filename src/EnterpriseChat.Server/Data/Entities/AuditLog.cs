using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EnterpriseChat.Server.Data.Entities;

public sealed class AuditLog
{
    public long Id { get; set; }

    public int? ActorUserId { get; set; }

    [ForeignKey(nameof(ActorUserId))]
    public User? ActorUser { get; set; }

    [Required, MaxLength(64)]
    public string Action { get; set; } = null!;

    [MaxLength(256)]
    public string? Target { get; set; }

    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Optional JSON payload with extra context.</summary>
    [MaxLength(1024)]
    public string? Details { get; set; }
}

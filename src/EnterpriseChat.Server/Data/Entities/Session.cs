using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EnterpriseChat.Server.Data.Entities;

public sealed class Session
{
    public long Id { get; set; }

    public int UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public User User { get; set; } = null!;

    [Required, MaxLength(64)]
    public string ConnectionId { get; set; } = null!;

    public DateTimeOffset ConnectedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? DisconnectedAt { get; set; }

    [MaxLength(64)]
    public string? ClientIp { get; set; }

    [MaxLength(64)]
    public string? ClientVersion { get; set; }
}

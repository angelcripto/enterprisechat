using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EnterpriseChat.Server.Data.Entities;

public sealed class Room
{
    public int Id { get; set; }

    [Required, MaxLength(64)]
    public string Name { get; set; } = null!;

    public int CreatedByUserId { get; set; }

    [ForeignKey(nameof(CreatedByUserId))]
    public User CreatedBy { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Private rooms are not listed in the "discover" endpoint; only members can see them.
    /// Public rooms are visible to everybody on the server.
    /// </summary>
    public bool IsPrivate { get; set; }

    public ICollection<RoomMember> Members { get; set; } = new List<RoomMember>();
}

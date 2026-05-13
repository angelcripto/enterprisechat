using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EnterpriseChat.Server.Data.Entities;

public sealed class User
{
    public int Id { get; set; }

    [Required, MaxLength(64)]
    public string Username { get; set; } = null!;

    /// <summary>BCrypt hash; full algorithm prefix preserved so future migrations can detect format.</summary>
    [Required, MaxLength(128)]
    public string PasswordHash { get; set; } = null!;

    [Required, MaxLength(128)]
    public string FullName { get; set; } = null!;

    [MaxLength(256)]
    public string? Email { get; set; }

    public UserRole Role { get; set; } = UserRole.User;

    public int? DepartmentId { get; set; }

    [ForeignKey(nameof(DepartmentId))]
    public Department? Department { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? LastLoginAt { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// File name (relative to <c>data/avatars/</c>) of the profile picture
    /// uploaded by the user. Null = no avatar; the client renders initials
    /// in that case. The on-disk file is owned by the server, not the
    /// uploader, and is replaced (overwritten + previous deleted) on every
    /// new upload.
    /// </summary>
    [MaxLength(128)]
    public string? AvatarFileName { get; set; }
}

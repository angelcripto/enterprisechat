using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EnterpriseChat.Server.Data.Entities;

/// <summary>
/// Local copy of the license token applied to this server. Only one row should
/// ever be <see cref="Status"/> = <c>Active</c>; older ones are kept as
/// <c>Superseded</c> for audit trail.
/// </summary>
public sealed class LicenseRecord
{
    public long Id { get; set; }

    [Required, MaxLength(64)]
    public string Jti { get; set; } = null!;

    [Required]
    public string RawToken { get; set; } = null!;

    [MaxLength(255)]
    public string? LicensedTo { get; set; }

    public int MaxUsers { get; set; }

    public DateTimeOffset IssuedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset AppliedAt { get; set; } = DateTimeOffset.UtcNow;

    public int? AppliedByUserId { get; set; }

    [ForeignKey(nameof(AppliedByUserId))]
    public User? AppliedBy { get; set; }

    [Required, MaxLength(32)]
    public string Status { get; set; } = LicenseRecordStatus.Active;
}

public static class LicenseRecordStatus
{
    public const string Active = "active";
    public const string Superseded = "superseded";
    public const string Cleared = "cleared";
}

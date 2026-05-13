using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EnterpriseChat.Server.Data.Entities;

public sealed class Attachment
{
    public long Id { get; set; }

    [Required, MaxLength(255)]
    public string FileName { get; set; } = null!;

    [Required, MaxLength(128)]
    public string MimeType { get; set; } = null!;

    public long SizeBytes { get; set; }

    /// <summary>Path on disk, relative to the server's data/attachments folder.</summary>
    [Required, MaxLength(255)]
    public string StoragePath { get; set; } = null!;

    public int UploadedByUserId { get; set; }

    [ForeignKey(nameof(UploadedByUserId))]
    public User UploadedBy { get; set; } = null!;

    public DateTimeOffset UploadedAt { get; set; } = DateTimeOffset.UtcNow;
}

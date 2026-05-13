namespace EnterpriseChat.Protocol.Files;

public sealed record AttachmentSummary(
    long Id,
    string FileName,
    string MimeType,
    long SizeBytes,
    int UploadedByUserId,
    DateTimeOffset UploadedAt);

using CommunityToolkit.Mvvm.ComponentModel;
using EnterpriseChat.Protocol;

namespace EnterpriseChat.Client.ViewModels;

public sealed partial class MessageViewModel : ObservableObject
{
    public MessageViewModel(ChatMessage source, int meId)
    {
        ServerId = source.ServerId;
        Body = source.Body;
        SentAt = source.SentAt;
        FromUserId = source.FromUserId;
        IsFromMe = source.FromUserId == meId;
        AttachmentId = source.AttachmentId;
        AttachmentFileName = source.AttachmentFileName;
        AttachmentSizeBytes = source.AttachmentSizeBytes;
    }

    public long? ServerId { get; }
    public int FromUserId { get; }
    public string Body { get; }
    public DateTimeOffset SentAt { get; }
    public bool IsFromMe { get; }

    public long? AttachmentId { get; }
    public string? AttachmentFileName { get; }
    public long? AttachmentSizeBytes { get; }

    public bool HasAttachment => AttachmentId.HasValue;
    public bool HasBody => !string.IsNullOrWhiteSpace(Body);

    public bool IsViewableImage
    {
        get
        {
            if (AttachmentFileName is null) return false;
            var ext = System.IO.Path.GetExtension(AttachmentFileName).ToLowerInvariant();
            return ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".webp";
        }
    }

    public string AttachmentLabel => AttachmentSizeBytes is long size
        ? $"{AttachmentFileName} ({FormatBytes(size)})"
        : AttachmentFileName ?? "";

    [ObservableProperty]
    private DateTimeOffset? _readAt;

    public bool IsRead => ReadAt is not null;
    partial void OnReadAtChanged(DateTimeOffset? value) => OnPropertyChanged(nameof(IsRead));

    public string SentAtDisplay => SentAt.ToLocalTime().ToString("HH:mm",
        System.Globalization.CultureInfo.CurrentCulture);

    private static string FormatBytes(long size)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double s = size;
        int i = 0;
        while (s >= 1024 && i < units.Length - 1)
        {
            s /= 1024;
            i++;
        }
        return $"{s:F1} {units[i]}";
    }
}

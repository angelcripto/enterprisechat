namespace EnterpriseChat.Protocol;

/// <summary>
/// Lightweight metrics polled by the admin dashboard widget.
/// </summary>
public sealed record ServerMetrics(
    int ActiveUsers,
    int MaxUsers,
    long StorageUsedBytes,
    long StorageQuotaBytes,
    int MessageCount,
    int RoomCount);

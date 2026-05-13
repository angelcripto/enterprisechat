using EnterpriseChat.Licensing.Abstractions;

namespace EnterpriseChat.Server.Licensing;

/// <summary>
/// Validator backed by <see cref="RemoteLicenseState"/>. Returns whatever the
/// last successful online activation reported; falls back to Free if no
/// heartbeat in the last 24h or if no serial has been applied yet.
/// </summary>
public sealed class RemoteLicenseValidator(RemoteLicenseState state) : ILicenseValidator
{
    public LicenseInfo Current => state.Current;

    public LicenseAdmissionResult TryAdmitSession(int currentActiveSessions)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(currentActiveSessions);

        var info = Current;
        return currentActiveSessions < info.MaxConcurrentUsers
            ? LicenseAdmissionResult.Allow()
            : LicenseAdmissionResult.Deny(
                info.Edition == LicenseEdition.Pro
                    ? $"Edición Pro: límite de {info.MaxConcurrentUsers} usuarios concurrentes alcanzado."
                    : $"Edición Free: límite de {info.MaxConcurrentUsers} usuarios concurrentes alcanzado.");
    }
}

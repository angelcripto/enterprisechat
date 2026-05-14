namespace EnterpriseChat.Licensing.Abstractions;

/// <summary>
/// Default license validator that ships with the AGPL build of EnterpriseChat.
///
/// Hard cap of <see cref="FreeUserCap"/> concurrent users, no expiry, no token
/// validation. This is the "anonymous" Free tier; users that register on
/// enterprisechat.es get a real Free serial that lifts the cap to 10 (issued
/// by the activation backend, applied on top of this validator).
/// </summary>
public sealed class FreeLicenseValidator : ILicenseValidator
{
    /// <summary>
    /// Cap for servers that never activated a serial against the backend.
    /// Registering a free account on enterprisechat.es lifts this to 10
    /// (via a proper licensed Free serial), and Pro plans go higher.
    /// </summary>
    public const int FreeUserCap = 5;

    public LicenseInfo Current { get; } = new(
        Edition: LicenseEdition.Free,
        MaxConcurrentUsers: FreeUserCap,
        ExpiresAt: null,
        LicensedTo: null,
        LicenseId: null);

    public LicenseAdmissionResult TryAdmitSession(int currentActiveSessions)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(currentActiveSessions);

        return currentActiveSessions < FreeUserCap
            ? LicenseAdmissionResult.Allow()
            : LicenseAdmissionResult.Deny(
                $"Edición Free: límite de {FreeUserCap} usuarios concurrentes alcanzado.");
    }
}

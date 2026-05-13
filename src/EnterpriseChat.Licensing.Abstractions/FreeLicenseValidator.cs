namespace EnterpriseChat.Licensing.Abstractions;

/// <summary>
/// Default license validator that ships with the AGPL build of EnterpriseChat.
/// Hard cap of 10 concurrent users, no expiry, no token validation. Replace at
/// runtime via the Pro plugin to lift the cap.
/// </summary>
public sealed class FreeLicenseValidator : ILicenseValidator
{
    public const int FreeUserCap = 10;

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

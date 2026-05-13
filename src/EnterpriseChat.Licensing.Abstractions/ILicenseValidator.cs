namespace EnterpriseChat.Licensing.Abstractions;

/// <summary>
/// Boundary between the open-source server and any licensing implementation.
/// The Free edition ships a built-in implementation (<c>FreeLicenseValidator</c>)
/// in this assembly. A commercial Pro plugin (closed source) can implement this
/// interface and be loaded at runtime from the server's <c>plugins/</c>
/// directory to replace the Free implementation.
/// </summary>
public interface ILicenseValidator
{
    /// <summary>
    /// Describes the active license. Cheap, safe to call from admin endpoints
    /// or status banners.
    /// </summary>
    LicenseInfo Current { get; }

    /// <summary>
    /// Called when a client attempts to open a new session. The validator
    /// decides whether the new session is allowed given the current concurrent
    /// session count.
    /// </summary>
    /// <param name="currentActiveSessions">
    /// Number of sessions already counted as active at the moment of admission.
    /// Does <b>not</b> include the candidate session.
    /// </param>
    LicenseAdmissionResult TryAdmitSession(int currentActiveSessions);
}

public sealed record LicenseAdmissionResult(bool Admitted, string? DeniedReason)
{
    public static LicenseAdmissionResult Allow() => new(true, null);
    public static LicenseAdmissionResult Deny(string reason) => new(false, reason);
}

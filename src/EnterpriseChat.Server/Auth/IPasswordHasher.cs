namespace EnterpriseChat.Server.Auth;

/// <summary>
/// Abstraction over the password hashing algorithm so we can swap BCrypt for
/// Argon2id (or rotate the cost factor) without touching call sites.
/// </summary>
public interface IPasswordHasher
{
    /// <summary>Returns a fresh hash for the given plain-text password.</summary>
    string Hash(string password);

    /// <summary>
    /// Verifies a plain-text password against a stored hash. Returns the verification
    /// outcome plus a flag indicating whether the stored hash should be replaced
    /// (e.g. because the work factor was raised).
    /// </summary>
    PasswordVerificationResult Verify(string password, string storedHash);
}

public sealed record PasswordVerificationResult(bool Success, bool NeedsRehash);

using BCryptNet = BCrypt.Net.BCrypt;

namespace EnterpriseChat.Server.Auth;

public sealed class BcryptPasswordHasher : IPasswordHasher
{
    /// <summary>
    /// Active work factor. Verification accepts hashes generated with a lower
    /// factor and flags them for rehash on next successful login.
    /// </summary>
    public const int WorkFactor = 12;

    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        return BCryptNet.HashPassword(password, WorkFactor);
    }

    public PasswordVerificationResult Verify(string password, string storedHash)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        ArgumentException.ThrowIfNullOrEmpty(storedHash);

        bool ok;
        try
        {
            ok = BCryptNet.Verify(password, storedHash);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            // Malformed or unknown hash format — treat as failed verify.
            return new PasswordVerificationResult(false, false);
        }

        if (!ok)
        {
            return new PasswordVerificationResult(false, false);
        }

        // PasswordNeedsRehash inspects the cost factor embedded in the hash.
        var needsRehash = BCryptNet.PasswordNeedsRehash(storedHash, WorkFactor);
        return new PasswordVerificationResult(true, needsRehash);
    }
}

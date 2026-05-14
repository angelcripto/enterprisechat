using BCryptNet = BCrypt.Net.BCrypt;

namespace EnterpriseChat.Server.Auth.Hashers;

public sealed class BcryptVerifier : IPasswordHashVerifier
{
    public bool Verify(string plaintext, string stored)
    {
        if (string.IsNullOrEmpty(plaintext) || string.IsNullOrEmpty(stored))
        {
            return false;
        }
        try
        {
            return BCryptNet.Verify(plaintext, stored);
        }
        catch
        {
            return false;
        }
    }
}

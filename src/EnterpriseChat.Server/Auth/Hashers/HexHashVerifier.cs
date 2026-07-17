using System.Security.Cryptography;
using System.Text;

namespace EnterpriseChat.Server.Auth.Hashers;

/// <summary>
/// Verificador genérico de hashes sin sal en formato hexadecimal:
/// MD5, SHA-1, SHA-256. Se usan tres instancias parametrizadas por
/// constructor en lugar de una clase por algoritmo para no duplicar
/// código.
/// </summary>
public sealed class HexHashVerifier : IPasswordHashVerifier
{
    private readonly Func<HashAlgorithmName> _algorithmFactory;
    private readonly int _expectedHexLength;

    public HexHashVerifier(HashAlgorithmName algorithm, int expectedHexLength)
    {
        _algorithmFactory = () => algorithm;
        _expectedHexLength = expectedHexLength;
    }

    public static HexHashVerifier Md5 => new(HashAlgorithmName.MD5, 32);
    public static HexHashVerifier Sha1 => new(HashAlgorithmName.SHA1, 40);
    public static HexHashVerifier Sha256 => new(HashAlgorithmName.SHA256, 64);

    public bool Verify(string plaintext, string stored)
    {
        if (string.IsNullOrEmpty(plaintext) || string.IsNullOrEmpty(stored))
        {
            return false;
        }
        var normalized = stored.Trim().ToLowerInvariant();
        if (normalized.Length != _expectedHexLength)
        {
            return false;
        }

        byte[] computed;
        using (var alg = CryptoConfig.CreateFromName(_algorithmFactory().Name!) as System.Security.Cryptography.HashAlgorithm)
        {
            if (alg is null) return false;
            computed = alg.ComputeHash(Encoding.UTF8.GetBytes(plaintext));
        }
        var hex = Convert.ToHexString(computed).ToLowerInvariant();
        // Comparación constant-time.
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(hex),
            Encoding.ASCII.GetBytes(normalized));
    }
}

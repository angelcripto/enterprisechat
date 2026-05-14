using System.Security.Cryptography;
using System.Text;

namespace EnterpriseChat.Server.Auth.Hashers;

/// <summary>
/// SHA-256 con sal en formato Django/Laravel-ish:
///   <c>sha256$salt$hexhash</c>  o  <c>sha256$salt$base64hash</c>
///
/// El hash se calcula como <c>sha256(salt + plaintext)</c> tras decodificar
/// el salt como UTF-8 (consistente con la implementación Django). Si la BD
/// del cliente usa un esquema distinto, el admin elige Sha256 raw (sin sal)
/// o tiene que pedirnos soporte para su formato concreto.
/// </summary>
public sealed class Sha256SaltedVerifier : IPasswordHashVerifier
{
    public bool Verify(string plaintext, string stored)
    {
        if (string.IsNullOrEmpty(plaintext) || string.IsNullOrEmpty(stored))
        {
            return false;
        }

        var parts = stored.Split('$');
        // Aceptamos también el formato sin prefijo: salt$hash (2 partes).
        string algo, salt, hash;
        if (parts.Length == 3)
        {
            algo = parts[0].ToLowerInvariant();
            salt = parts[1];
            hash = parts[2];
            if (algo != "sha256")
            {
                return false;
            }
        }
        else if (parts.Length == 2)
        {
            algo = "sha256";
            salt = parts[0];
            hash = parts[1];
        }
        else
        {
            return false;
        }

        var combined = Encoding.UTF8.GetBytes(salt + plaintext);
        var computed = SHA256.HashData(combined);

        // Hash puede venir como hex (64 chars) o base64 (44 chars con padding).
        if (TryHexCompare(computed, hash)) return true;
        if (TryBase64Compare(computed, hash)) return true;
        return false;
    }

    private static bool TryHexCompare(byte[] computed, string stored)
    {
        var normalized = stored.Trim().ToLowerInvariant();
        if (normalized.Length != computed.Length * 2)
        {
            return false;
        }
        var hex = Convert.ToHexString(computed).ToLowerInvariant();
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(hex),
            Encoding.ASCII.GetBytes(normalized));
    }

    private static bool TryBase64Compare(byte[] computed, string stored)
    {
        try
        {
            var decoded = Convert.FromBase64String(stored.Trim());
            if (decoded.Length != computed.Length) return false;
            return CryptographicOperations.FixedTimeEquals(decoded, computed);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

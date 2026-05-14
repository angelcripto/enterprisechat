using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace EnterpriseChat.Server.Auth.Hashers;

/// <summary>
/// Verifica hashes Argon2id en formato PHC:
///   <c>$argon2id$v=19$m=65536,t=3,p=4$base64salt$base64hash</c>
///
/// La sal y el hash van en base64 SIN padding (estilo PHP password_hash).
/// Si la BD del cliente usa una variante con padding, también lo aceptamos
/// porque Convert.FromBase64String exige padding y rellenamos a múltiplo
/// de 4 antes de decodificar.
/// </summary>
public sealed class Argon2idVerifier : IPasswordHashVerifier
{
    public bool Verify(string plaintext, string stored)
    {
        if (string.IsNullOrEmpty(plaintext) || string.IsNullOrEmpty(stored))
        {
            return false;
        }

        if (!TryParse(stored, out var parsed))
        {
            return false;
        }

        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(plaintext))
        {
            Salt = parsed.Salt,
            DegreeOfParallelism = parsed.Parallelism,
            MemorySize = parsed.MemoryKib,
            Iterations = parsed.Iterations,
        };

        var computed = argon2.GetBytes(parsed.Hash.Length);
        return CryptographicOperations.FixedTimeEquals(computed, parsed.Hash);
    }

    private readonly record struct PhcParts(
        int MemoryKib,
        int Iterations,
        int Parallelism,
        byte[] Salt,
        byte[] Hash);

    private static bool TryParse(string stored, out PhcParts parts)
    {
        parts = default;
        // Formato: $argon2id$v=19$m=...,t=...,p=...$salt$hash
        var segments = stored.Split('$');
        if (segments.Length != 6) return false;
        if (segments[0].Length != 0) return false;
        if (!segments[1].Equals("argon2id", StringComparison.OrdinalIgnoreCase)) return false;
        // segments[2] = "v=19" (ignoramos, Konscious siempre asume 0x13).

        int m = 0, t = 0, p = 0;
        foreach (var kv in segments[3].Split(','))
        {
            var eq = kv.IndexOf('=');
            if (eq <= 0) return false;
            var key = kv[..eq];
            if (!int.TryParse(kv[(eq + 1)..], out var value)) return false;
            switch (key)
            {
                case "m": m = value; break;
                case "t": t = value; break;
                case "p": p = value; break;
            }
        }
        if (m <= 0 || t <= 0 || p <= 0) return false;

        if (!TryFromBase64NoPad(segments[4], out var salt)) return false;
        if (!TryFromBase64NoPad(segments[5], out var hash)) return false;
        if (salt.Length == 0 || hash.Length == 0) return false;

        parts = new PhcParts(m, t, p, salt, hash);
        return true;
    }

    private static bool TryFromBase64NoPad(string value, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        var padded = value;
        var mod = value.Length % 4;
        if (mod == 2) padded += "==";
        else if (mod == 3) padded += "=";
        else if (mod == 1) return false;
        try
        {
            bytes = Convert.FromBase64String(padded);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

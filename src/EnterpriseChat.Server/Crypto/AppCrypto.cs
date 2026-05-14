using System.Security.Cryptography;
using System.Text;

namespace EnterpriseChat.Server.Crypto;

/// <summary>
/// AES-256-GCM con una master key persistida en
/// <c>appsettings.Production.json:EnterpriseChat:Crypto:MasterKey</c> (32 bytes
/// base64). Se usa para cifrar las credenciales que el admin guarda al
/// configurar proveedores de autenticación externos (MySQL, HTTP webhook) —
/// nunca para passwords de usuarios, que pasan por <c>IPasswordHasher</c>.
///
/// Formato del ciphertext almacenado (todo concatenado y base64):
///   nonce (12 bytes) || ciphertext (N bytes) || tag (16 bytes)
///
/// La rotación de la clave master invalida los blobs viejos; documentar que
/// el admin debe re-introducir las credenciales tras rotar.
/// </summary>
public sealed class AppCrypto
{
    private const int NonceBytes = 12;
    private const int TagBytes   = 16;
    private const int KeyBytes   = 32; // AES-256

    private readonly byte[] _key;

    public AppCrypto(byte[] key)
    {
        if (key is null || key.Length != KeyBytes)
        {
            throw new ArgumentException(
                $"Master key debe tener {KeyBytes} bytes (recibidos {(key?.Length ?? 0)}).",
                nameof(key));
        }
        _key = key;
    }

    /// <summary>
    /// Convierte el master key serializado en base64 al formato byte[]
    /// esperado por <see cref="AppCrypto"/>. Lanza <see cref="ArgumentException"/>
    /// si la longitud es incorrecta — la inicialización del server debe
    /// abortar antes de que ningún provider externo intente cifrar.
    /// </summary>
    public static byte[] DecodeKey(string base64)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(base64);
        byte[] decoded;
        try
        {
            decoded = Convert.FromBase64String(base64);
        }
        catch (FormatException ex)
        {
            throw new ArgumentException("MasterKey no es Base64 válido.", nameof(base64), ex);
        }
        if (decoded.Length != KeyBytes)
        {
            throw new ArgumentException(
                $"MasterKey debe decodificar a {KeyBytes} bytes (decodificados {decoded.Length}).",
                nameof(base64));
        }
        return decoded;
    }

    /// <summary>
    /// Genera 32 bytes aleatorios para inicializar la master key.
    /// El bootstrap llama a esto la primera vez que arranca el server y
    /// no hay <c>MasterKey</c> en appsettings.
    /// </summary>
    public static string GenerateBase64Key()
    {
        var bytes = RandomNumberGenerator.GetBytes(KeyBytes);
        return Convert.ToBase64String(bytes);
    }

    public string EncryptString(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        return Convert.ToBase64String(EncryptBytes(Encoding.UTF8.GetBytes(plaintext)));
    }

    public string DecryptString(string blobBase64)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blobBase64);
        var plain = DecryptBytes(Convert.FromBase64String(blobBase64));
        return Encoding.UTF8.GetString(plain);
    }

    public byte[] EncryptBytes(ReadOnlySpan<byte> plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceBytes);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagBytes];

        using var aes = new AesGcm(_key, TagBytes);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        var blob = new byte[NonceBytes + ciphertext.Length + TagBytes];
        Buffer.BlockCopy(nonce,      0, blob, 0,                          NonceBytes);
        Buffer.BlockCopy(ciphertext, 0, blob, NonceBytes,                 ciphertext.Length);
        Buffer.BlockCopy(tag,        0, blob, NonceBytes + ciphertext.Length, TagBytes);
        return blob;
    }

    public byte[] DecryptBytes(ReadOnlySpan<byte> blob)
    {
        if (blob.Length < NonceBytes + TagBytes)
        {
            throw new CryptographicException("Blob cifrado corrupto (longitud insuficiente).");
        }

        var nonce       = blob.Slice(0, NonceBytes);
        var ciphertext  = blob.Slice(NonceBytes, blob.Length - NonceBytes - TagBytes);
        var tag         = blob.Slice(blob.Length - TagBytes, TagBytes);

        var plaintext = new byte[ciphertext.Length];
        using var aes = new AesGcm(_key, TagBytes);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        return plaintext;
    }
}

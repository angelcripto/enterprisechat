using System.Security.Cryptography;
using System.Text;

namespace EnterpriseChat.Server.Auth.Hashers;

/// <summary>
/// Compara plaintext con plaintext usando <c>FixedTimeEquals</c> para no
/// filtrar la longitud o el contenido por timing. Aun así, este verifier
/// implica que la BD del cliente almacena passwords en claro, lo cual es
/// catastrófico si se filtra y el banner de la UI lo explica.
/// </summary>
public sealed class PlaintextVerifier : IPasswordHashVerifier
{
    public bool Verify(string plaintext, string stored)
    {
        // Rechazamos vacíos para no autenticar a un atacante que apunte a
        // un usuario con la columna password sin rellenar.
        if (string.IsNullOrEmpty(plaintext) || string.IsNullOrEmpty(stored))
        {
            return false;
        }
        var a = Encoding.UTF8.GetBytes(plaintext);
        var b = Encoding.UTF8.GetBytes(stored);
        if (a.Length != b.Length)
        {
            return false;
        }
        return CryptographicOperations.FixedTimeEquals(a, b);
    }
}

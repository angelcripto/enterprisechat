namespace EnterpriseChat.Server.Auth.Hashers;

/// <summary>
/// Verificador de un algoritmo concreto. Cada provider externo declara qué
/// algoritmo usa la BD del cliente y el server elige el verifier que toca.
///
/// Para login interno (chat propio) seguimos usando <c>IPasswordHasher</c>
/// (BCrypt con rehash), que es distinto: incluye Hash() para producir
/// hashes nuevos. Los verifiers de aquí son SOLO lectura — no generamos
/// nunca un MD5 ni un SHA1 nuevo en nuestro código.
/// </summary>
public interface IPasswordHashVerifier
{
    /// <summary>
    /// Devuelve <c>true</c> si el plaintext coincide con el hash almacenado.
    /// Implementaciones deben usar comparaciones constant-time donde el
    /// algoritmo lo permita. No deben lanzar — un hash mal formado se
    /// trata como "no coincide".
    /// </summary>
    bool Verify(string plaintext, string stored);
}

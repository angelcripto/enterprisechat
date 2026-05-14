namespace EnterpriseChat.Server.Auth.Hashers;

/// <summary>
/// Algoritmos soportados al leer hashes desde la BD del cliente en
/// proveedores externos (MySQL, CSV, etc.). Solo verificación, no se
/// generan en este código.
///
/// Plaintext queda al final como recordatorio visual de que es la opción
/// menos segura. Su selección en la UI exige un check secundario "acepto
/// el riesgo" antes de guardar.
/// </summary>
public enum HashAlgorithm
{
    /// <summary>BCrypt con prefijo <c>$2a$/$2b$/$2y$</c>.</summary>
    Bcrypt = 0,

    /// <summary>Argon2id en formato PHC (<c>$argon2id$v=19$m=...,t=...,p=...$salt$hash</c>).</summary>
    Argon2id = 1,

    /// <summary>
    /// SHA-256 con sal en formato <c>sha256$salt$hex(hash)</c> (estilo Django).
    /// También acepta <c>sha256$salt$base64(hash)</c>.
    /// </summary>
    Sha256Salted = 2,

    /// <summary>SHA-256 hexadecimal sin sal. <strong>Inseguro</strong>: rainbow tables.</summary>
    Sha256 = 3,

    /// <summary>SHA-1 hexadecimal sin sal. <strong>Inseguro</strong>.</summary>
    Sha1 = 4,

    /// <summary>MD5 hexadecimal sin sal. <strong>Inseguro y roto</strong>.</summary>
    Md5 = 5,

    /// <summary>
    /// Texto plano. <strong>Catastrófico si se filtra</strong>. Requiere
    /// confirmación adicional en la UI para activarse.
    /// </summary>
    Plaintext = 9,
}

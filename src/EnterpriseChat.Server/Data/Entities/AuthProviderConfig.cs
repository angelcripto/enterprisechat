using System.ComponentModel.DataAnnotations;
using EnterpriseChat.Server.Auth.Hashers;
using EnterpriseChat.Server.Auth.Providers;

namespace EnterpriseChat.Server.Data.Entities;

/// <summary>
/// Configuración persistida de un proveedor externo de autenticación.
/// PR 1 solo crea la tabla y el modelo; los PRs siguientes la rellenarán
/// desde el panel de admin.
///
/// Las credenciales sensibles (<see cref="EncryptedSecretsJson"/>) se
/// guardan cifradas con AES-256-GCM usando la master key del server.
/// El JSON descifrado tiene la forma específica por proveedor:
///   MySQL  → {"host","port","database","user","password"}
///   HTTP   → {"endpointUrl","bearerToken","caBundlePem"}
///   CSV    → (vacío; los datos se importan a otra tabla)
///
/// <see cref="ConfigJson"/> es no-sensible (host, puerto, columna mapeada,
/// etc.) y queda en claro para que las queries de admin / debug no
/// requieran descifrado.
/// </summary>
public sealed class AuthProviderConfig
{
    public int Id { get; set; }

    public AuthProviderKind Kind { get; set; }

    [Required, MaxLength(128)]
    public string DisplayName { get; set; } = null!;

    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Orden de evaluación; menor = antes. El provider Internal queda
    /// fuera de esta tabla (no es configurable), siempre evalúa primero.
    /// </summary>
    public int Priority { get; set; }

    public HashAlgorithm HashAlgorithm { get; set; } = HashAlgorithm.Bcrypt;

    /// <summary>
    /// Confirmación explícita al usar <see cref="HashAlgorithm.Plaintext"/>.
    /// La UI exige un check secundario y guarda <c>true</c> aquí.
    /// </summary>
    public bool PlaintextRiskAcknowledged { get; set; }

    /// <summary>
    /// JSON no sensible con la configuración del proveedor (host, puerto,
    /// nombre de tabla, columnas mapeadas, etc.). Esquema validado en
    /// el endpoint que escribe el registro.
    /// </summary>
    [Required]
    public string ConfigJson { get; set; } = "{}";

    /// <summary>
    /// Blob base64 producido por <c>AppCrypto.EncryptString</c> que
    /// contiene un JSON con las credenciales sensibles. Vacío si el
    /// proveedor no requiere secretos (CSV).
    /// </summary>
    public string? EncryptedSecretsJson { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

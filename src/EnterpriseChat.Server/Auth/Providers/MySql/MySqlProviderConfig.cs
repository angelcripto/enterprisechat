namespace EnterpriseChat.Server.Auth.Providers.MySql;

/// <summary>
/// Configuración no-sensible del proveedor MySQL (host, puerto, mapeo de
/// columnas, opciones TLS). Se persiste como JSON dentro de
/// <c>AuthProviderConfig.ConfigJson</c>. Las credenciales (usuario,
/// contraseña) van en <c>EncryptedSecretsJson</c>.
/// </summary>
public sealed record MySqlProviderPublicConfig
{
    public string Host { get; init; } = "localhost";
    public int Port { get; init; } = 3306;
    public string Database { get; init; } = "";

    public string Table { get; init; } = "users";
    public string UsernameColumn { get; init; } = "username";
    public string PasswordColumn { get; init; } = "password_hash";

    /// <summary>Columna opcional con el ID estable del usuario. Si null se usa Username.</summary>
    public string? ExternalIdColumn { get; init; }
    public string? FullNameColumn { get; init; }
    public string? EmailColumn { get; init; }

    /// <summary>
    /// Filtro WHERE adicional (sin la palabra WHERE) para excluir
    /// usuarios desactivados / soft-deleted del lado externo. Ejemplo:
    ///   <c>is_active = 1</c>. Validado contra una whitelist mínima
    ///   antes de concatenar para evitar inyección.
    /// </summary>
    public string? ExtraWhere { get; init; }

    public MySqlTlsMode TlsMode { get; init; } = MySqlTlsMode.VerifyFull;
    public bool AutoProvision { get; init; } = true;

    /// <summary>Timeout total del SELECT, en segundos.</summary>
    public int QueryTimeoutSeconds { get; init; } = 5;
}

/// <summary>
/// Secretos del proveedor MySQL, cifrados con AES-256-GCM y guardados
/// en <c>AuthProviderConfig.EncryptedSecretsJson</c>.
/// </summary>
public sealed record MySqlProviderSecrets
{
    public string User { get; init; } = "";
    public string Password { get; init; } = "";

    /// <summary>
    /// Bundle CA opcional (pegado por el admin en formato PEM). Si null,
    /// el cliente usa la cadena del sistema. Necesario cuando el cert
    /// del MySQL externo está firmado por una CA interna y el server
    /// no tiene root trust del sistema (típico en VPS Linux).
    /// </summary>
    public string? CaBundlePem { get; init; }
}

public enum MySqlTlsMode
{
    None = 0,
    Preferred = 1,
    Required = 2,
    VerifyCa = 3,
    VerifyFull = 4,
}

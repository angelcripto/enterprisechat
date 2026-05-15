using EnterpriseChat.Server.Crypto;

namespace EnterpriseChat.Server.Bootstrap;

/// <summary>
/// Garantiza que existe una master key de AES-256-GCM antes de que
/// ningún componente intente cifrar credenciales de proveedores
/// externos.
///
/// La key se persiste en <c>{ContentRoot}/data/master.key</c> en TODOS
/// los entornos (Development, Testing, Production). Razones:
///   - Si la key sólo viviera en memoria, cada reinicio invalidaría
///     todos los blobs cifrados (caso real visto en dev: el admin
///     guarda un provider MySQL, reinicia el server y al volver a
///     entrar a la UI todos los blobs lanzan "computed authentication
///     tag did not match").
///   - El fichero queda fuera de <c>appsettings.json</c> para no
///     mezclarlo con config commiteable. <c>data/</c> ya está
///     gitignored.
///   - Permisos: el fichero hereda los del directorio <c>data/</c>,
///     que debe estar restringido al usuario que corre el server.
///     En producción Linux + systemd → 0700.
///
/// Si la key falta, se genera 32 bytes aleatorios y se escriben.
/// Rotación manual: borrar el fichero invalida todos los secretos
/// guardados; el admin tendrá que re-introducir las credenciales de
/// los providers.
/// </summary>
internal static class MasterKeyInitializer
{
    public const string DataDirName = "data";
    public const string KeyFileName = "master.key";

    private const string ConfigPath = "EnterpriseChat:Crypto:MasterKey";

    public static byte[] EnsureMasterKey(IConfigurationManager config, IHostEnvironment env, ILogger logger)
    {
        // Si el operador ya definió la key vía configuración (env var,
        // user-secrets, appsettings) tiene preferencia. Útil para
        // entornos donde la key se inyecta desde un secret manager.
        var existing = config[ConfigPath];
        if (!string.IsNullOrWhiteSpace(existing))
        {
            return AppCrypto.DecodeKey(existing);
        }

        var dataDir = Path.Combine(env.ContentRootPath, DataDirName);
        var keyPath = Path.Combine(dataDir, KeyFileName);

        if (File.Exists(keyPath))
        {
            var encoded = File.ReadAllText(keyPath).Trim();
            try
            {
                var bytes = AppCrypto.DecodeKey(encoded);
                config[ConfigPath] = encoded;
                logger.LogDebug("Master key cargada desde {Path}.", keyPath);
                return bytes;
            }
            catch (ArgumentException ex)
            {
                throw new InvalidOperationException(
                    $"El fichero {keyPath} existe pero no contiene una master key válida. " +
                    "Bórralo para regenerar (perderás los secretos cifrados anteriores) o restáuralo desde backup.",
                    ex);
            }
        }

        Directory.CreateDirectory(dataDir);
        var newKey = AppCrypto.GenerateBase64Key();
        File.WriteAllText(keyPath, newKey);
        try
        {
            // Restringir lectura al owner en sistemas POSIX. En Windows
            // el fichero hereda los ACLs de data/, suficiente con que
            // sólo el usuario del servicio tenga acceso al directorio.
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(keyPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "No se pudieron ajustar los permisos de {Path}.", keyPath);
        }

        logger.LogWarning(
            "Se ha generado una nueva master key en {Path}. " +
            "Inclúyela en tus backups: si la pierdes, los secretos cifrados de proveedores externos se vuelven irrecuperables.",
            keyPath);

        config[ConfigPath] = newKey;
        return AppCrypto.DecodeKey(newKey);
    }
}

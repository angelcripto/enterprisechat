using System.Text;
using EnterpriseChat.Server.Auth.Hashers;
using MySqlConnector;

namespace EnterpriseChat.Server.Auth.Providers.MySql;

/// <summary>
/// Proveedor que valida credenciales contra una tabla de usuarios
/// almacenada en un MySQL externo. Solo necesita SELECT en una columna
/// y una fila — recomendamos al admin crear un usuario MySQL específico
/// con permisos SELECT y filtro por IP (allowlist).
///
/// Hash:
///   El password almacenado en MySQL puede venir en cualquiera de los
///   algoritmos soportados por <see cref="HashVerifierRegistry"/>. La
///   selección la hace el admin al configurar el proveedor.
///
/// SQL inyección:
///   Sólo el <c>username</c> va como parámetro. Tabla y columnas se
///   validan con <see cref="MySqlIdentifier"/> antes de construir el
///   SELECT. El <c>ExtraWhere</c> se valida con whitelist mínima
///   (sin punto y coma, sin comentarios, sin DML/DDL).
/// </summary>
public sealed class MySqlAuthProvider : IAuthProvider
{
    private readonly string _connectionString;
    private readonly MySqlProviderPublicConfig _config;
    private readonly HashAlgorithm _hashAlgorithm;
    private readonly IPasswordHashVerifier _verifier;
    private readonly string _selectSql;

    public MySqlAuthProvider(
        int providerId,
        string displayName,
        MySqlProviderPublicConfig config,
        MySqlProviderSecrets secrets,
        HashAlgorithm hashAlgorithm,
        IPasswordHashVerifier verifier)
    {
        ProviderId = providerId;
        DisplayName = displayName;
        _config = config;
        _hashAlgorithm = hashAlgorithm;
        _verifier = verifier;
        _connectionString = BuildConnectionString(config, secrets);
        _selectSql = BuildSelectSql(config);
    }

    public AuthProviderKind Kind => AuthProviderKind.Mysql;
    public int ProviderId { get; }
    public string DisplayName { get; }

    public async Task<AuthResult> VerifyAsync(string username, string password, CancellationToken ct)
    {
        try
        {
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync(ct);

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = _selectSql;
            cmd.CommandTimeout = _config.QueryTimeoutSeconds;
            cmd.Parameters.AddWithValue("@username", username);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
            {
                return AuthResult.UnknownUser();
            }

            var storedHash = reader.IsDBNull(0) ? null : reader.GetString(0);
            string? externalId = _config.ExternalIdColumn is null
                ? username
                : (reader.IsDBNull(1) ? null : Convert.ToString(reader.GetValue(1)));
            string? fullName = ReadOptional(reader, "full_name");
            string? email    = ReadOptional(reader, "email");

            if (string.IsNullOrEmpty(storedHash))
            {
                return AuthResult.BadPassword("empty_hash");
            }

            var ok = _verifier.Verify(password, storedHash);
            return ok
                ? AuthResult.Success(externalId, fullName, email)
                : AuthResult.BadPassword();
        }
        catch (MySqlException ex)
        {
            return AuthResult.ProviderError($"mysql:{ex.ErrorCode}:{ex.Message}");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return AuthResult.ProviderError(ex.Message);
        }
    }

    private static string? ReadOptional(MySqlDataReader reader, string columnAlias)
    {
        var ordinal = reader.GetOrdinal(columnAlias);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    /// <summary>Genera el SELECT a partir del mapeo validado.</summary>
    public static string BuildSelectSql(MySqlProviderPublicConfig cfg)
    {
        var passwordCol = MySqlIdentifier.Quote(cfg.PasswordColumn);
        var externalIdCol = cfg.ExternalIdColumn is null
            ? "NULL"
            : MySqlIdentifier.Quote(cfg.ExternalIdColumn);
        var fullNameSelect = cfg.FullNameColumn is null
            ? "NULL AS full_name"
            : $"{MySqlIdentifier.Quote(cfg.FullNameColumn)} AS full_name";
        var emailSelect = cfg.EmailColumn is null
            ? "NULL AS email"
            : $"{MySqlIdentifier.Quote(cfg.EmailColumn)} AS email";
        var table = MySqlIdentifier.Quote(cfg.Table);
        var usernameCol = MySqlIdentifier.Quote(cfg.UsernameColumn);

        var sb = new StringBuilder();
        sb.Append("SELECT ")
          .Append(passwordCol).Append(", ")
          .Append(externalIdCol).Append(", ")
          .Append(fullNameSelect).Append(", ")
          .Append(emailSelect)
          .Append(" FROM ").Append(table)
          .Append(" WHERE ").Append(usernameCol).Append(" = @username");

        if (!string.IsNullOrWhiteSpace(cfg.ExtraWhere))
        {
            MySqlIdentifier.ValidateExtraWhere(cfg.ExtraWhere);
            sb.Append(" AND (").Append(cfg.ExtraWhere).Append(')');
        }
        sb.Append(" LIMIT 1");
        return sb.ToString();
    }

    private static string BuildConnectionString(MySqlProviderPublicConfig cfg, MySqlProviderSecrets secrets)
    {
        var b = new MySqlConnectionStringBuilder
        {
            Server = cfg.Host,
            Port = (uint)cfg.Port,
            Database = cfg.Database,
            UserID = secrets.User,
            Password = secrets.Password,
            ConnectionTimeout = (uint)Math.Max(2, cfg.QueryTimeoutSeconds),
            DefaultCommandTimeout = (uint)cfg.QueryTimeoutSeconds,
            Pooling = true,
            MaximumPoolSize = 4,
            MinimumPoolSize = 0,
            SslMode = cfg.TlsMode switch
            {
                MySqlTlsMode.None       => MySqlSslMode.None,
                MySqlTlsMode.Preferred  => MySqlSslMode.Preferred,
                MySqlTlsMode.Required   => MySqlSslMode.Required,
                MySqlTlsMode.VerifyCa   => MySqlSslMode.VerifyCA,
                MySqlTlsMode.VerifyFull => MySqlSslMode.VerifyFull,
                _                       => MySqlSslMode.VerifyFull,
            },
        };

        if (!string.IsNullOrWhiteSpace(secrets.CaBundlePem))
        {
            // MySqlConnector acepta ruta a fichero PEM. Escribimos el
            // bundle a un fichero temporal del data dir (no del temp del
            // sistema) y referenciamos. Para PR2 inline: el admin pega
            // PEM y lo materializamos en bootstrap del provider.
            var caPath = MaterializeCaBundle(secrets.CaBundlePem);
            b.SslCa = caPath;
        }

        return b.ToString();
    }

    private static string MaterializeCaBundle(string pem)
    {
        // Tiramos a la subcarpeta data/auth-ca/ para que el operador
        // tenga visibilidad de qué CA está confiando el server.
        var dir = Path.Combine(AppContext.BaseDirectory, "data", "auth-ca");
        Directory.CreateDirectory(dir);
        // Filename estable basado en hash del PEM para no rotar en
        // cada arranque y permitir cacheo del runtime SSL.
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(pem)))[..16];
        var path = Path.Combine(dir, $"mysql-{hash}.pem");
        if (!File.Exists(path))
        {
            File.WriteAllText(path, pem);
        }
        return path;
    }
}

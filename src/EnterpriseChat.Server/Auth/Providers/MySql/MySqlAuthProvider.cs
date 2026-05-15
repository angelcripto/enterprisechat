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

    /// <summary>
    /// Construye un connection string a partir de la config sin instanciar
    /// un provider completo. Útil para endpoints de introspección que
    /// no necesitan SELECT compuesto.
    /// </summary>
    public static string BuildConnectionStringFor(MySqlProviderPublicConfig cfg, MySqlProviderSecrets secrets)
        => BuildConnectionString(cfg, secrets);

    /// <summary>
    /// Lista las tablas del schema configurado. Para el wizard de la
    /// SPA: tras "Conectar y descubrir esquema", el admin elige tabla
    /// de un select rellenado con datos reales.
    /// </summary>
    public static async Task<IReadOnlyList<string>> ListTablesAsync(
        MySqlProviderPublicConfig cfg,
        MySqlProviderSecrets secrets,
        CancellationToken ct)
    {
        await using var connection = new MySqlConnection(BuildConnectionString(cfg, secrets));
        await connection.OpenAsync(ct);
        await using var cmd = connection.CreateCommand();
        // SHOW TABLES respeta la base de datos del connection string.
        cmd.CommandText = "SHOW TABLES";
        cmd.CommandTimeout = cfg.QueryTimeoutSeconds;
        var tables = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            tables.Add(reader.GetString(0));
        }
        return tables;
    }

    /// <summary>
    /// Lista las columnas de la tabla indicada. La tabla se valida
    /// contra <see cref="MySqlIdentifier"/> para evitar SHOW COLUMNS
    /// con identificadores arbitrarios.
    /// </summary>
    /// <summary>
    /// Fila proyectada al hacer browse de la tabla externa. Se usa para
    /// mostrar al admin la lista de candidatos al importar usuarios.
    /// </summary>
    public sealed record BrowseRow(
        string ExternalId,
        string Username,
        string? FullName,
        string? Email);

    /// <summary>
    /// Devuelve una página de usuarios de la tabla externa. <paramref name="search"/>
    /// matcha contra username y email (si está mapeado) con LIKE. La paginación
    /// es OFFSET/LIMIT — para tablas enormes (&gt;100k) habría que pasar a
    /// keyset por external_id, pero los entornos típicos de PYME aguantan.
    /// </summary>
    /// <summary>
    /// Columnas admitidas para <c>ORDER BY</c> al hacer browse. Se
    /// resuelven contra el mapeo del provider antes de quotar — el
    /// cliente envía "username|email|externalId" (lógico), nosotros
    /// resolvemos a la columna real configurada.
    /// </summary>
    private static string ResolveBrowseSortColumn(MySqlProviderPublicConfig cfg, string? sort)
    {
        return (sort?.ToLowerInvariant()) switch
        {
            "email" when cfg.EmailColumn is not null      => MySqlIdentifier.Quote(cfg.EmailColumn),
            "externalid" when cfg.ExternalIdColumn is not null => MySqlIdentifier.Quote(cfg.ExternalIdColumn),
            _ => MySqlIdentifier.Quote(cfg.UsernameColumn),
        };
    }

    private static string BuildWhereSql(
        MySqlProviderPublicConfig cfg, string? search, out bool hasSearch, out string usernameCol, out string emailExpr)
    {
        usernameCol = MySqlIdentifier.Quote(cfg.UsernameColumn);
        emailExpr = cfg.EmailColumn is null ? "NULL" : MySqlIdentifier.Quote(cfg.EmailColumn);

        var whereClauses = new List<string>();
        if (!string.IsNullOrWhiteSpace(cfg.ExtraWhere))
        {
            MySqlIdentifier.ValidateExtraWhere(cfg.ExtraWhere);
            whereClauses.Add($"({cfg.ExtraWhere})");
        }
        hasSearch = !string.IsNullOrWhiteSpace(search);
        if (hasSearch)
        {
            var searchExpr = cfg.EmailColumn is null
                ? $"{usernameCol} LIKE @search"
                : $"({usernameCol} LIKE @search OR {emailExpr} LIKE @search)";
            whereClauses.Add(searchExpr);
        }
        return whereClauses.Count == 0 ? "" : " WHERE " + string.Join(" AND ", whereClauses);
    }

    public static async Task<(IReadOnlyList<BrowseRow> Rows, int Total)> BrowseAsync(
        MySqlProviderPublicConfig cfg,
        MySqlProviderSecrets secrets,
        string? search,
        int page,
        int pageSize,
        CancellationToken ct,
        string? sort = null,
        string? dir = null)
    {
        if (page < 0) page = 0;
        if (pageSize <= 0) pageSize = 50;
        if (pageSize > 500) pageSize = 500;

        var table = MySqlIdentifier.Quote(cfg.Table);
        var whereSql = BuildWhereSql(cfg, search, out var hasSearch, out var usernameCol, out var emailExpr);
        var externalIdExpr = cfg.ExternalIdColumn is null
            ? usernameCol
            : MySqlIdentifier.Quote(cfg.ExternalIdColumn);
        var fullNameExpr = cfg.FullNameColumn is null ? "NULL" : MySqlIdentifier.Quote(cfg.FullNameColumn);

        var sortCol = ResolveBrowseSortColumn(cfg, sort);
        var ascending = !"desc".Equals(dir, StringComparison.OrdinalIgnoreCase);

        await using var connection = new MySqlConnection(BuildConnectionString(cfg, secrets));
        await connection.OpenAsync(ct);

        await using (var countCmd = connection.CreateCommand())
        {
            countCmd.CommandText = $"SELECT COUNT(*) FROM {table}{whereSql}";
            countCmd.CommandTimeout = cfg.QueryTimeoutSeconds;
            if (hasSearch) countCmd.Parameters.AddWithValue("@search", $"%{search}%");
            var totalObj = await countCmd.ExecuteScalarAsync(ct);
            var total = Convert.ToInt32(totalObj ?? 0);

            var rows = new List<BrowseRow>(Math.Min(pageSize, total));
            await using var cmd = connection.CreateCommand();
            cmd.CommandText =
                $"SELECT {externalIdExpr} AS external_id, {usernameCol} AS username, {fullNameExpr} AS full_name, {emailExpr} AS email " +
                $"FROM {table}{whereSql} ORDER BY {sortCol} {(ascending ? "ASC" : "DESC")} LIMIT @limit OFFSET @offset";
            cmd.CommandTimeout = cfg.QueryTimeoutSeconds;
            if (hasSearch) cmd.Parameters.AddWithValue("@search", $"%{search}%");
            cmd.Parameters.AddWithValue("@limit", pageSize);
            cmd.Parameters.AddWithValue("@offset", page * pageSize);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                rows.Add(new BrowseRow(
                    ExternalId: reader.IsDBNull(0) ? "" : Convert.ToString(reader.GetValue(0)) ?? "",
                    Username: reader.IsDBNull(1) ? "" : reader.GetString(1),
                    FullName: reader.IsDBNull(2) ? null : reader.GetString(2),
                    Email: reader.IsDBNull(3) ? null : reader.GetString(3)));
            }
            return (rows, total);
        }
    }

    /// <summary>
    /// Devuelve solo los external_id que matchean el filtro. Usado por
    /// la UI para "seleccionar todos los del filtro" sin paginar.
    /// </summary>
    public static async Task<(IReadOnlyList<string> Ids, int Total)> ListExternalIdsAsync(
        MySqlProviderPublicConfig cfg,
        MySqlProviderSecrets secrets,
        string? search,
        int hardLimit,
        CancellationToken ct,
        string? sort = null,
        string? dir = null)
    {
        var table = MySqlIdentifier.Quote(cfg.Table);
        var whereSql = BuildWhereSql(cfg, search, out var hasSearch, out var usernameCol, out _);
        var externalIdExpr = cfg.ExternalIdColumn is null
            ? usernameCol
            : MySqlIdentifier.Quote(cfg.ExternalIdColumn);
        var sortCol = ResolveBrowseSortColumn(cfg, sort);
        var ascending = !"desc".Equals(dir, StringComparison.OrdinalIgnoreCase);

        await using var connection = new MySqlConnection(BuildConnectionString(cfg, secrets));
        await connection.OpenAsync(ct);

        await using var countCmd = connection.CreateCommand();
        countCmd.CommandText = $"SELECT COUNT(*) FROM {table}{whereSql}";
        countCmd.CommandTimeout = cfg.QueryTimeoutSeconds;
        if (hasSearch) countCmd.Parameters.AddWithValue("@search", $"%{search}%");
        var total = Convert.ToInt32(await countCmd.ExecuteScalarAsync(ct) ?? 0);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText =
            $"SELECT {externalIdExpr} FROM {table}{whereSql} " +
            $"ORDER BY {sortCol} {(ascending ? "ASC" : "DESC")} LIMIT @limit";
        cmd.CommandTimeout = cfg.QueryTimeoutSeconds;
        if (hasSearch) cmd.Parameters.AddWithValue("@search", $"%{search}%");
        cmd.Parameters.AddWithValue("@limit", hardLimit);
        var ids = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            ids.Add(reader.IsDBNull(0) ? "" : Convert.ToString(reader.GetValue(0)) ?? "");
        }
        return (ids, total);
    }

    /// <summary>
    /// Recupera filas completas por lista de external IDs. Usado por el
    /// import bulk: el browse ya devolvió los IDs y queremos los datos
    /// completos sin hacer un SELECT por usuario.
    ///
    /// Si <c>ExternalIdColumn</c> no está mapeado, hace match contra
    /// <c>UsernameColumn</c> (caso "uso el username como id estable").
    /// </summary>
    public static async Task<IReadOnlyList<BrowseRow>> FetchByExternalIdsAsync(
        MySqlProviderPublicConfig cfg,
        MySqlProviderSecrets secrets,
        IReadOnlyList<string> externalIds,
        CancellationToken ct)
    {
        if (externalIds.Count == 0) return Array.Empty<BrowseRow>();
        if (externalIds.Count > 500)
            throw new ArgumentException("Máximo 500 ids por consulta.", nameof(externalIds));

        var table = MySqlIdentifier.Quote(cfg.Table);
        var usernameCol = MySqlIdentifier.Quote(cfg.UsernameColumn);
        var idCol = cfg.ExternalIdColumn is null
            ? usernameCol
            : MySqlIdentifier.Quote(cfg.ExternalIdColumn);
        var fullNameExpr = cfg.FullNameColumn is null ? "NULL" : MySqlIdentifier.Quote(cfg.FullNameColumn);
        var emailExpr = cfg.EmailColumn is null ? "NULL" : MySqlIdentifier.Quote(cfg.EmailColumn);

        // Generamos placeholders @p0, @p1... y los rellenamos vía
        // Parameters.AddWithValue para evitar inyección. MySQL no
        // soporta arrays nativos en parámetros.
        var placeholders = string.Join(", ", Enumerable.Range(0, externalIds.Count).Select(i => $"@p{i}"));
        var whereExtra = string.IsNullOrWhiteSpace(cfg.ExtraWhere) ? "" : $" AND ({cfg.ExtraWhere})";
        if (!string.IsNullOrWhiteSpace(cfg.ExtraWhere))
        {
            MySqlIdentifier.ValidateExtraWhere(cfg.ExtraWhere);
        }

        var sql =
            $"SELECT {idCol} AS external_id, {usernameCol} AS username, " +
            $"{fullNameExpr} AS full_name, {emailExpr} AS email " +
            $"FROM {table} WHERE {idCol} IN ({placeholders}){whereExtra}";

        await using var connection = new MySqlConnection(BuildConnectionString(cfg, secrets));
        await connection.OpenAsync(ct);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandTimeout = cfg.QueryTimeoutSeconds;
        for (int i = 0; i < externalIds.Count; i++)
        {
            cmd.Parameters.AddWithValue($"@p{i}", externalIds[i]);
        }

        var rows = new List<BrowseRow>(externalIds.Count);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add(new BrowseRow(
                ExternalId: reader.IsDBNull(0) ? "" : Convert.ToString(reader.GetValue(0)) ?? "",
                Username: reader.IsDBNull(1) ? "" : reader.GetString(1),
                FullName: reader.IsDBNull(2) ? null : reader.GetString(2),
                Email: reader.IsDBNull(3) ? null : reader.GetString(3)));
        }
        return rows;
    }

    public static async Task<IReadOnlyList<string>> ListColumnsAsync(
        MySqlProviderPublicConfig cfg,
        MySqlProviderSecrets secrets,
        string table,
        CancellationToken ct)
    {
        var quoted = MySqlIdentifier.Quote(table);
        await using var connection = new MySqlConnection(BuildConnectionString(cfg, secrets));
        await connection.OpenAsync(ct);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SHOW COLUMNS FROM {quoted}";
        cmd.CommandTimeout = cfg.QueryTimeoutSeconds;
        var columns = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            columns.Add(reader.GetString(0));
        }
        return columns;
    }

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

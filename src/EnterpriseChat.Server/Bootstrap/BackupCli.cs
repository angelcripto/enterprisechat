using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using EnterpriseChat.Server.Data;
using EnterpriseChat.Server.Files;
using Microsoft.Data.Sqlite;

namespace EnterpriseChat.Server.Bootstrap;

/// <summary>
/// CLI helpers para copia de seguridad (<c>--backup</c>) y restauración
/// (<c>--restore</c>) del servidor.
///
/// El backup es consistente aunque haya escrituras en curso: usa
/// <c>VACUUM INTO</c> de SQLite, que serializa la operación a nivel de
/// motor en lugar de hacer un <c>File.Copy</c> del .db abierto. Por eso
/// puede ejecutarse con el servicio corriendo. El restore SÍ requiere
/// el servicio parado — si la base está abierta, las operaciones de
/// borrado/copia del .db fallarán con un mensaje claro.
///
/// Antes de aplicar un restore se hace SIEMPRE un backup automático del
/// estado actual a <c>data/backup-pre-restore-{timestamp}.zip</c> como
/// red de seguridad. Si esa copia previa falla, se aborta sin tocar
/// nada.
///
/// El zip contiene:
///   - <c>manifest.json</c>: versión de formato, fecha, versión del
///     servidor que lo generó, lista de archivos con sha256.
///   - <c>chat.db</c>: snapshot vía <c>VACUUM INTO</c>.
///   - <c>master.key</c>: clave maestra del crypto (si existe en
///     fichero — no se incluye si se inyecta sólo vía configuración).
///   - <c>attachments/...</c>: contenido recursivo de
///     <c>data/attachments</c>.
///
/// El zip NO incluye <c>appsettings.Production.json</c> — las claves
/// de JWT, hashes de contraseña inicial y resto de secretos seguirán
/// siendo los de la máquina destino. Si el operador necesita moverlos,
/// lo hará manualmente.
/// </summary>
internal static class BackupCli
{
    public const string BackupFlag = "--backup";
    public const string RestoreFlag = "--restore";
    private const string ForceFlag = "--force";
    private const string YesFlag = "--yes";

    internal const int CurrentSchemaVersion = 1;
    internal const string ManifestEntry = "manifest.json";
    internal const string ChatDbEntry = "chat.db";
    internal const string MasterKeyEntry = "master.key";
    internal const string AttachmentsPrefix = "attachments/";

    public static bool TryExtractBackup(string[] args, out string path, out bool force, out string[] remaining)
        => TryExtractCommand(args, BackupFlag, ForceFlag, out path, out force, out remaining);

    public static bool TryExtractRestore(string[] args, out string path, out bool yes, out string[] remaining)
        => TryExtractCommand(args, RestoreFlag, YesFlag, out path, out yes, out remaining);

    private static bool TryExtractCommand(
        string[] args,
        string primaryFlag,
        string secondaryFlag,
        out string path,
        out bool secondaryPresent,
        out string[] remaining)
    {
        path = string.Empty;
        secondaryPresent = false;
        var list = new List<string>(args.Length);
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].Equals(primaryFlag, StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    throw new ArgumentException($"{primaryFlag} requiere una ruta como argumento.");
                }
                path = args[i + 1];
                i++;
                continue;
            }
            if (args[i].Equals(secondaryFlag, StringComparison.OrdinalIgnoreCase))
            {
                secondaryPresent = true;
                continue;
            }
            list.Add(args[i]);
        }
        remaining = list.ToArray();
        return !string.IsNullOrEmpty(path);
    }

    public static async Task<int> RunBackupAsync(string outputPath, bool force, string contentRoot)
    {
        var outputAbsolute = Path.GetFullPath(outputPath);
        if (File.Exists(outputAbsolute) && !force)
        {
            Console.Error.WriteLine($"Ya existe '{outputAbsolute}'. Usa --force para sobrescribir.");
            return 2;
        }

        var dataDir = Path.Combine(contentRoot, MasterKeyInitializer.DataDirName);
        var dbPath = Path.Combine(dataDir, "chat.db");
        if (!File.Exists(dbPath))
        {
            Console.Error.WriteLine($"No se encontró la base de datos en '{dbPath}'.");
            return 3;
        }

        var attachmentsDir = Path.Combine(contentRoot, FileEndpoints.AttachmentsSubDir);
        var masterKeyPath = Path.Combine(dataDir, MasterKeyInitializer.KeyFileName);

        var staging = Directory.CreateTempSubdirectory("ec-backup-").FullName;
        try
        {
            // 1. Snapshot consistente de la BD usando VACUUM INTO. Funciona
            //    aunque el servidor esté escribiendo: SQLite serializa la
            //    operación a nivel de motor y produce un fichero limpio sin
            //    -wal / -shm asociados.
            var stagedDb = Path.Combine(staging, ChatDbEntry);
            var connStr = new SqliteConnectionStringBuilder
            {
                DataSource = dbPath,
                Mode = SqliteOpenMode.ReadOnly,
                Cache = SqliteCacheMode.Shared
            }.ConnectionString;
            await using (var conn = new SqliteConnection(connStr))
            {
                await conn.OpenAsync();
                await using var cmd = conn.CreateCommand();
                // VACUUM INTO no admite parámetros bindados — hay que inlinearlo.
                var escapedPath = stagedDb.Replace("'", "''");
                cmd.CommandText = $"VACUUM INTO '{escapedPath}';";
                await cmd.ExecuteNonQueryAsync();
            }

            // 2. master.key — sólo si está como fichero (en setups con secret
            //    manager puede no existir y no es un error).
            string? stagedKey = null;
            if (File.Exists(masterKeyPath))
            {
                stagedKey = Path.Combine(staging, MasterKeyEntry);
                File.Copy(masterKeyPath, stagedKey);
            }

            // 3. attachments/ recursivo.
            var attachmentEntries = new List<string>();
            if (Directory.Exists(attachmentsDir))
            {
                var stagedAttachments = Path.Combine(staging, "attachments");
                Directory.CreateDirectory(stagedAttachments);
                foreach (var file in Directory.EnumerateFiles(attachmentsDir, "*", SearchOption.AllDirectories))
                {
                    var rel = Path.GetRelativePath(attachmentsDir, file).Replace('\\', '/');
                    var dest = Path.Combine(stagedAttachments, rel.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    File.Copy(file, dest);
                    attachmentEntries.Add(AttachmentsPrefix + rel);
                }
            }

            // 4. Manifest con sha256 de cada archivo.
            var manifestFiles = new List<BackupManifestFile>
            {
                BackupManifestFile.From(ChatDbEntry, stagedDb)
            };
            if (stagedKey is not null)
            {
                manifestFiles.Add(BackupManifestFile.From(MasterKeyEntry, stagedKey));
            }
            foreach (var entry in attachmentEntries)
            {
                var full = Path.Combine(staging, entry.Replace('/', Path.DirectorySeparatorChar));
                manifestFiles.Add(BackupManifestFile.From(entry, full));
            }

            var manifest = new BackupManifest(
                SchemaVersion: CurrentSchemaVersion,
                CreatedAt: DateTimeOffset.UtcNow,
                ServerVersion: typeof(BackupCli).Assembly.GetName().Version?.ToString() ?? "0.0.0",
                Files: manifestFiles);

            var manifestJson = JsonSerializer.Serialize(manifest, BackupJson.Options);
            await File.WriteAllTextAsync(Path.Combine(staging, ManifestEntry), manifestJson);

            // 5. ZIP final.
            if (File.Exists(outputAbsolute))
            {
                File.Delete(outputAbsolute);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(outputAbsolute)!);
            }
            ZipFile.CreateFromDirectory(staging, outputAbsolute, CompressionLevel.Optimal, includeBaseDirectory: false);

            var size = new FileInfo(outputAbsolute).Length;
            Console.WriteLine($"Copia de seguridad creada en '{outputAbsolute}'.");
            Console.WriteLine($"  Tamaño: {FormatSize(size)}");
            Console.WriteLine($"  Archivos: {manifestFiles.Count} ({attachmentEntries.Count} adjuntos)");
            return 0;
        }
        finally
        {
            TryDeleteDirectory(staging);
        }
    }

    public static Task<int> RunRestoreAsync(string inputPath, bool yes, string contentRoot)
        => RunRestoreAsync(inputPath, yes, contentRoot, applyMigrations: true);

    /// <summary>
    /// Variante usada por los tests: permite saltarse la fase de migraciones
    /// EF Core cuando la BD del backup no es un schema EnterpriseChat real
    /// (los tests crean SQLite "a mano" con una tabla cualquiera para no tener
    /// que arrancar un host completo).
    /// </summary>
    internal static async Task<int> RunRestoreAsync(string inputPath, bool yes, string contentRoot, bool applyMigrations)
    {
        var inputAbsolute = Path.GetFullPath(inputPath);
        if (!File.Exists(inputAbsolute))
        {
            Console.Error.WriteLine($"No se encontró el archivo de copia de seguridad: '{inputAbsolute}'.");
            return 2;
        }

        if (!yes)
        {
            if (Console.IsInputRedirected)
            {
                Console.Error.WriteLine("La restauración requiere confirmación interactiva o el flag --yes.");
                return 1;
            }
            Console.Write("Esto sobrescribirá los datos actuales del servidor. ¿Continuar? (s/N): ");
            var response = Console.ReadLine()?.Trim();
            if (!IsAffirmative(response))
            {
                Console.WriteLine("Restauración cancelada.");
                return 1;
            }
        }

        // Red de seguridad: copia previa del estado actual antes de tocar nada.
        var dataDir = Path.Combine(contentRoot, MasterKeyInitializer.DataDirName);
        Directory.CreateDirectory(dataDir);
        var preRestoreBackup = Path.Combine(
            dataDir,
            $"backup-pre-restore-{DateTime.UtcNow:yyyyMMddHHmmss}.zip");
        if (File.Exists(Path.Combine(dataDir, "chat.db")))
        {
            var safetyResult = await RunBackupAsync(preRestoreBackup, force: false, contentRoot);
            if (safetyResult != 0)
            {
                Console.Error.WriteLine("No se pudo crear la copia previa al restore. Abortando para no dejar el servidor en estado inconsistente.");
                return safetyResult;
            }
            Console.WriteLine($"Copia previa guardada en '{preRestoreBackup}'.");
        }

        var staging = Directory.CreateTempSubdirectory("ec-restore-").FullName;
        try
        {
            try
            {
                ZipFile.ExtractToDirectory(inputAbsolute, staging);
            }
            catch (InvalidDataException ex)
            {
                Console.Error.WriteLine($"El archivo no es un zip válido: {ex.Message}");
                return 3;
            }

            var manifestPath = Path.Combine(staging, ManifestEntry);
            if (!File.Exists(manifestPath))
            {
                Console.Error.WriteLine("El zip no contiene manifest.json. ¿Archivo corrupto o no generado por EnterpriseChat?");
                return 3;
            }

            BackupManifest manifest;
            try
            {
                manifest = JsonSerializer.Deserialize<BackupManifest>(
                    await File.ReadAllTextAsync(manifestPath), BackupJson.Options)
                    ?? throw new InvalidOperationException("manifest.json vacío.");
            }
            catch (JsonException ex)
            {
                Console.Error.WriteLine($"manifest.json corrupto: {ex.Message}");
                return 3;
            }

            if (manifest.SchemaVersion > CurrentSchemaVersion)
            {
                Console.Error.WriteLine(
                    $"Versión de formato no soportada (zip: {manifest.SchemaVersion}, este servidor: {CurrentSchemaVersion}). " +
                    "Actualiza EnterpriseChat antes de restaurar.");
                return 4;
            }

            // Verificación de integridad: sha256 de cada archivo contra el manifest.
            foreach (var file in manifest.Files)
            {
                var full = Path.Combine(staging, file.Path.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(full))
                {
                    Console.Error.WriteLine($"Falta en el zip un archivo declarado en manifest.json: {file.Path}");
                    return 3;
                }
                var actual = ComputeSha256(full);
                if (!string.Equals(actual, file.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    Console.Error.WriteLine($"Hash no coincide para {file.Path}. Archivo corrupto o modificado.");
                    return 3;
                }
            }

            // Aplicar: reemplazar chat.db, master.key y attachments/.
            var dbPath = Path.Combine(dataDir, "chat.db");
            var stagedDb = Path.Combine(staging, ChatDbEntry);
            DeleteIfExists(dbPath);
            DeleteIfExists(dbPath + "-wal");
            DeleteIfExists(dbPath + "-shm");
            File.Copy(stagedDb, dbPath, overwrite: true);

            var stagedKey = Path.Combine(staging, MasterKeyEntry);
            if (File.Exists(stagedKey))
            {
                var masterKeyPath = Path.Combine(dataDir, MasterKeyInitializer.KeyFileName);
                File.Copy(stagedKey, masterKeyPath, overwrite: true);
            }

            var attachmentsDir = Path.Combine(contentRoot, FileEndpoints.AttachmentsSubDir);
            if (Directory.Exists(attachmentsDir))
            {
                Directory.Delete(attachmentsDir, recursive: true);
            }
            var stagedAttachments = Path.Combine(staging, "attachments");
            if (Directory.Exists(stagedAttachments))
            {
                Directory.CreateDirectory(attachmentsDir);
                foreach (var file in Directory.EnumerateFiles(stagedAttachments, "*", SearchOption.AllDirectories))
                {
                    var rel = Path.GetRelativePath(stagedAttachments, file);
                    var dest = Path.Combine(attachmentsDir, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    File.Copy(file, dest, overwrite: true);
                }
            }

            // Aplicar migraciones por si la BD restaurada es de un servidor
            // más antiguo. Se hace cambiando temporalmente el CWD para que
            // PersistenceExtensions resuelva la connection string relativa
            // (Data Source=data/chat.db) contra el contentRoot correcto.
            if (applyMigrations)
            {
                var previousCwd = Directory.GetCurrentDirectory();
                try
                {
                    Directory.SetCurrentDirectory(contentRoot);
                    var migrationBuilder = WebApplication.CreateBuilder(Array.Empty<string>());
                    migrationBuilder.Services.AddChatPersistence(migrationBuilder.Configuration);
                    await using var migApp = migrationBuilder.Build();
                    await migApp.Services.InitializeChatDatabaseAsync();
                }
                finally
                {
                    Directory.SetCurrentDirectory(previousCwd);
                }
            }

            var attachmentCount = manifest.Files.Count(f =>
                f.Path.StartsWith(AttachmentsPrefix, StringComparison.Ordinal));
            Console.WriteLine($"Restauración completada desde '{inputAbsolute}'.");
            Console.WriteLine($"  Adjuntos restaurados: {attachmentCount}");
            return 0;
        }
        finally
        {
            TryDeleteDirectory(staging);
        }
    }

    private static bool IsAffirmative(string? response)
    {
        if (string.IsNullOrWhiteSpace(response)) return false;
        return response.Equals("s", StringComparison.OrdinalIgnoreCase)
            || response.Equals("si", StringComparison.OrdinalIgnoreCase)
            || response.Equals("sí", StringComparison.OrdinalIgnoreCase)
            || response.Equals("y", StringComparison.OrdinalIgnoreCase)
            || response.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    private static void TryDeleteDirectory(string path)
    {
        try { Directory.Delete(path, recursive: true); }
        catch { /* best effort */ }
    }

    internal static string ComputeSha256(string path)
    {
        using var sha = SHA256.Create();
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
    }
}

internal sealed record BackupManifest(
    int SchemaVersion,
    DateTimeOffset CreatedAt,
    string ServerVersion,
    IReadOnlyList<BackupManifestFile> Files);

internal sealed record BackupManifestFile(string Path, long Size, string Sha256)
{
    public static BackupManifestFile From(string entryPath, string fullPath)
    {
        var info = new FileInfo(fullPath);
        var hash = BackupCli.ComputeSha256(fullPath);
        return new BackupManifestFile(entryPath, info.Length, hash);
    }
}

internal static class BackupJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}

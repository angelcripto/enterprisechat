using System.IO.Compression;
using System.Text;
using System.Text.Json;
using EnterpriseChat.Server.Bootstrap;
using EnterpriseChat.Server.Files;
using FluentAssertions;
using Microsoft.Data.Sqlite;

namespace EnterpriseChat.Tests.Server;

/// <summary>
/// Smoke tests del CLI de backup/restore. No usa <see cref="ChatServerFactory"/>
/// porque <see cref="BackupCli"/> opera sobre el sistema de archivos y SQLite
/// directamente, sin necesidad de host HTTP. Cada test crea un
/// <c>contentRoot</c> aislado en <c>%TEMP%</c> y lo borra al final.
/// </summary>
public sealed class BackupCliTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    [Fact]
    public async Task RunBackup_creates_zip_with_manifest_db_and_attachments()
    {
        var contentRoot = CreateSeededContentRoot();
        var outputPath = Path.Combine(NewTempDir(), "backup.zip");

        var exitCode = await BackupCli.RunBackupAsync(outputPath, force: false, contentRoot);

        exitCode.Should().Be(0);
        File.Exists(outputPath).Should().BeTrue();

        using var zip = ZipFile.OpenRead(outputPath);
        zip.GetEntry(BackupCli.ManifestEntry).Should().NotBeNull("el zip debe llevar manifest.json");
        zip.GetEntry(BackupCli.ChatDbEntry).Should().NotBeNull("el zip debe llevar chat.db");
        zip.GetEntry(BackupCli.MasterKeyEntry).Should().NotBeNull("el zip debe llevar master.key cuando existe");
        zip.GetEntry(BackupCli.AttachmentsPrefix + "foo.txt").Should().NotBeNull();

        var manifest = ReadManifest(zip);
        manifest.SchemaVersion.Should().Be(BackupCli.CurrentSchemaVersion);
        manifest.Files.Should().HaveCount(3, "chat.db + master.key + 1 attachment");
        manifest.Files.Should().OnlyContain(f => !string.IsNullOrEmpty(f.Sha256) && f.Size > 0);
    }

    [Fact]
    public async Task RunBackup_without_force_refuses_to_overwrite_existing_file()
    {
        var contentRoot = CreateSeededContentRoot();
        var outputPath = Path.Combine(NewTempDir(), "backup.zip");
        await File.WriteAllTextAsync(outputPath, "ya existía");

        var exitCode = await BackupCli.RunBackupAsync(outputPath, force: false, contentRoot);

        exitCode.Should().Be(2);
        (await File.ReadAllTextAsync(outputPath)).Should().Be("ya existía", "no debe haberse sobrescrito");
    }

    [Fact]
    public async Task RunBackup_with_force_overwrites_existing_file()
    {
        var contentRoot = CreateSeededContentRoot();
        var outputPath = Path.Combine(NewTempDir(), "backup.zip");
        await File.WriteAllTextAsync(outputPath, "ya existía");

        var exitCode = await BackupCli.RunBackupAsync(outputPath, force: true, contentRoot);

        exitCode.Should().Be(0);
        new FileInfo(outputPath).Length.Should().BeGreaterThan(100, "el archivo debe ser un zip real, no el texto previo");
    }

    [Fact]
    public async Task RunBackup_fails_clearly_when_chat_db_is_missing()
    {
        var contentRoot = NewTempDir();
        Directory.CreateDirectory(Path.Combine(contentRoot, "data")); // sin chat.db
        var outputPath = Path.Combine(NewTempDir(), "backup.zip");

        var exitCode = await BackupCli.RunBackupAsync(outputPath, force: false, contentRoot);

        exitCode.Should().Be(3);
        File.Exists(outputPath).Should().BeFalse();
    }

    [Fact]
    public async Task RunBackup_succeeds_when_master_key_is_absent()
    {
        var contentRoot = CreateSeededContentRoot(includeMasterKey: false);
        var outputPath = Path.Combine(NewTempDir(), "backup.zip");

        var exitCode = await BackupCli.RunBackupAsync(outputPath, force: false, contentRoot);

        exitCode.Should().Be(0);
        using var zip = ZipFile.OpenRead(outputPath);
        zip.GetEntry(BackupCli.MasterKeyEntry).Should().BeNull("no debe inventar la master.key si no existe");
        ReadManifest(zip).Files.Should().NotContain(f => f.Path == BackupCli.MasterKeyEntry);
    }

    [Fact]
    public async Task RunRestore_round_trip_restores_db_master_key_and_attachments()
    {
        // 1. Crear origen con datos.
        var sourceRoot = CreateSeededContentRoot();
        var originalKey = await File.ReadAllTextAsync(Path.Combine(sourceRoot, "data", MasterKeyInitializer.KeyFileName));
        var originalAttachment = await File.ReadAllBytesAsync(Path.Combine(sourceRoot, FileEndpoints.AttachmentsSubDir, "foo.txt"));
        var backupPath = Path.Combine(NewTempDir(), "backup.zip");
        (await BackupCli.RunBackupAsync(backupPath, force: false, sourceRoot)).Should().Be(0);

        // 2. Restaurar en otro contentRoot limpio.
        var destRoot = NewTempDir();
        var exitCode = await BackupCli.RunRestoreAsync(
            backupPath, yes: true, destRoot, applyMigrations: false);

        // 3. Verificar.
        exitCode.Should().Be(0);
        File.Exists(Path.Combine(destRoot, "data", "chat.db")).Should().BeTrue();
        (await File.ReadAllTextAsync(Path.Combine(destRoot, "data", MasterKeyInitializer.KeyFileName)))
            .Should().Be(originalKey);
        var restoredAttachment = await File.ReadAllBytesAsync(Path.Combine(destRoot, FileEndpoints.AttachmentsSubDir, "foo.txt"));
        restoredAttachment.Should().BeEquivalentTo(originalAttachment);

        // La BD restaurada conserva los datos (el snapshot de VACUUM INTO).
        using var conn = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(destRoot, "data", "chat.db"),
            Mode = SqliteOpenMode.ReadOnly
        }.ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM TestTable WHERE id = 1;";
        var name = (string?)await cmd.ExecuteScalarAsync();
        name.Should().Be("hola");
    }

    [Fact]
    public async Task RunRestore_creates_pre_restore_safety_backup_when_data_exists()
    {
        var sourceRoot = CreateSeededContentRoot();
        var backupPath = Path.Combine(NewTempDir(), "backup.zip");
        (await BackupCli.RunBackupAsync(backupPath, force: false, sourceRoot)).Should().Be(0);

        // Restaurar SOBRE un contentRoot que ya tiene datos: tiene que generar
        // backup-pre-restore-*.zip antes de tocar nada.
        var destRoot = CreateSeededContentRoot();
        var dataDir = Path.Combine(destRoot, "data");

        var exitCode = await BackupCli.RunRestoreAsync(
            backupPath, yes: true, destRoot, applyMigrations: false);

        exitCode.Should().Be(0);
        Directory.EnumerateFiles(dataDir, "backup-pre-restore-*.zip")
            .Should().HaveCount(1, "el restore debe haber generado una copia de seguridad previa");
    }

    [Fact]
    public async Task RunRestore_rejects_input_that_is_not_a_valid_zip()
    {
        var destRoot = NewTempDir();
        var notAZip = Path.Combine(NewTempDir(), "garbage.zip");
        await File.WriteAllTextAsync(notAZip, "esto no es un zip");

        var exitCode = await BackupCli.RunRestoreAsync(
            notAZip, yes: true, destRoot, applyMigrations: false);

        exitCode.Should().Be(3);
    }

    [Fact]
    public async Task RunRestore_rejects_zip_without_manifest()
    {
        var destRoot = NewTempDir();
        var zipPath = Path.Combine(NewTempDir(), "no-manifest.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("unrelated.txt");
            using var s = entry.Open();
            s.Write(Encoding.UTF8.GetBytes("data"));
        }

        var exitCode = await BackupCli.RunRestoreAsync(
            zipPath, yes: true, destRoot, applyMigrations: false);

        exitCode.Should().Be(3);
    }

    [Fact]
    public async Task RunRestore_rejects_zip_with_unsupported_schema_version()
    {
        var destRoot = NewTempDir();
        var zipPath = await BuildBackupWithCustomManifestAsync(
            schemaVersion: BackupCli.CurrentSchemaVersion + 100);

        var exitCode = await BackupCli.RunRestoreAsync(
            zipPath, yes: true, destRoot, applyMigrations: false);

        exitCode.Should().Be(4);
    }

    [Fact]
    public async Task RunRestore_rejects_zip_with_tampered_file_hash()
    {
        var sourceRoot = CreateSeededContentRoot();
        var backupPath = Path.Combine(NewTempDir(), "backup.zip");
        (await BackupCli.RunBackupAsync(backupPath, force: false, sourceRoot)).Should().Be(0);

        // Reescribimos chat.db dentro del zip con contenido distinto, sin
        // tocar el manifest — el restore tiene que detectarlo por sha256.
        using (var archive = ZipFile.Open(backupPath, ZipArchiveMode.Update))
        {
            archive.GetEntry(BackupCli.ChatDbEntry)!.Delete();
            var newEntry = archive.CreateEntry(BackupCli.ChatDbEntry);
            using var s = newEntry.Open();
            s.Write(Encoding.UTF8.GetBytes("contenido falsificado"));
        }

        var destRoot = NewTempDir();
        var exitCode = await BackupCli.RunRestoreAsync(
            backupPath, yes: true, destRoot, applyMigrations: false);

        exitCode.Should().Be(3);
    }

    [Fact]
    public async Task RunRestore_fails_when_input_file_does_not_exist()
    {
        var destRoot = NewTempDir();
        var missingZip = Path.Combine(NewTempDir(), "no-existe.zip");

        var exitCode = await BackupCli.RunRestoreAsync(
            missingZip, yes: true, destRoot, applyMigrations: false);

        exitCode.Should().Be(2);
    }

    [Fact]
    public void TryExtractBackup_parses_flag_and_force_correctly()
    {
        var ok = BackupCli.TryExtractBackup(
            new[] { "--backup", "C:\\salida.zip", "--force", "--otra-cosa" },
            out var path, out var force, out var remaining);

        ok.Should().BeTrue();
        path.Should().Be("C:\\salida.zip");
        force.Should().BeTrue();
        remaining.Should().BeEquivalentTo(new[] { "--otra-cosa" });
    }

    [Fact]
    public void TryExtractRestore_parses_flag_and_yes_correctly()
    {
        var ok = BackupCli.TryExtractRestore(
            new[] { "--restore", "C:\\entrada.zip", "--yes" },
            out var path, out var yes, out var remaining);

        ok.Should().BeTrue();
        path.Should().Be("C:\\entrada.zip");
        yes.Should().BeTrue();
        remaining.Should().BeEmpty();
    }

    [Fact]
    public void TryExtractBackup_throws_when_path_is_missing()
    {
        var act = () => BackupCli.TryExtractBackup(
            new[] { "--backup" }, out _, out _, out _);

        act.Should().Throw<ArgumentException>();
    }

    // ---------- Helpers ----------

    private string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"ec-backup-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }

    private string CreateSeededContentRoot(bool includeMasterKey = true)
    {
        var root = NewTempDir();
        var dataDir = Path.Combine(root, "data");
        var attachmentsDir = Path.Combine(root, FileEndpoints.AttachmentsSubDir);
        Directory.CreateDirectory(dataDir);
        Directory.CreateDirectory(attachmentsDir);

        // Crear una BD SQLite real con una tabla y una fila para que VACUUM INTO
        // tenga algo que copiar. No usa el schema EF Core real porque los tests
        // no aplican migraciones (RunRestoreAsync con applyMigrations:false).
        var dbPath = Path.Combine(dataDir, "chat.db");
        using (var conn = new SqliteConnection($"Data Source={dbPath}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE TABLE TestTable (id INTEGER PRIMARY KEY, name TEXT NOT NULL); INSERT INTO TestTable (id, name) VALUES (1, 'hola');";
            cmd.ExecuteNonQuery();
        }

        if (includeMasterKey)
        {
            File.WriteAllText(
                Path.Combine(dataDir, MasterKeyInitializer.KeyFileName),
                "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=");
        }

        File.WriteAllBytes(Path.Combine(attachmentsDir, "foo.txt"), Encoding.UTF8.GetBytes("contenido adjunto"));
        return root;
    }

    private async Task<string> BuildBackupWithCustomManifestAsync(int schemaVersion)
    {
        var staging = NewTempDir();
        var dbPath = Path.Combine(staging, BackupCli.ChatDbEntry);
        await File.WriteAllBytesAsync(dbPath, new byte[] { 1, 2, 3 });
        var hash = BackupCli.ComputeSha256(dbPath);

        var manifest = new BackupManifest(
            SchemaVersion: schemaVersion,
            CreatedAt: DateTimeOffset.UtcNow,
            ServerVersion: "0.0.0-test",
            Files: new[] { new BackupManifestFile(BackupCli.ChatDbEntry, 3, hash) });
        await File.WriteAllTextAsync(
            Path.Combine(staging, BackupCli.ManifestEntry),
            JsonSerializer.Serialize(manifest, BackupJson.Options));

        var zipPath = Path.Combine(NewTempDir(), "future.zip");
        ZipFile.CreateFromDirectory(staging, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);
        return zipPath;
    }

    private static BackupManifest ReadManifest(ZipArchive zip)
    {
        var entry = zip.GetEntry(BackupCli.ManifestEntry)
            ?? throw new InvalidOperationException("manifest.json no encontrado en el zip");
        using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var json = reader.ReadToEnd();
        return JsonSerializer.Deserialize<BackupManifest>(json, BackupJson.Options)
            ?? throw new InvalidOperationException("manifest.json vacío o malformado");
    }

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
        {
            try { Directory.Delete(dir, recursive: true); }
            catch { /* best effort */ }
        }
    }
}

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace EnterpriseChat.Tests.Server;

/// <summary>
/// Test host for the EnterpriseChat server. Each instance writes its SQLite
/// database to a unique temp path that is deleted on dispose, so tests are
/// fully isolated.
/// </summary>
public sealed class ChatServerFactory : WebApplicationFactory<Program>
{
    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(),
        $"enterprisechat-test-{Guid.NewGuid():N}.db");

    /// <summary>Bootstrap admin password used by the seeder; tests use this to log in.</summary>
    public const string AdminPassword = "test-admin-password";

    public string DbPath => _dbPath;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // Por defecto el host de tests apunta a `bin/Debug/net8.0/wwwroot`
        // del runner, que no existe (el SPA se construye en el csproj del
        // server). Lo redirigimos al wwwroot real para que UseStaticFiles
        // pueda servir `index.html`, `docs/*.md`, etc. Si no encontramos
        // el .sln (test ejecutado desde un layout distinto) caemos al
        // default sin romper.
        var serverWwwRoot = ResolveServerWwwRoot();
        if (serverWwwRoot is not null)
        {
            builder.UseWebRoot(serverWwwRoot);
        }

        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Sqlite"] = $"Data Source={_dbPath};Cache=Shared;Foreign Keys=true",
                ["EnterpriseChat:Jwt:SigningKey"] = "test-signing-key-must-be-at-least-32-chars-long",
                ["EnterpriseChat:Jwt:Issuer"] = "EnterpriseChat.Test",
                ["EnterpriseChat:Jwt:Audience"] = "EnterpriseChat.Clients.Test",
                ["EnterpriseChat:Jwt:AccessTokenLifetimeMinutes"] = "30",
                ["EnterpriseChat:Bootstrap:AdminPassword"] = AdminPassword
            });
        });
    }

    /// <summary>
    /// Sube desde <c>AppContext.BaseDirectory</c> hasta el directorio que
    /// contiene <c>EnterpriseChat.sln</c> y devuelve el wwwroot del server.
    /// Devuelve null si no encuentra la solution (tests fuera del repo).
    /// </summary>
    private static string? ResolveServerWwwRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "EnterpriseChat.sln")))
            {
                var candidate = Path.Combine(dir.FullName, "src", "EnterpriseChat.Server", "wwwroot");
                return Directory.Exists(candidate) ? candidate : null;
            }
            dir = dir.Parent;
        }
        return null;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            foreach (var file in new[] { _dbPath, _dbPath + "-shm", _dbPath + "-wal" })
            {
                if (File.Exists(file))
                {
                    try { File.Delete(file); } catch (IOException) { /* held briefly */ }
                }
            }
        }
    }
}

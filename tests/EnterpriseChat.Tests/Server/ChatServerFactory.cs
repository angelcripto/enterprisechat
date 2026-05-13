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

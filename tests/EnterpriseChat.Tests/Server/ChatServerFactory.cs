using EnterpriseChat.Licensing.Abstractions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EnterpriseChat.Tests.Server;

/// <summary>
/// Test host for the EnterpriseChat server. Each instance writes its SQLite
/// database to a unique temp path that is deleted on dispose, so tests are
/// fully isolated.
/// </summary>
public class ChatServerFactory : WebApplicationFactory<Program>
{
    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(),
        $"enterprisechat-test-{Guid.NewGuid():N}.db");

    /// <summary>Bootstrap admin password used by the seeder; tests use this to log in.</summary>
    public const string AdminPassword = "test-admin-password";

    public string DbPath => _dbPath;

    /// <summary>
    /// Tope de cuentas del host de test. <c>null</c> deja el validador real, que
    /// en un servidor sin serial activado es el Free anónimo
    /// (<see cref="FreeLicenseValidator.FreeUserCap"/> cuentas).
    ///
    /// Los tests que necesitan más cuentas que ese tope y NO están probando
    /// licenciamiento usan <see cref="LicensedChatServerFactory"/>. El
    /// licenciamiento tiene sus propios tests dedicados
    /// (FreeLicenseValidatorTests, ConcurrentSessionCounterTests) y el endpoint
    /// /license lo cubre HostBootstrapSmokeTests contra el validador real, así
    /// que subir el tope aquí no deja nada sin verificar.
    /// </summary>
    protected virtual int? LicenseCapOverride => null;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        if (LicenseCapOverride is int cap)
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ILicenseValidator>();
                services.AddSingleton<ILicenseValidator>(new FixedCapLicenseValidator(cap));
            });
        }

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

/// <summary>
/// Host de test con una licencia holgada. Lo usan las clases que crean más
/// cuentas que el tope Free anónimo porque comparten una única BD entre todos
/// sus <c>[Fact]</c> (IClassFixture) y por tanto los usuarios se acumulan:
/// RoomsIntegrationTests crea 6 + admin, SearchTests 5 + admin.
///
/// Sin esto fallan con 403 al crear el sexto usuario, que es el
/// comportamiento CORRECTO del producto — pero esos tests van de salas y
/// búsqueda, no de licenciamiento.
/// </summary>
public sealed class LicensedChatServerFactory : ChatServerFactory
{
    protected override int? LicenseCapOverride => 100;
}

/// <summary>Validador de licencia de test: tope fijo, sin caducidad.</summary>
internal sealed class FixedCapLicenseValidator : ILicenseValidator
{
    private readonly int _cap;

    public FixedCapLicenseValidator(int cap)
    {
        _cap = cap;
        Current = new LicenseInfo(
            Edition: LicenseEdition.Pro,
            MaxConcurrentUsers: cap,
            ExpiresAt: null,
            LicensedTo: "Suite de tests",
            LicenseId: null);
    }

    public LicenseInfo Current { get; }

    public LicenseAdmissionResult TryAdmitSession(int currentActiveSessions)
        => currentActiveSessions < _cap
            ? LicenseAdmissionResult.Allow()
            : LicenseAdmissionResult.Deny($"Test: límite de {_cap} alcanzado.");
}

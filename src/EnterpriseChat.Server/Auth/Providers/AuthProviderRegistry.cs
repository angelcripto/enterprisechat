using System.Text.Json;
using EnterpriseChat.Server.Auth.Hashers;
using EnterpriseChat.Server.Auth.Providers.MySql;
using EnterpriseChat.Server.Crypto;
using EnterpriseChat.Server.Data;
using EnterpriseChat.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseChat.Server.Auth.Providers;

/// <summary>
/// Snapshot vivo de los providers configurados. Se carga al arranque
/// desde <c>AuthProviderConfig</c> y se refresca con
/// <see cref="ReloadAsync"/> cuando el admin modifica la configuración.
///
/// El provider Internal está siempre presente: es un singleton inyectado
/// por DI y nunca se reemplaza.
/// </summary>
public sealed class AuthProviderRegistry
{
    private readonly IDbContextFactory<ChatDbContext> _dbFactory;
    private readonly HashVerifierRegistry _verifiers;
    private readonly AppCrypto _crypto;
    private readonly InternalAuthProvider _internal;
    private readonly ILogger<AuthProviderRegistry> _log;
    private readonly object _swap = new();
    private IReadOnlyList<IAuthProvider> _providers;

    public AuthProviderRegistry(
        IDbContextFactory<ChatDbContext> dbFactory,
        HashVerifierRegistry verifiers,
        AppCrypto crypto,
        InternalAuthProvider @internal,
        ILogger<AuthProviderRegistry> log)
    {
        _dbFactory = dbFactory;
        _verifiers = verifiers;
        _crypto = crypto;
        _internal = @internal;
        _log = log;
        _providers = new IAuthProvider[] { @internal };
    }

    public IReadOnlyList<IAuthProvider> All => _providers;

    public IAuthProvider Internal => _internal;

    /// <summary>
    /// Lee <c>AuthProviderConfig</c> y reconstruye la lista de
    /// providers. Idempotente; se llama en bootstrap y tras cada PUT
    /// del admin para que el cambio surta efecto sin reiniciar.
    /// </summary>
    public async Task ReloadAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var rows = await db.AuthProviders
            .Where(p => p.IsEnabled)
            .OrderBy(p => p.Priority)
            .ThenBy(p => p.Id)
            .AsNoTracking()
            .ToListAsync(ct);

        var loaded = new List<IAuthProvider> { _internal };
        foreach (var row in rows)
        {
            try
            {
                var provider = Materialize(row);
                if (provider is not null) loaded.Add(provider);
            }
            catch (Exception ex)
            {
                _log.LogError(ex,
                    "Provider #{Id} ({DisplayName}, {Kind}) no se pudo cargar; queda inactivo.",
                    row.Id, row.DisplayName, row.Kind);
            }
        }

        lock (_swap)
        {
            _providers = loaded;
        }
        _log.LogInformation("AuthProviderRegistry recargado: {Count} providers activos.", loaded.Count);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private IAuthProvider? Materialize(AuthProviderConfig row)
    {
        switch (row.Kind)
        {
            case AuthProviderKind.Mysql:
            {
                var pub = JsonSerializer.Deserialize<MySqlProviderPublicConfig>(row.ConfigJson, JsonOptions)
                    ?? throw new InvalidOperationException("ConfigJson de MySQL vacío.");
                var secretsJson = string.IsNullOrEmpty(row.EncryptedSecretsJson)
                    ? "{}"
                    : _crypto.DecryptString(row.EncryptedSecretsJson);
                var secrets = JsonSerializer.Deserialize<MySqlProviderSecrets>(secretsJson, JsonOptions)
                    ?? new MySqlProviderSecrets();
                var verifier = _verifiers.Get(row.HashAlgorithm);
                return new MySqlAuthProvider(
                    row.Id, row.DisplayName, pub, secrets, row.HashAlgorithm, verifier);
            }
            case AuthProviderKind.Internal:
                // Internal no debería persistirse, pero por seguridad lo
                // ignoramos si aparece.
                return null;
            case AuthProviderKind.Csv:
            case AuthProviderKind.Http:
                // PR 3 y PR 4 los activarán.
                return null;
            default:
                return null;
        }
    }
}

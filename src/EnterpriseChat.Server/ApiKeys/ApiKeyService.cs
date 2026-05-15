using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using EnterpriseChat.Server.Data;
using EnterpriseChat.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseChat.Server.ApiKeys;

/// <summary>
/// Lógica de emisión, rotación, revocación y resolución de claves de API.
/// El plaintext del token solo existe en memoria durante la llamada que lo
/// crea/rota y se devuelve al cliente UNA SOLA VEZ — la BD solo guarda
/// <see cref="ApiKey.KeyHash"/> (SHA-256 hex) para el lookup del middleware.
///
/// Singleton: el throttle in-memory de <see cref="ApiKey.LastUsedAt"/>
/// (<see cref="_lastTouched"/>) tiene que sobrevivir entre requests para
/// evitar escrituras a BD en cada petición de un bot que hace 60 RPS.
/// </summary>
public sealed class ApiKeyService
{
    /// <summary>
    /// Mínimo intervalo entre dos actualizaciones de <c>LastUsedAt</c> en BD
    /// para la misma clave. Métrica observacional, no auditoría: si el bot
    /// hace 1k RPS solo escribimos una vez por minuto.
    /// </summary>
    private static readonly TimeSpan LastUsedThrottle = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Longitud (en chars) del <see cref="ApiKey.Prefix"/> mostrable.
    /// 11 = <c>ec_pat_</c> (7) + 4 chars del secreto. Suficiente para
    /// identificar la clave en logs/UI sin filtrar el resto.
    /// </summary>
    private const int PrefixDisplayLength = 11;

    private readonly IDbContextFactory<ChatDbContext> _factory;
    private readonly TimeProvider _time;
    private readonly ILogger<ApiKeyService> _logger;

    private readonly ConcurrentDictionary<int, DateTimeOffset> _lastTouched = new();

    public ApiKeyService(
        IDbContextFactory<ChatDbContext> factory,
        TimeProvider time,
        ILogger<ApiKeyService> logger)
    {
        _factory = factory;
        _time = time;
        _logger = logger;
    }

    /// <summary>
    /// Crea una clave nueva. El plaintext solo se devuelve aquí; tras
    /// salir del método ya no es recuperable.
    /// </summary>
    public async Task<IssuedApiKey> IssueAsync(
        string displayName,
        UserRole role,
        int? createdByUserId,
        DateTimeOffset? expiresAt = null,
        string? notes = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        var trimmed = displayName.Trim();
        if (trimmed.Length > 80)
        {
            throw new ArgumentException("displayName supera los 80 caracteres.", nameof(displayName));
        }

        var (plaintext, prefix, hash) = GenerateToken();

        await using var db = await _factory.CreateDbContextAsync(ct);
        var record = new ApiKey
        {
            DisplayName = trimmed,
            Prefix = prefix,
            KeyHash = hash,
            Role = role,
            CreatedAt = _time.GetUtcNow(),
            CreatedByUserId = createdByUserId,
            ExpiresAt = expiresAt,
            Notes = notes
        };
        db.ApiKeys.Add(record);
        db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = createdByUserId,
            Action = "apikey.create",
            Target = $"apikey:{prefix}",
            Timestamp = _time.GetUtcNow(),
            Details = $"role={role}; expires={(expiresAt?.ToString("o") ?? "never")}"
        });
        await db.SaveChangesAsync(ct);

        _logger.LogInformation("API key emitida {Prefix} ({Role}) por usuario {ActorId}.",
            prefix, role, createdByUserId);
        return new IssuedApiKey(record, plaintext);
    }

    /// <summary>
    /// Rota una clave activa: marca la antigua como revocada (con periodo
    /// de gracia opcional) y emite una nueva con el mismo nombre/role/notes,
    /// enlazada vía <see cref="ApiKey.RotatedFromId"/>.
    /// </summary>
    public async Task<IssuedApiKey> RotateAsync(
        int id,
        int? actorUserId,
        int graceSeconds = 0,
        CancellationToken ct = default)
    {
        if (graceSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(graceSeconds), "Grace no puede ser negativo.");
        }

        await using var db = await _factory.CreateDbContextAsync(ct);
        var old = await db.ApiKeys.FirstOrDefaultAsync(k => k.Id == id, ct)
            ?? throw new InvalidOperationException($"API key {id} no encontrada.");
        if (old.RevokedAt is not null)
        {
            throw new InvalidOperationException("La clave ya estaba revocada; rota una clave activa o crea una nueva.");
        }

        var (plaintext, prefix, hash) = GenerateToken();
        var now = _time.GetUtcNow();
        var fresh = new ApiKey
        {
            DisplayName = old.DisplayName,
            Prefix = prefix,
            KeyHash = hash,
            Role = old.Role,
            CreatedAt = now,
            CreatedByUserId = actorUserId,
            ExpiresAt = old.ExpiresAt,
            Notes = old.Notes,
            RotatedFromId = old.Id
        };
        db.ApiKeys.Add(fresh);

        old.RevokedAt = now.AddSeconds(graceSeconds);
        old.RevokedByUserId = actorUserId;
        old.RevokeReason = "rotated";

        db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = actorUserId,
            Action = "apikey.rotate",
            Target = $"apikey:{old.Prefix}",
            Timestamp = now,
            Details = $"new={prefix}; grace={graceSeconds}s"
        });
        await db.SaveChangesAsync(ct);

        _logger.LogInformation("API key {OldPrefix} rotada a {NewPrefix} por usuario {ActorId}.",
            old.Prefix, prefix, actorUserId);
        return new IssuedApiKey(fresh, plaintext);
    }

    /// <summary>Marca la clave como revocada. Idempotente: si ya estaba revocada, devuelve <c>false</c>.</summary>
    public async Task<bool> RevokeAsync(
        int id,
        int? actorUserId,
        string? reason = null,
        CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var key = await db.ApiKeys.FirstOrDefaultAsync(k => k.Id == id, ct);
        if (key is null || key.RevokedAt is not null)
        {
            return false;
        }

        var now = _time.GetUtcNow();
        key.RevokedAt = now;
        key.RevokedByUserId = actorUserId;
        key.RevokeReason = reason;

        db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = actorUserId,
            Action = "apikey.revoke",
            Target = $"apikey:{key.Prefix}",
            Timestamp = now,
            Details = reason ?? "(sin motivo)"
        });
        await db.SaveChangesAsync(ct);

        _logger.LogInformation("API key {Prefix} revocada por usuario {ActorId} ({Reason}).",
            key.Prefix, actorUserId, reason ?? "sin motivo");
        return true;
    }

    /// <summary>Lista para el panel admin. Por defecto oculta las revocadas.</summary>
    public async Task<IReadOnlyList<ApiKey>> ListAsync(
        bool includeRevoked = false,
        CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        IQueryable<ApiKey> query = db.ApiKeys.AsNoTracking();
        if (!includeRevoked)
        {
            query = query.Where(k => k.RevokedAt == null);
        }
        return await query.OrderByDescending(k => k.CreatedAt).ToListAsync(ct);
    }

    /// <summary>
    /// Lookup del middleware: hashea el plaintext, busca por
    /// <see cref="ApiKey.KeyHash"/>, valida revocación/caducidad y actualiza
    /// <c>LastUsedAt</c> de forma throttled. Devuelve <c>null</c> si el
    /// token no existe o no es válido.
    /// </summary>
    public async Task<ApiKey?> ResolveAsync(
        string presentedToken,
        string? requestIp = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(presentedToken)
            || !presentedToken.StartsWith(ApiKeyAuthenticationDefaults.TokenPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var hash = HashToken(presentedToken);
        var now = _time.GetUtcNow();

        await using var db = await _factory.CreateDbContextAsync(ct);
        var key = await db.ApiKeys.FirstOrDefaultAsync(k => k.KeyHash == hash, ct);
        if (key is null)
        {
            return null;
        }
        if (key.RevokedAt is not null && key.RevokedAt <= now)
        {
            return null;
        }
        if (key.ExpiresAt is not null && key.ExpiresAt <= now)
        {
            return null;
        }

        // Throttle: solo escribimos a BD si la última actualización (in-memory)
        // es más antigua que LastUsedThrottle. Tras un reinicio del proceso
        // el dictionary vacío garantiza una escritura por clave activa.
        var lastTouched = _lastTouched.GetValueOrDefault(key.Id, DateTimeOffset.MinValue);
        if (now - lastTouched >= LastUsedThrottle)
        {
            key.LastUsedAt = now;
            key.LastUsedIp = string.IsNullOrEmpty(requestIp) ? null : requestIp;
            await db.SaveChangesAsync(ct);
            _lastTouched[key.Id] = now;
        }

        return key;
    }

    /// <summary>SHA-256 hex (64 chars, lowercase) del plaintext. Determinista — el middleware reusa el método.</summary>
    public static string HashToken(string plaintext)
    {
        Span<byte> hash = stackalloc byte[32];
        var bytes = Encoding.UTF8.GetBytes(plaintext);
        SHA256.HashData(bytes, hash);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Genera un token nuevo: 32 bytes RNG → Base64Url sin padding → 43
    /// caracteres URL-safe. El plaintext entero es <c>ec_pat_&lt;base64url&gt;</c>.
    /// </summary>
    private static (string Plaintext, string Prefix, string Hash) GenerateToken()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        var secret = Base64UrlEncode(bytes);
        var plaintext = ApiKeyAuthenticationDefaults.TokenPrefix + secret;
        var prefix = plaintext[..Math.Min(PrefixDisplayLength, plaintext.Length)];
        return (plaintext, prefix, HashToken(plaintext));
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> bytes)
    {
        // Convert.ToBase64String emite Base64 estándar con padding '=';
        // sustituimos '+' '/' por '-' '_' y quitamos el padding para que
        // el token sea URL-safe (se usa también en query string ?api_key=).
        var b64 = Convert.ToBase64String(bytes);
        return b64.TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}

/// <summary>Resultado de <see cref="ApiKeyService.IssueAsync"/> y <see cref="ApiKeyService.RotateAsync"/>.</summary>
/// <param name="Record">Fila persistida (sin el plaintext).</param>
/// <param name="Plaintext">Token completo; mostrar al usuario una sola vez y descartar.</param>
public sealed record IssuedApiKey(ApiKey Record, string Plaintext);

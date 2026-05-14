using EnterpriseChat.Licensing.Abstractions;

namespace EnterpriseChat.Server.Licensing;

/// <summary>
/// Process-wide singleton holding the currently active license info plus
/// heartbeat bookkeeping (last successful activation time + heartbeat
/// cadence). Mutated by <see cref="RemoteLicenseAdministrator"/> and
/// <see cref="LicenseHeartbeatService"/>; read by
/// <see cref="RemoteLicenseValidator"/>.
/// </summary>
public sealed class RemoteLicenseState
{
    // Free anónima: el servidor recién instalado, sin clave, queda capado
    // a 5 usuarios concurrentes. Para subir a 10 el cliente debe registrarse
    // en enterprisechat.es, verificar email y pegar el serial Free emitido
    // (que en el JWT viene con edition='free' y max_users=10).
    public const int DefaultFreeCap = 5;
    public static readonly LicenseInfo DefaultFree = new(
        Edition: LicenseEdition.Free,
        MaxConcurrentUsers: DefaultFreeCap,
        ExpiresAt: null,
        LicensedTo: null,
        LicenseId: null);

    /// <summary>How long a Pro state survives without a successful heartbeat
    /// before the validator falls back to Free.</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromHours(24);

    private readonly object _gate = new();
    private LicenseInfo _current = DefaultFree;
    private DateTimeOffset? _lastActivationAt;
    private string? _lastActivationError;
    private TimeSpan _heartbeatInterval = TimeSpan.FromMinutes(30);
    private string? _serial;

    public LicenseInfo Current
    {
        get
        {
            lock (_gate)
            {
                if (_current.Edition == LicenseEdition.Pro
                    && _lastActivationAt is { } when
                    && DateTimeOffset.UtcNow - when > StaleAfter)
                {
                    return DefaultFree;
                }
                return _current;
            }
        }
    }

    public DateTimeOffset? LastActivationAt
    {
        get { lock (_gate) { return _lastActivationAt; } }
    }

    public string? LastActivationError
    {
        get { lock (_gate) { return _lastActivationError; } }
    }

    public TimeSpan HeartbeatInterval
    {
        get { lock (_gate) { return _heartbeatInterval; } }
    }

    /// <summary>The opaque serial last accepted, kept so the heartbeat
    /// service knows what to re-activate. <c>null</c> means Free mode.</summary>
    public string? Serial
    {
        get { lock (_gate) { return _serial; } }
    }

    public void ApplySuccess(LicenseInfo info, string serial, TimeSpan heartbeatInterval)
    {
        lock (_gate)
        {
            _current = info;
            _serial = serial;
            _lastActivationAt = DateTimeOffset.UtcNow;
            _lastActivationError = null;
            _heartbeatInterval = heartbeatInterval > TimeSpan.Zero
                ? heartbeatInterval
                : TimeSpan.FromMinutes(30);
        }
    }

    public void RecordFailure(string error)
    {
        lock (_gate)
        {
            _lastActivationError = error;
            // Do NOT touch _current here — it stays Pro for `StaleAfter` so a
            // transient network blip does not knock everyone off the server.
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _current = DefaultFree;
            _serial = null;
            _lastActivationAt = null;
            _lastActivationError = null;
            _heartbeatInterval = TimeSpan.FromMinutes(30);
        }
    }
}

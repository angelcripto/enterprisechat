using EnterpriseChat.Licensing.Abstractions;

namespace EnterpriseChat.Server.Licensing;

/// <summary>
/// In-memory counter that combines per-user connection count with the active
/// <see cref="ILicenseValidator"/> to gate new admissions. The licence cap
/// applies to <b>distinct users with at least one active connection</b>, not
/// to raw connection count — a user opening a second window should not
/// consume a second licence slot.
/// </summary>
public sealed class ConcurrentSessionCounter(ILicenseValidator validator)
{
    private readonly Dictionary<int, int> _connectionsPerUser = [];
    private readonly object _gate = new();

    public SessionAdmission TryAdmit(int userId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(userId);

        lock (_gate)
        {
            if (_connectionsPerUser.TryGetValue(userId, out var existing) && existing > 0)
            {
                _connectionsPerUser[userId] = existing + 1;
                return new SessionAdmission(Admitted: true, DeniedReason: null, IsFirstConnection: false);
            }

            var distinctActive = _connectionsPerUser.Count(kv => kv.Value > 0);
            var verdict = validator.TryAdmitSession(distinctActive);
            if (verdict.Admitted)
            {
                _connectionsPerUser[userId] = 1;
                return new SessionAdmission(Admitted: true, DeniedReason: null, IsFirstConnection: true);
            }
            return new SessionAdmission(Admitted: false, DeniedReason: verdict.DeniedReason, IsFirstConnection: false);
        }
    }

    public SessionRelease Release(int userId)
    {
        lock (_gate)
        {
            if (!_connectionsPerUser.TryGetValue(userId, out var existing))
            {
                return new SessionRelease(WasLastConnection: false);
            }

            if (existing <= 1)
            {
                _connectionsPerUser.Remove(userId);
                return new SessionRelease(WasLastConnection: true);
            }

            _connectionsPerUser[userId] = existing - 1;
            return new SessionRelease(WasLastConnection: false);
        }
    }

    public int DistinctActiveUsers
    {
        get
        {
            lock (_gate)
            {
                return _connectionsPerUser.Count(kv => kv.Value > 0);
            }
        }
    }

    public bool IsOnline(int userId)
    {
        lock (_gate)
        {
            return _connectionsPerUser.TryGetValue(userId, out var n) && n > 0;
        }
    }

    public IReadOnlyCollection<int> Snapshot()
    {
        lock (_gate)
        {
            return _connectionsPerUser.Where(kv => kv.Value > 0).Select(kv => kv.Key).ToArray();
        }
    }
}

public sealed record SessionAdmission(bool Admitted, string? DeniedReason, bool IsFirstConnection);
public sealed record SessionRelease(bool WasLastConnection);

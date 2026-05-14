using EnterpriseChat.Licensing.Abstractions;

namespace EnterpriseChat.Server.Licensing;

/// <summary>
/// Admin facade that delegates the actual validation to the licensing backend
/// over HTTP. The .NET server never validates locally — it just submits the
/// opaque serial + its identity and trusts the backend's verdict.
/// </summary>
public sealed class RemoteLicenseAdministrator(
    LicenseActivationClient client,
    RemoteLicenseState state,
    ILogger<RemoteLicenseAdministrator> log) : ILicenseAdministrator
{
    public async Task<ApplyLicenseResult> ApplyAsync(string serial, CancellationToken ct = default)
    {
        var response = await client.ActivateAsync(serial, ct);
        if (response is null || !response.Success)
        {
            var error = response?.Error ?? "Sin respuesta del servidor de licencias.";
            state.RecordFailure(error);
            log.LogWarning("Activación fallida: {Error}", error);
            return new ApplyLicenseResult(false, error, null);
        }

        // El backend devuelve "edition" como string ("free"/"pro"). Anteriormente
        // forzábamos Pro siempre: cualquier activación con éxito quedaba como
        // Pro aunque la clave fuese Free. Mapeamos al enum LicenseEdition para
        // que el cap, mensajes y UI distingan correctamente las dos tiers.
        var edition = string.Equals(response.Edition, "free", StringComparison.OrdinalIgnoreCase)
            ? LicenseEdition.Free
            : LicenseEdition.Pro;

        var info = new LicenseInfo(
            Edition: edition,
            MaxConcurrentUsers: response.MaxUsers,
            // The wire JWT is short-TTL (1h). The DISPLAYED expiry comes from
            // there until we widen the activation contract to also return the
            // license's master expires_at. Good enough until then.
            ExpiresAt: DateTimeOffset.UtcNow.AddSeconds(response.HeartbeatSeconds * 2),
            LicensedTo: response.LicensedTo,
            LicenseId: response.Jti);

        state.ApplySuccess(info, serial, TimeSpan.FromSeconds(response.HeartbeatSeconds));
        log.LogInformation(
            "Licencia activada para {LicensedTo} ({MaxUsers} usuarios). Heartbeat cada {Seconds}s.",
            response.LicensedTo, response.MaxUsers, response.HeartbeatSeconds);

        return new ApplyLicenseResult(true, null, info);
    }

    public Task ClearAsync(CancellationToken ct = default)
    {
        state.Clear();
        log.LogInformation("Licencia limpiada — servidor vuelve a Free.");
        return Task.CompletedTask;
    }

    public Task RestoreFromStorageAsync(CancellationToken ct = default)
    {
        // The startup restorer reads the active LicenseRecord from the DB and
        // calls ApplyAsync with its opaque serial, so this hook is a no-op.
        return Task.CompletedTask;
    }
}

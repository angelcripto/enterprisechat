namespace EnterpriseChat.Licensing.Abstractions;

/// <summary>
/// Stub used when no closed-source Pro plugin is loaded. Always rejects new
/// serials with a clear "Pro plugin not installed" message, never persists
/// anything, and reports the Free validator's state on restore.
/// </summary>
public sealed class FreeLicenseAdministrator : ILicenseAdministrator
{
    public Task<ApplyLicenseResult> ApplyAsync(string serial, CancellationToken ct = default)
    {
        return Task.FromResult(new ApplyLicenseResult(
            Success: false,
            ErrorMessage:
                "Este servidor está compilado en edición Free y no incluye el módulo de licencias Pro. " +
                "Descarga el instalador comercial desde nuestra web para habilitar la activación de licencias.",
            Info: null));
    }

    public Task ClearAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task RestoreFromStorageAsync(CancellationToken ct = default) => Task.CompletedTask;
}

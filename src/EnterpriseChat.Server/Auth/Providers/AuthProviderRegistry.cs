namespace EnterpriseChat.Server.Auth.Providers;

/// <summary>
/// Punto único de acceso a los providers configurados en runtime. PR 1
/// solo registra <see cref="InternalAuthProvider"/>; los siguientes PRs
/// añadirán MySQL / CSV / HTTP cargando su configuración desde
/// <c>AuthProviderConfig</c> en la BD.
///
/// La enumeración respeta el orden de prioridad que el admin defina:
/// el endpoint de login para en el primer <see cref="AuthOutcome.Success"/>.
/// </summary>
public sealed class AuthProviderRegistry
{
    private readonly IReadOnlyList<IAuthProvider> _providers;

    public AuthProviderRegistry(IEnumerable<IAuthProvider> providers)
    {
        // Orden estable: Internal primero hasta que tengamos prioridad
        // configurable. Mantener Internal arriba garantiza que el admin
        // y el reset de emergencia siempre funcionen aunque otros
        // providers estén mal configurados.
        _providers = providers
            .OrderBy(p => p.Kind == AuthProviderKind.Internal ? 0 : 1)
            .ThenBy(p => p.ProviderId)
            .ToList();
    }

    public IReadOnlyList<IAuthProvider> All => _providers;

    public IAuthProvider Internal =>
        _providers.First(p => p.Kind == AuthProviderKind.Internal);
}

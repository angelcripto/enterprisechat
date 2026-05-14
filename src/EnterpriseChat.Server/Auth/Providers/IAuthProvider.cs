namespace EnterpriseChat.Server.Auth.Providers;

/// <summary>
/// Backend de autenticación. Cada implementación encapsula un origen
/// (SQLite interno, MySQL externo, CSV importado, webhook HTTP). El
/// endpoint de login itera los providers habilitados en orden de
/// prioridad y se queda con el primer <see cref="AuthOutcome.Success"/>.
///
/// Diseño:
///   - El provider devuelve <see cref="AuthOutcome.UnknownUser"/> cuando
///     no conoce al usuario para permitir fallback al siguiente.
///     <see cref="AuthOutcome.BadPassword"/> también permite seguir
///     probando otros providers porque el mismo username puede existir
///     en varios orígenes; el operador decide la prioridad.
///   - <see cref="AuthOutcome.ProviderError"/> corta la cadena: si MySQL
///     está caído no queremos colapsar a Internal silenciosamente y dar
///     una sensación falsa de aislamiento.
///   - El provider NUNCA escribe en la BD local (creación de usuario,
///     auditoría, etc.). Eso es responsabilidad del endpoint, que tiene
///     la visión completa y maneja la transacción.
/// </summary>
public interface IAuthProvider
{
    /// <summary>Discriminador estático del tipo de provider.</summary>
    AuthProviderKind Kind { get; }

    /// <summary>
    /// Identificador estable del provider configurado (clave primaria
    /// en <c>AuthProviderConfig</c>). Para el provider interno es 0.
    /// </summary>
    int ProviderId { get; }

    /// <summary>Nombre humano definido por el admin para la UI / logs.</summary>
    string DisplayName { get; }

    /// <summary>Verifica las credenciales contra este provider.</summary>
    Task<AuthResult> VerifyAsync(string username, string password, CancellationToken ct);
}

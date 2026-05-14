namespace EnterpriseChat.Server.Auth.Providers;

/// <summary>
/// Resultado de la verificación contra un <see cref="IAuthProvider"/>:
///   - <see cref="Outcome"/> distingue éxito de tres tipos de fallo
///     (usuario desconocido / contraseña incorrecta / proveedor caído)
///     para que el endpoint de login decida si seguir probando con el
///     siguiente provider o cortar la cadena.
///   - <see cref="ExternalId"/> es el identificador estable del usuario
///     en el sistema externo. Si más adelante el username cambia, el
///     <c>ExternalId</c> permite mantener la sesión / mensajes ligados
///     al mismo registro local.
///   - <see cref="FullName"/> y <see cref="Email"/> son opcionales: los
///     providers que los devuelvan permiten autoaprovisionar / actualizar
///     metadatos del usuario local.
///   - <see cref="NeedsRehash"/> solo aplica al provider Internal: indica
///     que el hash BCrypt almacenado debe rotarse al cost factor actual.
/// </summary>
public sealed record AuthResult(
    AuthOutcome Outcome,
    string? ExternalId = null,
    string? FullName = null,
    string? Email = null,
    bool NeedsRehash = false,
    string? FailureDetail = null)
{
    public bool Succeeded => Outcome == AuthOutcome.Success;

    public static AuthResult Success(string? externalId = null, string? fullName = null, string? email = null, bool needsRehash = false)
        => new(AuthOutcome.Success, externalId, fullName, email, needsRehash);

    public static AuthResult UnknownUser(string? detail = null)
        => new(AuthOutcome.UnknownUser, FailureDetail: detail);

    public static AuthResult BadPassword(string? detail = null)
        => new(AuthOutcome.BadPassword, FailureDetail: detail);

    public static AuthResult ProviderError(string? detail = null)
        => new(AuthOutcome.ProviderError, FailureDetail: detail);
}

public enum AuthOutcome
{
    Success = 0,
    /// <summary>El provider respondió pero no conoce al usuario.</summary>
    UnknownUser = 1,
    /// <summary>El provider conoce al usuario pero la contraseña no encaja.</summary>
    BadPassword = 2,
    /// <summary>El provider falló (conexión, hash corrupto, etc.). No se debe consultar el siguiente.</summary>
    ProviderError = 3,
}

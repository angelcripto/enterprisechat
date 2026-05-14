namespace EnterpriseChat.Server.Auth.Providers;

/// <summary>
/// Discriminador del backend de autenticación. Internal es el SQLite
/// propio del server (donde vive siempre el admin); el resto son
/// orígenes externos opt-in que el admin configura desde la UI.
/// </summary>
public enum AuthProviderKind
{
    Internal = 0,
    Mysql    = 1,
    Csv      = 2,
    Http     = 3,
}

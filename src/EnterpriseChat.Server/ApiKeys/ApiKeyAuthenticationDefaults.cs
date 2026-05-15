namespace EnterpriseChat.Server.ApiKeys;

/// <summary>
/// Constantes del scheme de autenticación por API key. Se declaran aparte
/// para que las pueda referenciar tanto el handler como el wiring del
/// pipeline sin imports circulares.
/// </summary>
public static class ApiKeyAuthenticationDefaults
{
    /// <summary>Nombre del scheme registrado en ASP.NET Core.</summary>
    public const string Scheme = "ApiKey";

    /// <summary>
    /// Scheme "router" que ASP.NET Core usa por defecto: mira el header y
    /// reenvía a <see cref="Scheme"/> si llega un PAT o a JwtBearer en caso
    /// contrario. Evita tener que enumerar ambos schemes en cada policy y
    /// elude el bug en el que un <c>AuthorizationPolicy</c> con dos
    /// <c>AuthenticationSchemes</c> y <c>RequireRole</c> rechazaba JWTs
    /// válidos cuando el segundo scheme devolvía <c>NoResult</c>.
    /// </summary>
    public const string ForwardingScheme = "JwtOrApiKey";

    /// <summary>
    /// Prefijo literal de todos los tokens emitidos. El middleware lo usa
    /// como discriminador frente a un JWT (ningún JWT empieza por aquí).
    /// </summary>
    public const string TokenPrefix = "ec_pat_";

    /// <summary>
    /// Policy de autorización que acepta JWT humano o PAT con rol Admin.
    /// Reemplaza al inline <c>p =&gt; p.RequireRole("Admin")</c> en los
    /// endpoints admin para que las dos vías de auth funcionen.
    /// </summary>
    public const string AdminPolicy = "AdminOnly";

    /// <summary>Claim custom que marca un Principal autenticado por PAT.</summary>
    public const string KeyTypeClaim = "key_type";

    /// <summary>Valor de <see cref="KeyTypeClaim"/> para PATs.</summary>
    public const string KeyTypeValue = "apikey";
}

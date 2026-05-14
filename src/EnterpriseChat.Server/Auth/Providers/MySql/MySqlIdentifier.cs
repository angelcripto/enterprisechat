using System.Text.RegularExpressions;

namespace EnterpriseChat.Server.Auth.Providers.MySql;

/// <summary>
/// MySQL no acepta tabla/columna parametrizada en un prepared statement;
/// hay que componerlo en el SQL. Para evitar inyección, validamos contra
/// una whitelist estricta y comillamos con backticks duplicados.
/// </summary>
internal static class MySqlIdentifier
{
    // [a-zA-Z_][a-zA-Z0-9_$]{0,63} cubre nombres no entrecomillados de
    // MySQL 8 (RFC: 64 chars). Si el cliente usa caracteres exóticos
    // (espacios, acentos, reserved keywords), tendrá que renombrar — es
    // peor el riesgo de inyección que perder cobertura del 0.1% de casos.
    private static readonly Regex Allowed = new(@"^[A-Za-z_][A-Za-z0-9_$]{0,63}$", RegexOptions.Compiled);

    public static string Quote(string identifier)
    {
        if (string.IsNullOrEmpty(identifier) || !Allowed.IsMatch(identifier))
        {
            throw new ArgumentException(
                $"Identificador MySQL no admitido: '{identifier}'. Usa solo letras, dígitos y guión bajo (máx. 64).",
                nameof(identifier));
        }
        return $"`{identifier}`";
    }

    /// <summary>
    /// Saneo del WHERE adicional (string libre). No es seguro al 100%
    /// pero rechaza ; -- /* */ y palabras claves peligrosas. Lo único
    /// que rendimos es servir como red de protección — el admin debe
    /// asumir que esto se concatena, y si compromete la cadena
    /// compromete su propia BD.
    /// </summary>
    public static void ValidateExtraWhere(string extraWhere)
    {
        ArgumentNullException.ThrowIfNull(extraWhere);
        var lower = " " + extraWhere.ToLowerInvariant() + " ";
        string[] blocked = { ";", "--", "/*", "*/", " union ", " select ", " insert ", " update ", " delete ", " drop ", " alter ", " grant ", " revoke ", "(select " };
        foreach (var token in blocked)
        {
            if (lower.Contains(token, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"El filtro adicional contiene un token prohibido ({token.Trim()}).",
                    nameof(extraWhere));
            }
        }
    }
}

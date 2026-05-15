namespace EnterpriseChat.Protocol.ApiKeys;

/// <summary>
/// Petición de creación de una API key (PAT). El plaintext del token
/// generado se devuelve UNA SOLA VEZ en <see cref="IssuedApiKeyResponse"/>.
/// </summary>
/// <param name="DisplayName">Nombre humano de la clave (≤80 chars).</param>
/// <param name="Role">"User" o "Admin". Cualquier otro valor se rechaza con 400.</param>
/// <param name="ExpiresInDays">Días desde ahora hasta que caduca; null = no caduca.</param>
/// <param name="Notes">Notas libres del admin (≤500 chars). No se expone públicamente.</param>
public sealed record CreateApiKeyRequest(
    string DisplayName,
    string Role,
    int? ExpiresInDays,
    string? Notes);

/// <param name="GraceSeconds">
/// Segundos durante los que la clave anterior sigue siendo válida tras
/// la rotación. 0 = revocación inmediata. Útil para ventanas de despliegue
/// donde el bot necesita unos segundos para reemplazar el secreto.
/// </param>
public sealed record RotateApiKeyRequest(int? GraceSeconds);

/// <param name="Reason">Texto libre (≤200 chars) que queda en el audit log.</param>
public sealed record RevokeApiKeyRequest(string? Reason);

/// <summary>
/// Resumen público de una API key. <b>No incluye nunca el plaintext ni el hash</b>:
/// solo <see cref="Prefix"/> (los primeros 11 chars del token, suficiente para
/// identificarla en logs y UI).
/// </summary>
public sealed record ApiKeySummary(
    int Id,
    string DisplayName,
    string Prefix,
    string Role,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? LastUsedAt,
    string? LastUsedIp,
    DateTimeOffset? RevokedAt,
    string? RevokeReason,
    string? Notes,
    int? RotatedFromId,
    int? CreatedByUserId);

/// <summary>
/// Respuesta exclusiva de <c>POST /admin/api-keys</c> y
/// <c>POST /admin/api-keys/{id}/rotate</c>: incluye el plaintext del token
/// recién emitido. <b>El servidor no lo conserva</b>; si el cliente lo
/// pierde, hay que rotar la clave.
/// </summary>
public sealed record IssuedApiKeyResponse(
    string Plaintext,
    ApiKeySummary Key);

/// <summary>Listado para el panel admin.</summary>
public sealed record ApiKeyListResult(IReadOnlyList<ApiKeySummary> Rows);

using System.Globalization;
using System.Security.Claims;
using EnterpriseChat.Protocol.ApiKeys;
using EnterpriseChat.Server.Data.Entities;

namespace EnterpriseChat.Server.ApiKeys;

/// <summary>
/// CRUD REST de API keys bajo <c>/admin/api-keys</c>. Solo accesible con
/// rol Admin (policy <see cref="ApiKeyAuthenticationDefaults.AdminPolicy"/>),
/// y por tanto desde JWT humano o desde una PAT con rol Admin.
///
/// Convención: los endpoints que GENERAN un secreto nuevo (Create, Rotate)
/// devuelven el plaintext en <see cref="IssuedApiKeyResponse"/>. El resto
/// solo proyecta <see cref="ApiKeySummary"/>.
/// </summary>
internal static class ApiKeyAdminEndpoints
{
    public static void MapApiKeyAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/admin/api-keys")
            .WithTags("AdminApiKeys")
            .RequireAuthorization(ApiKeyAuthenticationDefaults.AdminPolicy);

        group.MapPost("/", CreateAsync);
        group.MapGet("/", ListAsync);
        group.MapGet("/{id:int}", GetAsync);
        group.MapPost("/{id:int}/rotate", RotateAsync);
        group.MapPost("/{id:int}/revoke", RevokeAsync);
        // DELETE es alias de revoke sin body. El recurso no se borra físicamente
        // (audit-friendly): solo se marca con RevokedAt = NOW.
        group.MapDelete("/{id:int}", DeleteAsync);
    }

    private static async Task<IResult> CreateAsync(
        CreateApiKeyRequest request,
        ApiKeyService service,
        ClaimsPrincipal principal,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.DisplayName))
        {
            return Problem("El campo displayName es obligatorio.");
        }
        if (request.DisplayName.Length > 80)
        {
            return Problem("displayName excede 80 caracteres.");
        }
        if (!TryParseRole(request.Role, out var role))
        {
            return Problem("role debe ser 'User' o 'Admin'.");
        }
        if (request.ExpiresInDays is < 0)
        {
            return Problem("expiresInDays no puede ser negativo.");
        }
        if (request.Notes is { Length: > 500 })
        {
            return Problem("notes excede 500 caracteres.");
        }

        var expiresAt = request.ExpiresInDays is { } days
            ? DateTimeOffset.UtcNow.AddDays(days)
            : (DateTimeOffset?)null;

        var issued = await service.IssueAsync(
            request.DisplayName,
            role,
            createdByUserId: TryGetActorId(principal),
            expiresAt: expiresAt,
            notes: request.Notes,
            ct: ct);

        return Results.Created(
            $"/admin/api-keys/{issued.Record.Id}",
            new IssuedApiKeyResponse(issued.Plaintext, ToSummary(issued.Record)));
    }

    private static async Task<IResult> ListAsync(
        ApiKeyService service,
        bool? includeRevoked,
        CancellationToken ct)
    {
        var rows = await service.ListAsync(includeRevoked == true, ct);
        return Results.Ok(new ApiKeyListResult(rows.Select(ToSummary).ToList()));
    }

    private static async Task<IResult> GetAsync(
        int id,
        ApiKeyService service,
        CancellationToken ct)
    {
        // Reusamos ListAsync(includeRevoked=true) en lugar de exponer un Get
        // individual en el servicio — es una sola fila + filter, irrelevante
        // a estas alturas y mantiene el servicio mínimo.
        var rows = await service.ListAsync(includeRevoked: true, ct);
        var row = rows.FirstOrDefault(k => k.Id == id);
        return row is null
            ? Results.NotFound()
            : Results.Ok(ToSummary(row));
    }

    private static async Task<IResult> RotateAsync(
        int id,
        RotateApiKeyRequest? request,
        ApiKeyService service,
        ClaimsPrincipal principal,
        CancellationToken ct)
    {
        var grace = request?.GraceSeconds ?? 0;
        if (grace < 0)
        {
            return Problem("graceSeconds no puede ser negativo.");
        }

        try
        {
            var issued = await service.RotateAsync(id, TryGetActorId(principal), grace, ct);
            return Results.Ok(new IssuedApiKeyResponse(issued.Plaintext, ToSummary(issued.Record)));
        }
        catch (InvalidOperationException ex)
        {
            // Cubre dos casos del servicio: id no existe → 404; ya revocada → 409.
            // El mensaje del servicio es lo bastante específico para distinguirlos.
            var notFound = ex.Message.Contains("no encontrada", StringComparison.OrdinalIgnoreCase);
            return Results.Json(
                new { error = ex.Message },
                statusCode: notFound ? StatusCodes.Status404NotFound : StatusCodes.Status409Conflict);
        }
    }

    private static async Task<IResult> RevokeAsync(
        int id,
        RevokeApiKeyRequest? request,
        ApiKeyService service,
        ClaimsPrincipal principal,
        CancellationToken ct)
    {
        if (request?.Reason is { Length: > 200 })
        {
            return Problem("reason excede 200 caracteres.");
        }
        var revoked = await service.RevokeAsync(id, TryGetActorId(principal), request?.Reason, ct);
        return revoked ? Results.NoContent() : Results.NotFound();
    }

    private static async Task<IResult> DeleteAsync(
        int id,
        ApiKeyService service,
        ClaimsPrincipal principal,
        CancellationToken ct)
    {
        var revoked = await service.RevokeAsync(id, TryGetActorId(principal), reason: "deleted", ct);
        return revoked ? Results.NoContent() : Results.NotFound();
    }

    private static ApiKeySummary ToSummary(ApiKey k) => new(
        Id: k.Id,
        DisplayName: k.DisplayName,
        Prefix: k.Prefix,
        Role: k.Role.ToString(),
        CreatedAt: k.CreatedAt,
        ExpiresAt: k.ExpiresAt,
        LastUsedAt: k.LastUsedAt,
        LastUsedIp: k.LastUsedIp,
        RevokedAt: k.RevokedAt,
        RevokeReason: k.RevokeReason,
        Notes: k.Notes,
        RotatedFromId: k.RotatedFromId,
        CreatedByUserId: k.CreatedByUserId);

    private static bool TryParseRole(string? raw, out UserRole role)
    {
        // Aceptamos solo los dos valores nominales en lugar de Enum.TryParse
        // permisivo, que se traga números enteros o variantes en otro casing.
        if (string.Equals(raw, "User", StringComparison.Ordinal))
        {
            role = UserRole.User;
            return true;
        }
        if (string.Equals(raw, "Admin", StringComparison.Ordinal))
        {
            role = UserRole.Admin;
            return true;
        }
        role = default;
        return false;
    }

    /// <summary>
    /// El <c>sub</c> de un JWT humano es el userId numérico; el de un PAT
    /// es <c>apikey:&lt;id&gt;</c>. Solo extraemos id cuando es int —
    /// el resto queda como <c>null</c> en el audit log, lo que indica
    /// "originado por una PAT".
    /// </summary>
    private static int? TryGetActorId(ClaimsPrincipal principal)
    {
        var raw = principal.FindFirstValue("sub") ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(raw, CultureInfo.InvariantCulture, out var id) ? id : null;
    }

    private static IResult Problem(string error) =>
        Results.Json(new { error }, statusCode: StatusCodes.Status400BadRequest);
}

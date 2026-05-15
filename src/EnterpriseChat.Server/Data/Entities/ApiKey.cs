using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EnterpriseChat.Server.Data.Entities;

/// <summary>
/// Personal Access Token (PAT) que permite a integraciones externas
/// autenticarse contra el servidor sin pasar por el flujo humano de
/// <c>/auth/login</c>. El plaintext se entrega UNA SOLA VEZ al crear
/// o rotar; el servidor solo persiste <see cref="KeyHash"/> (SHA-256
/// hex), que es la columna de lookup del middleware de API keys.
///
/// Son tokens de SERVICIO, no representan a un usuario humano: una
/// petición autenticada por API key entra al pipeline con un
/// <c>ClaimsPrincipal</c> sintético cuyo <c>sub</c> es
/// <c>apikey:&lt;Id&gt;</c>. Los endpoints que dependen de un userId
/// numérico (DMs, /me/inbox, hub SignalR) responden 422 — por diseño.
///
/// Lifecycle: las filas nunca se borran físicamente.
/// <see cref="RevokedAt"/> + <see cref="RevokedByUserId"/> dejan rastro
/// en BD para auditoría. Una rotación crea una fila nueva con
/// <see cref="RotatedFromId"/> apuntando a la anterior y marca la
/// anterior como revocada.
/// </summary>
public sealed class ApiKey
{
    public int Id { get; set; }

    /// <summary>
    /// Nombre humano de la clave ("Bot de turno", "CI build"). Visible
    /// en la lista del panel admin; lo elige quien la crea.
    /// </summary>
    [Required, MaxLength(80)]
    public string DisplayName { get; set; } = null!;

    /// <summary>
    /// Primeros caracteres del secreto en claro (p.ej. <c>ec_pat_AB12</c>).
    /// Se conservan tal cual para identificar la clave en logs y UI sin
    /// exponer el resto. El secreto completo nunca se persiste en claro.
    /// </summary>
    [Required, MaxLength(16)]
    public string Prefix { get; set; } = null!;

    /// <summary>
    /// SHA-256 hex (64 chars) del plaintext completo. El middleware
    /// hashea el token recibido en el header y busca por aquí. Índice
    /// único — las colisiones de SHA-256 son astronómicamente
    /// improbables, pero el constraint nos protege de cualquier bug
    /// futuro en el generador.
    /// </summary>
    [Required, MaxLength(64)]
    public string KeyHash { get; set; } = null!;

    /// <summary>
    /// Rol concedido a esta clave. El middleware lo expone como claim
    /// <c>ClaimTypes.Role</c> para que los policies
    /// <c>RequireRole("Admin")</c> existentes sigan funcionando sin
    /// cambios.
    /// </summary>
    public UserRole Role { get; set; } = UserRole.User;

    /// <summary>
    /// Admin que creó la clave. Nullable porque si el admin se borra
    /// con hard-delete la clave debe seguir viva (igual que
    /// <c>LicenseRecord.AppliedByUserId</c>).
    /// </summary>
    public int? CreatedByUserId { get; set; }

    [ForeignKey(nameof(CreatedByUserId))]
    public User? CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Null = no caduca. Si es pasada, el middleware devuelve 401.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>
    /// Última vez que el middleware aceptó esta clave. Se actualiza con
    /// throttle (1 min por clave) para no martillear la BD; null = nunca
    /// se ha usado. No sustituye al audit log; es para mostrarlo en la UI.
    /// </summary>
    public DateTimeOffset? LastUsedAt { get; set; }

    [MaxLength(45)]
    public string? LastUsedIp { get; set; }

    /// <summary>
    /// Soft-delete. Si <c>!= null</c> la clave está inactiva y todo
    /// intento de uso devuelve 401. Las filas no se borran nunca para
    /// preservar el rastro de quién hizo qué con qué clave.
    /// </summary>
    public DateTimeOffset? RevokedAt { get; set; }

    public int? RevokedByUserId { get; set; }

    [ForeignKey(nameof(RevokedByUserId))]
    public User? RevokedBy { get; set; }

    [MaxLength(200)]
    public string? RevokeReason { get; set; }

    /// <summary>Notas libres del admin (motivo, owner de la integración, …).</summary>
    [MaxLength(500)]
    public string? Notes { get; set; }

    /// <summary>
    /// Si la clave nació de una rotación, apunta a la fila anterior.
    /// Permite trazar la cadena completa para una integración concreta.
    /// </summary>
    public int? RotatedFromId { get; set; }

    [ForeignKey(nameof(RotatedFromId))]
    public ApiKey? RotatedFrom { get; set; }
}

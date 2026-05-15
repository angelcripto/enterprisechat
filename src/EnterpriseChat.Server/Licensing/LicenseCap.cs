using EnterpriseChat.Licensing.Abstractions;
using EnterpriseChat.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseChat.Server.Licensing;

/// <summary>
/// Limita el número de cuentas locales activas según la edición de la
/// licencia. Free = 10 cuentas totales (no concurrentes). Pro = lo que
/// declare el JWT.
///
/// El cap se evalúa en TODOS los puntos donde se crea o reactiva un
/// usuario: alta manual de admin, auto-provisión JIT por proveedor
/// externo, import masivo desde MySQL/CSV, reactivar usuario
/// previamente desactivado.
///
/// Nota histórica: <see cref="LicenseInfo.MaxConcurrentUsers"/> se llamó
/// "concurrentes" en la primera iteración. Se mantiene el nombre del
/// campo en el contrato con el server remoto de licensing, pero la
/// semántica del lado del cliente del chat ahora es <em>cuentas
/// activas totales</em>.
/// </summary>
public static class LicenseCap
{
    public static async Task<int> CountActiveUsersAsync(ChatDbContext db, CancellationToken ct)
        => await db.Users.CountAsync(u => u.IsActive, ct);

    /// <summary>
    /// Comprueba si crear / reactivar <paramref name="extra"/> cuentas
    /// excedería el límite de la edición actual.
    /// </summary>
    public static async Task<LicenseCapResult> CheckCanAddAsync(
        ChatDbContext db,
        ILicenseValidator licensing,
        int extra,
        CancellationToken ct)
    {
        var current = await CountActiveUsersAsync(db, ct);
        var max = licensing.Current.MaxConcurrentUsers;
        var wouldBe = current + extra;
        if (wouldBe > max)
        {
            return new LicenseCapResult(
                Allowed: false,
                CurrentActive: current,
                Max: max,
                Available: Math.Max(0, max - current));
        }
        return new LicenseCapResult(
            Allowed: true,
            CurrentActive: current,
            Max: max,
            Available: Math.Max(0, max - current - extra));
    }
}

/// <param name="Allowed">True si la operación cabe dentro del cap.</param>
/// <param name="CurrentActive">Cuentas activas ahora mismo.</param>
/// <param name="Max">Tope total según la edición.</param>
/// <param name="Available">Slots restantes tras la operación si <paramref name="Allowed"/>; antes de la operación si rechazado.</param>
public sealed record LicenseCapResult(bool Allowed, int CurrentActive, int Max, int Available);

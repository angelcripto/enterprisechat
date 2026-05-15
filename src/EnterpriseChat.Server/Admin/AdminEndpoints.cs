using System.Globalization;
using System.Security.Claims;
using EnterpriseChat.Licensing.Abstractions;
using EnterpriseChat.Protocol.Admin;
using EnterpriseChat.Server.Auth;
using EnterpriseChat.Server.Data;
using EnterpriseChat.Server.Data.Entities;
using EnterpriseChat.Server.Licensing;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseChat.Server.Admin;

internal static class AdminEndpoints
{
    public static void MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        // Every route under /admin requires the Admin role.
        var group = app.MapGroup("/admin")
            .WithTags("Admin")
            .RequireAuthorization(p => p.RequireRole(UserRole.Admin.ToString()));

        group.MapGet("/users", ListUsersAsync);
        group.MapGet("/users/ids", ListUserIdsAsync);
        group.MapPost("/users", CreateUserAsync);
        group.MapPut("/users/{id:int}", UpdateUserAsync);
        group.MapDelete("/users/{id:int}", DeactivateUserAsync);
        group.MapPost("/users/{id:int}/reset-password", ResetPasswordAsync);
        group.MapPost("/users/{id:int}/activate", ActivateUserAsync);
        group.MapDelete("/users/{id:int}/hard", HardDeleteUserAsync);

        group.MapGet("/departments", ListDepartmentsAsync);
        group.MapPost("/departments", CreateDepartmentAsync);
    }

    /// <summary>
    /// Sort columns admitidas en el listado de usuarios. Whitelist
    /// para que el cliente no pueda inyectar SQL en `ORDER BY`.
    /// </summary>
    private static readonly HashSet<string> UserSortColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "username", "fullName", "email", "role", "lastLoginAt", "createdAt", "isActive",
    };

    private const int MaxAllUserIds = 10_000;

    private static IQueryable<Data.Entities.User> BuildUserListingQuery(
        ChatDbContext db, string? search, string? sort, string? dir)
    {
        var query = db.Users
            .Include(u => u.Department)
            .Where(u => u.Username != Bootstrap.AdminSeeder.DeletedUserUsername);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            var like = $"%{s}%";
            query = query.Where(u =>
                EF.Functions.Like(u.Username, like)
                || EF.Functions.Like(u.FullName, like)
                || (u.Email != null && EF.Functions.Like(u.Email!, like)));
        }

        var col = string.IsNullOrWhiteSpace(sort) ? "username" : sort.Trim();
        var ascending = !"desc".Equals(dir, StringComparison.OrdinalIgnoreCase);
        query = (col.ToLowerInvariant(), ascending) switch
        {
            ("username",    true)  => query.OrderBy(u => u.Username),
            ("username",    false) => query.OrderByDescending(u => u.Username),
            ("fullname",    true)  => query.OrderBy(u => u.FullName),
            ("fullname",    false) => query.OrderByDescending(u => u.FullName),
            ("email",       true)  => query.OrderBy(u => u.Email),
            ("email",       false) => query.OrderByDescending(u => u.Email),
            ("role",        true)  => query.OrderBy(u => u.Role),
            ("role",        false) => query.OrderByDescending(u => u.Role),
            ("lastloginat", true)  => query.OrderBy(u => u.LastLoginAt),
            ("lastloginat", false) => query.OrderByDescending(u => u.LastLoginAt),
            ("createdat",   true)  => query.OrderBy(u => u.CreatedAt),
            ("createdat",   false) => query.OrderByDescending(u => u.CreatedAt),
            ("isactive",    true)  => query.OrderBy(u => u.IsActive),
            ("isactive",    false) => query.OrderByDescending(u => u.IsActive),
            _                       => query.OrderBy(u => u.Username),
        };
        return query;
    }

    private static async Task<IResult> ListUsersAsync(
        IDbContextFactory<ChatDbContext> dbFactory,
        string? search,
        int? page,
        int? pageSize,
        string? sort,
        string? dir,
        CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(sort) && !UserSortColumns.Contains(sort))
        {
            return Results.BadRequest(new { error = $"Columna de orden no admitida: {sort}." });
        }

        var p = page ?? 0;
        if (p < 0) p = 0;
        var ps = pageSize ?? 50;
        if (ps <= 0) ps = 50;
        if (ps > 500) ps = 500;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var query = BuildUserListingQuery(db, search, sort, dir);
        var total = await query.CountAsync(ct);
        var users = await query.Skip(p * ps).Take(ps).ToListAsync(ct);

        var rows = users.Select(u => new AdminUserDetail(
            Id: u.Id,
            Username: u.Username,
            FullName: u.FullName,
            Email: u.Email,
            DepartmentId: u.DepartmentId,
            DepartmentName: u.Department?.Name,
            Role: u.Role.ToString(),
            IsActive: u.IsActive,
            CreatedAt: u.CreatedAt,
            LastLoginAt: u.LastLoginAt)).ToList();

        return Results.Ok(new AdminUserListResult(rows, total, p, ps));
    }

    /// <summary>
    /// Devuelve los IDs de los usuarios que matchean el filtro (sin
    /// paginar). Usado por la UI para "seleccionar todos los del
    /// filtro" sin tirar de N páginas. Tope duro a 10.000 — si el
    /// admin necesita más, que afine el search.
    /// </summary>
    private static async Task<IResult> ListUserIdsAsync(
        IDbContextFactory<ChatDbContext> dbFactory,
        string? search,
        string? sort,
        string? dir,
        CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(sort) && !UserSortColumns.Contains(sort))
        {
            return Results.BadRequest(new { error = $"Columna de orden no admitida: {sort}." });
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var query = BuildUserListingQuery(db, search, sort, dir);
        var total = await query.CountAsync(ct);
        if (total > MaxAllUserIds)
        {
            return Results.Json(
                new { error = $"Demasiados resultados ({total}). Refina el filtro: máximo {MaxAllUserIds}." },
                statusCode: StatusCodes.Status413PayloadTooLarge);
        }
        var ids = await query.Select(u => u.Id).ToListAsync(ct);
        return Results.Ok(new AdminUserIdsResult(ids));
    }

    private static async Task<IResult> CreateUserAsync(
        CreateUserRequest request,
        ClaimsPrincipal principal,
        IDbContextFactory<ChatDbContext> dbFactory,
        IPasswordHasher hasher,
        ILicenseValidator licensing,
        CancellationToken ct)
    {
        var validation = ValidateCreate(request);
        if (validation is not null)
        {
            return Results.BadRequest(new { error = validation });
        }

        if (!Enum.TryParse<UserRole>(request.Role, ignoreCase: true, out var role))
        {
            return Results.BadRequest(new { error = "Rol inválido (User | Admin)." });
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        if (await db.Users.AnyAsync(u => u.Username == request.Username, ct))
        {
            return Results.Conflict(new { error = "Ya existe un usuario con ese nombre." });
        }

        var cap = await LicenseCap.CheckCanAddAsync(db, licensing, extra: 1, ct);
        if (!cap.Allowed)
        {
            return Results.Json(new
            {
                error = $"Edición {licensing.Current.Edition}: límite de {cap.Max} cuentas activas alcanzado ({cap.CurrentActive} en uso). Actualiza a Pro o desactiva un usuario.",
                currentActive = cap.CurrentActive,
                max = cap.Max,
            }, statusCode: StatusCodes.Status403Forbidden);
        }

        if (request.DepartmentId is int depId
            && !await db.Departments.AnyAsync(d => d.Id == depId, ct))
        {
            return Results.BadRequest(new { error = "El departamento indicado no existe." });
        }

        var entity = new User
        {
            Username = request.Username.Trim(),
            FullName = request.FullName.Trim(),
            Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim(),
            DepartmentId = request.DepartmentId,
            Role = role,
            IsActive = true,
            PasswordHash = hasher.Hash(request.Password),
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.Users.Add(entity);
        db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = TryGetActorId(principal),
            Action = "admin.user.create",
            Target = entity.Username
        });
        await db.SaveChangesAsync(ct);

        return Results.Created($"/admin/users/{entity.Id}", new AdminUserDetail(
            Id: entity.Id,
            Username: entity.Username,
            FullName: entity.FullName,
            Email: entity.Email,
            DepartmentId: entity.DepartmentId,
            DepartmentName: null,
            Role: entity.Role.ToString(),
            IsActive: entity.IsActive,
            CreatedAt: entity.CreatedAt,
            LastLoginAt: null));
    }

    private static async Task<IResult> UpdateUserAsync(
        int id,
        UpdateUserRequest request,
        ClaimsPrincipal principal,
        IDbContextFactory<ChatDbContext> dbFactory,
        ILicenseValidator licensing,
        CancellationToken ct)
    {
        if (!Enum.TryParse<UserRole>(request.Role, ignoreCase: true, out var role))
        {
            return Results.BadRequest(new { error = "Rol inválido." });
        }
        if (string.IsNullOrWhiteSpace(request.FullName))
        {
            return Results.BadRequest(new { error = "FullName es obligatorio." });
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var user = await db.Users.FindAsync([id], ct);
        if (user is null)
        {
            return Results.NotFound();
        }

        if (request.DepartmentId is int depId
            && !await db.Departments.AnyAsync(d => d.Id == depId, ct))
        {
            return Results.BadRequest(new { error = "El departamento indicado no existe." });
        }

        // Reactivar consume un slot. Si pasa de inactivo a activo,
        // re-evaluamos el cap.
        if (request.IsActive && !user.IsActive)
        {
            var cap = await LicenseCap.CheckCanAddAsync(db, licensing, extra: 1, ct);
            if (!cap.Allowed)
            {
                return Results.Json(new
                {
                    error = $"Edición {licensing.Current.Edition}: límite de {cap.Max} cuentas activas alcanzado ({cap.CurrentActive} en uso).",
                    currentActive = cap.CurrentActive,
                    max = cap.Max,
                }, statusCode: StatusCodes.Status403Forbidden);
            }
        }

        user.FullName = request.FullName.Trim();
        user.Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();
        user.DepartmentId = request.DepartmentId;
        user.Role = role;
        user.IsActive = request.IsActive;

        db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = TryGetActorId(principal),
            Action = "admin.user.update",
            Target = user.Username
        });
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> DeactivateUserAsync(
        int id,
        ClaimsPrincipal principal,
        IDbContextFactory<ChatDbContext> dbFactory,
        CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var user = await db.Users.FindAsync([id], ct);
        if (user is null)
        {
            return Results.NotFound();
        }

        var actorId = TryGetActorId(principal);
        if (actorId == user.Id)
        {
            return Results.BadRequest(new { error = "No puedes desactivar tu propia cuenta." });
        }

        user.IsActive = false;
        db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = actorId,
            Action = "admin.user.deactivate",
            Target = user.Username
        });
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ResetPasswordAsync(
        int id,
        ResetPasswordRequest request,
        ClaimsPrincipal principal,
        IDbContextFactory<ChatDbContext> dbFactory,
        IPasswordHasher hasher,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(request.NewPassword) || request.NewPassword.Length < 4)
        {
            return Results.BadRequest(new { error = "La contraseña nueva debe tener al menos 4 caracteres." });
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var user = await db.Users.FindAsync([id], ct);
        if (user is null)
        {
            return Results.NotFound();
        }

        user.PasswordHash = hasher.Hash(request.NewPassword);
        db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = TryGetActorId(principal),
            Action = "admin.user.reset-password",
            Target = user.Username
        });
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    /// <summary>
    /// Reactiva un usuario desactivado previamente. Aplica el cap de la
    /// licencia: si no hay slot disponible, falla con 403.
    /// </summary>
    private static async Task<IResult> ActivateUserAsync(
        int id,
        ClaimsPrincipal principal,
        IDbContextFactory<ChatDbContext> dbFactory,
        ILicenseValidator licensing,
        CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var user = await db.Users.FindAsync([id], ct);
        if (user is null) return Results.NotFound();
        if (user.IsActive) return Results.NoContent();

        var cap = await LicenseCap.CheckCanAddAsync(db, licensing, extra: 1, ct);
        if (!cap.Allowed)
        {
            return Results.Json(new
            {
                error = $"Edición {licensing.Current.Edition}: límite de {cap.Max} cuentas activas alcanzado ({cap.CurrentActive} en uso).",
            }, statusCode: StatusCodes.Status403Forbidden);
        }

        user.IsActive = true;
        db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = TryGetActorId(principal),
            Action = "admin.user.activate",
            Target = user.Username,
        });
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    /// <summary>
    /// Hard delete con anonimización: los mensajes / adjuntos / salas
    /// creadas por el usuario quedan re-asignados al usuario sistema
    /// "_deleted" (ver <see cref="EnterpriseChat.Server.Bootstrap.AdminSeeder.DeletedUserUsername"/>).
    /// Conserva el historial de conversaciones pero borra el rastro
    /// personal — patrón GDPR-friendly.
    ///
    /// Acciones:
    ///   - Messages.FromUserId  / ToUserId      → system user.
    ///   - Attachments.UploadedByUserId          → system user.
    ///   - Rooms.CreatedByUserId                 → system user.
    ///   - PinnedMessages.PinnedByUserId         → system user.
    ///   - RoomMembers, MessageReactions, SavedMessages, Sessions: delete cascade.
    ///   - User: delete.
    /// </summary>
    private static async Task<IResult> HardDeleteUserAsync(
        int id,
        ClaimsPrincipal principal,
        IDbContextFactory<ChatDbContext> dbFactory,
        CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var user = await db.Users.FindAsync([id], ct);
        if (user is null) return Results.NotFound();

        var actorId = TryGetActorId(principal);
        if (actorId == user.Id)
        {
            return Results.BadRequest(new { error = "No puedes borrarte a ti mismo." });
        }
        if (user.Username == Bootstrap.AdminSeeder.DeletedUserUsername)
        {
            return Results.BadRequest(new { error = "No se puede borrar el usuario sistema." });
        }

        var system = await db.Users.SingleOrDefaultAsync(
            u => u.Username == Bootstrap.AdminSeeder.DeletedUserUsername, ct);
        if (system is null)
        {
            return Results.Problem(
                "El usuario sistema '_deleted' no existe. Reinicia el server: el seeder lo crea al arrancar.",
                statusCode: StatusCodes.Status500InternalServerError);
        }

        // Re-asignaciones (UPDATE bulk a través de EF Core 8 ExecuteUpdate).
        await db.Messages
            .Where(m => m.FromUserId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.FromUserId, system.Id), ct);
        await db.Messages
            .Where(m => m.ToUserId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.ToUserId, system.Id), ct);
        await db.Attachments
            .Where(a => a.UploadedByUserId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.UploadedByUserId, system.Id), ct);
        await db.Rooms
            .Where(r => r.CreatedByUserId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.CreatedByUserId, system.Id), ct);
        await db.PinnedMessages
            .Where(p => p.PinnedByUserId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.PinnedByUserId, system.Id), ct);

        // Borrados directos (no aportan valor histórico).
        await db.RoomMembers.Where(rm => rm.UserId == id).ExecuteDeleteAsync(ct);
        await db.MessageReactions.Where(r => r.UserId == id).ExecuteDeleteAsync(ct);
        await db.SavedMessages.Where(s => s.UserId == id).ExecuteDeleteAsync(ct);
        await db.Sessions.Where(s => s.UserId == id).ExecuteDeleteAsync(ct);

        // AuditLogs.ActorUserId es nullable pero la FK se creó sin
        // ON DELETE SET NULL (ver migración InitialCreate). Sin esto, SQLite
        // rechaza el borrado con "FOREIGN KEY constraint failed" si el
        // usuario tiene alguna entrada de auditoría como actor.
        await db.AuditLogs
            .Where(a => a.ActorUserId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.ActorUserId, (int?)null), ct);

        db.Users.Remove(user);
        db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = actorId,
            Action = "admin.user.hard-delete",
            Target = user.Username,
            Details = $"anonymized to system user #{system.Id}",
        });
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ListDepartmentsAsync(
        IDbContextFactory<ChatDbContext> dbFactory,
        CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var departments = await db.Departments
            .OrderBy(d => d.Name)
            .Select(d => new DepartmentSummary(d.Id, d.Name))
            .ToListAsync(ct);
        return Results.Ok(departments);
    }

    private static async Task<IResult> CreateDepartmentAsync(
        CreateDepartmentRequest request,
        ClaimsPrincipal principal,
        IDbContextFactory<ChatDbContext> dbFactory,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Results.BadRequest(new { error = "Nombre obligatorio." });
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await db.Departments.AnyAsync(d => d.Name == request.Name, ct))
        {
            return Results.Conflict(new { error = "Ya existe un departamento con ese nombre." });
        }

        var entity = new Department { Name = request.Name.Trim() };
        db.Departments.Add(entity);
        db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = TryGetActorId(principal),
            Action = "admin.department.create",
            Target = entity.Name
        });
        await db.SaveChangesAsync(ct);
        return Results.Created($"/admin/departments/{entity.Id}", new DepartmentSummary(entity.Id, entity.Name));
    }

    private static string? ValidateCreate(CreateUserRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Username) || r.Username.Length is < 3 or > 64)
        {
            return "Usuario debe tener entre 3 y 64 caracteres.";
        }
        if (string.IsNullOrEmpty(r.Password) || r.Password.Length < 4)
        {
            return "La contraseña debe tener al menos 4 caracteres.";
        }
        if (string.IsNullOrWhiteSpace(r.FullName))
        {
            return "FullName es obligatorio.";
        }
        return null;
    }

    private static int? TryGetActorId(ClaimsPrincipal principal)
    {
        var raw = principal.FindFirstValue("sub") ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(raw, CultureInfo.InvariantCulture, out var id) ? id : null;
    }
}

using System.Globalization;
using System.Security.Claims;
using EnterpriseChat.Protocol.Admin;
using EnterpriseChat.Server.Auth;
using EnterpriseChat.Server.Data;
using EnterpriseChat.Server.Data.Entities;
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
        group.MapPost("/users", CreateUserAsync);
        group.MapPut("/users/{id:int}", UpdateUserAsync);
        group.MapDelete("/users/{id:int}", DeactivateUserAsync);
        group.MapPost("/users/{id:int}/reset-password", ResetPasswordAsync);

        group.MapGet("/departments", ListDepartmentsAsync);
        group.MapPost("/departments", CreateDepartmentAsync);
    }

    private static async Task<IResult> ListUsersAsync(
        IDbContextFactory<ChatDbContext> dbFactory,
        CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var users = await db.Users
            .Include(u => u.Department)
            .OrderBy(u => u.Username)
            .ToListAsync(ct);

        var result = users.Select(u => new AdminUserDetail(
            Id: u.Id,
            Username: u.Username,
            FullName: u.FullName,
            Email: u.Email,
            DepartmentId: u.DepartmentId,
            DepartmentName: u.Department?.Name,
            Role: u.Role.ToString(),
            IsActive: u.IsActive,
            CreatedAt: u.CreatedAt,
            LastLoginAt: u.LastLoginAt)).ToArray();

        return Results.Ok(result);
    }

    private static async Task<IResult> CreateUserAsync(
        CreateUserRequest request,
        ClaimsPrincipal principal,
        IDbContextFactory<ChatDbContext> dbFactory,
        IPasswordHasher hasher,
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

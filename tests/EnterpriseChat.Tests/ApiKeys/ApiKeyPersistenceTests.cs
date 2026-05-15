using EnterpriseChat.Server.Data;
using EnterpriseChat.Server.Data.Entities;
using EnterpriseChat.Tests.Server;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EnterpriseChat.Tests.ApiKeys;

/// <summary>
/// Smoke tests de la fase 1: comprueba que la entidad <c>ApiKey</c> se
/// mapea correctamente contra la BD (round-trip de campos + enum),
/// que el índice único en <c>KeyHash</c> está vivo y que la
/// autorreferencia <c>RotatedFrom</c> permite navegar la cadena de
/// rotaciones. Los tests más exhaustivos (FK SetNull, servicio,
/// middleware) llegarán en fases posteriores.
/// </summary>
public sealed class ApiKeyPersistenceTests : IClassFixture<ChatServerFactory>
{
    private readonly ChatServerFactory _factory;

    public ApiKeyPersistenceTests(ChatServerFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Round_trip_preserves_all_fields_including_enum_role()
    {
        await using var db = await CreateContextAsync();

        var now = DateTimeOffset.UtcNow;
        var key = new ApiKey
        {
            DisplayName = "Bot de turno",
            Prefix = "ec_pat_AB12",
            KeyHash = NewHash("round-trip"),
            Role = UserRole.Admin,
            CreatedAt = now,
            ExpiresAt = now.AddDays(30),
            Notes = "owner: equipo-ops"
        };
        db.ApiKeys.Add(key);
        await db.SaveChangesAsync();

        key.Id.Should().BeGreaterThan(0);

        await using var verifyDb = await CreateContextAsync();
        var loaded = await verifyDb.ApiKeys.SingleAsync(k => k.Id == key.Id);

        loaded.DisplayName.Should().Be("Bot de turno");
        loaded.Prefix.Should().Be("ec_pat_AB12");
        loaded.KeyHash.Should().Be(key.KeyHash);
        loaded.Role.Should().Be(UserRole.Admin);
        loaded.Notes.Should().Be("owner: equipo-ops");
        loaded.ExpiresAt.Should().NotBeNull();
        loaded.LastUsedAt.Should().BeNull();
        loaded.RevokedAt.Should().BeNull();
    }

    [Fact]
    public async Task Duplicate_KeyHash_violates_unique_index()
    {
        await using var db = await CreateContextAsync();

        var hash = NewHash("duplicate");
        db.ApiKeys.Add(new ApiKey
        {
            DisplayName = "Original",
            Prefix = "ec_pat_0001",
            KeyHash = hash,
            Role = UserRole.User
        });
        await db.SaveChangesAsync();

        db.ApiKeys.Add(new ApiKey
        {
            DisplayName = "Colisión",
            Prefix = "ec_pat_0002",
            KeyHash = hash,
            Role = UserRole.User
        });

        var act = async () => await db.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Rotation_chain_is_navigable_via_RotatedFrom()
    {
        await using var db = await CreateContextAsync();

        var original = new ApiKey
        {
            DisplayName = "CI build (v1)",
            Prefix = "ec_pat_V100",
            KeyHash = NewHash("rotate-v1"),
            Role = UserRole.Admin,
            RevokedAt = DateTimeOffset.UtcNow,
            RevokeReason = "rotated"
        };
        db.ApiKeys.Add(original);
        await db.SaveChangesAsync();

        var rotated = new ApiKey
        {
            DisplayName = "CI build (v2)",
            Prefix = "ec_pat_V200",
            KeyHash = NewHash("rotate-v2"),
            Role = UserRole.Admin,
            RotatedFromId = original.Id
        };
        db.ApiKeys.Add(rotated);
        await db.SaveChangesAsync();

        await using var verifyDb = await CreateContextAsync();
        var loaded = await verifyDb.ApiKeys
            .Include(k => k.RotatedFrom)
            .SingleAsync(k => k.Id == rotated.Id);

        loaded.RotatedFromId.Should().Be(original.Id);
        loaded.RotatedFrom.Should().NotBeNull();
        loaded.RotatedFrom!.DisplayName.Should().Be("CI build (v1)");
        loaded.RotatedFrom.RevokedAt.Should().NotBeNull();
    }

    private async Task<ChatDbContext> CreateContextAsync()
    {
        // Arrancar el host materializa el WebApplication y ejecuta las
        // migraciones; pedir el factory después garantiza que la tabla
        // ApiKeys ya existe en la BD del fixture.
        _ = _factory.Services;
        var dbFactory = _factory.Services.GetRequiredService<IDbContextFactory<ChatDbContext>>();
        return await dbFactory.CreateDbContextAsync();
    }

    /// <summary>SHA-256 hex de 64 chars derivado del seed para que cada test tenga el suyo sin colisiones.</summary>
    private static string NewHash(string seed)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(seed + Guid.NewGuid()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

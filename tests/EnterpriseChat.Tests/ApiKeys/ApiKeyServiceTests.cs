using EnterpriseChat.Server.ApiKeys;
using EnterpriseChat.Server.Data;
using EnterpriseChat.Server.Data.Entities;
using EnterpriseChat.Tests.Server;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EnterpriseChat.Tests.ApiKeys;

/// <summary>
/// Smoke tests del servicio: contratos de IssueAsync/RotateAsync/RevokeAsync/ResolveAsync
/// contra la BD real. La verificación E2E del middleware HTTP vive en
/// <see cref="ApiKeyAuthenticationTests"/>.
/// </summary>
public sealed class ApiKeyServiceTests : IClassFixture<ChatServerFactory>
{
    private readonly ChatServerFactory _factory;

    public ApiKeyServiceTests(ChatServerFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task IssueAsync_devuelve_plaintext_con_prefijo_y_persistido_solo_como_hash()
    {
        var svc = GetService();

        var issued = await svc.IssueAsync("smoke-issue", UserRole.User, createdByUserId: null);

        issued.Plaintext.Should().StartWith("ec_pat_");
        issued.Plaintext.Length.Should().BeGreaterThan(40, "32 bytes en base64url no padded ≈ 43 chars + prefijo");
        issued.Record.Id.Should().BeGreaterThan(0);
        issued.Record.KeyHash.Should().NotBe(issued.Plaintext, "la BD nunca guarda el plaintext");
        issued.Record.KeyHash.Should().HaveLength(64, "SHA-256 hex es 64 chars");
        issued.Record.Prefix.Should().StartWith("ec_pat_").And.HaveLength(11);
        issued.Record.RevokedAt.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_acepta_token_valido_y_rechaza_alteraciones()
    {
        var svc = GetService();
        var issued = await svc.IssueAsync("smoke-resolve", UserRole.Admin, createdByUserId: null);

        var resolved = await svc.ResolveAsync(issued.Plaintext);
        resolved.Should().NotBeNull();
        resolved!.Id.Should().Be(issued.Record.Id);
        resolved.Role.Should().Be(UserRole.Admin);

        // Alterar un carácter del plaintext rompe el hash y por tanto el lookup.
        var tampered = issued.Plaintext[..^1] + (issued.Plaintext[^1] == 'A' ? 'B' : 'A');
        var rejected = await svc.ResolveAsync(tampered);
        rejected.Should().BeNull();

        // Un token sin el prefijo se rechaza sin pegar a BD.
        var nonPat = await svc.ResolveAsync("eyJhbGciOiJIUzI1NiJ9.fake-jwt");
        nonPat.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_rechaza_token_revocado()
    {
        var svc = GetService();
        var issued = await svc.IssueAsync("smoke-revoke", UserRole.User, createdByUserId: null);

        var revoked = await svc.RevokeAsync(issued.Record.Id, actorUserId: null, reason: "test");
        revoked.Should().BeTrue();

        var second = await svc.RevokeAsync(issued.Record.Id, actorUserId: null);
        second.Should().BeFalse("revocar una clave ya revocada es no-op");

        var resolved = await svc.ResolveAsync(issued.Plaintext);
        resolved.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_rechaza_token_caducado()
    {
        var svc = GetService();
        var issued = await svc.IssueAsync(
            "smoke-expired",
            UserRole.User,
            createdByUserId: null,
            expiresAt: DateTimeOffset.UtcNow.AddDays(1));

        // Forzamos la caducidad escribiendo en BD una fecha pasada — no hay
        // forma "legítima" porque el servicio nunca se la pondría.
        await using (var db = await CreateDbAsync())
        {
            var row = await db.ApiKeys.SingleAsync(k => k.Id == issued.Record.Id);
            row.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        var resolved = await svc.ResolveAsync(issued.Plaintext);
        resolved.Should().BeNull();
    }

    [Fact]
    public async Task RotateAsync_emite_clave_nueva_y_revoca_la_anterior()
    {
        var svc = GetService();
        var original = await svc.IssueAsync("smoke-rotate", UserRole.Admin, createdByUserId: null);

        var rotated = await svc.RotateAsync(original.Record.Id, actorUserId: null);

        rotated.Record.Id.Should().NotBe(original.Record.Id);
        rotated.Record.RotatedFromId.Should().Be(original.Record.Id);
        rotated.Record.Role.Should().Be(UserRole.Admin, "la rotación preserva el role");
        rotated.Plaintext.Should().NotBe(original.Plaintext);

        // La vieja ya no es resoluble (queda revocada con grace=0).
        (await svc.ResolveAsync(original.Plaintext)).Should().BeNull();
        // La nueva sí.
        (await svc.ResolveAsync(rotated.Plaintext)).Should().NotBeNull();
    }

    private ApiKeyService GetService()
    {
        // Materializar Services arranca el host y aplica las migraciones,
        // necesario antes de tocar la tabla ApiKeys.
        return _factory.Services.GetRequiredService<ApiKeyService>();
    }

    private async Task<ChatDbContext> CreateDbAsync()
    {
        var dbFactory = _factory.Services.GetRequiredService<IDbContextFactory<ChatDbContext>>();
        return await dbFactory.CreateDbContextAsync();
    }
}

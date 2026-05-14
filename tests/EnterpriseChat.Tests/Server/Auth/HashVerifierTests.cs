using System.Security.Cryptography;
using System.Text;
using EnterpriseChat.Server.Auth.Hashers;
using FluentAssertions;
using HashAlgorithm = EnterpriseChat.Server.Auth.Hashers.HashAlgorithm;

namespace EnterpriseChat.Tests.Server.Auth;

/// <summary>
/// Cobertura mínima para los verifiers nuevos. La idea es asegurarse de
/// que aceptan hashes producidos por las herramientas estándar del
/// ecosistema (BCryptNet, openssl, Argon2 PHP, etc.) y rechazan inputs
/// malformados sin tirar excepciones.
/// </summary>
public class HashVerifierTests
{
    [Fact]
    public void Bcrypt_verifies_known_hash()
    {
        // BCrypt para "p4ssw0rd" generado con BCrypt.Net cost=4 (rápido).
        var stored = BCrypt.Net.BCrypt.HashPassword("p4ssw0rd", workFactor: 4);
        var v = new BcryptVerifier();
        v.Verify("p4ssw0rd", stored).Should().BeTrue();
        v.Verify("otro", stored).Should().BeFalse();
        v.Verify("", stored).Should().BeFalse();
    }

    [Theory]
    [InlineData("admin",    "21232f297a57a5a743894a0e4a801fc3")] // md5
    public void Md5_accepts_known_hash(string plaintext, string hash)
    {
        HexHashVerifier.Md5.Verify(plaintext, hash).Should().BeTrue();
        HexHashVerifier.Md5.Verify(plaintext, hash.ToUpperInvariant()).Should().BeTrue();
        HexHashVerifier.Md5.Verify("incorrecto", hash).Should().BeFalse();
        HexHashVerifier.Md5.Verify(plaintext, "00112233").Should().BeFalse();
    }

    [Fact]
    public void Sha1_accepts_known_hash()
    {
        // sha1("password") = 5baa61e4c9b93f3f0682250b6cf8331b7ee68fd8
        HexHashVerifier.Sha1.Verify("password", "5baa61e4c9b93f3f0682250b6cf8331b7ee68fd8").Should().BeTrue();
        HexHashVerifier.Sha1.Verify("password", "garbage").Should().BeFalse();
    }

    [Fact]
    public void Sha256_raw_accepts_known_hash()
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("hola")));
        HexHashVerifier.Sha256.Verify("hola", hash).Should().BeTrue();
        HexHashVerifier.Sha256.Verify("hola", hash.ToLowerInvariant()).Should().BeTrue();
    }

    [Fact]
    public void Sha256Salted_accepts_django_style_hex()
    {
        // sha256("saltLcontrasena") con salt="saltL" -> formato sha256$saltL$<hex>
        var salt = "saltL";
        var plaintext = "contrasena";
        var hex = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(salt + plaintext))).ToLowerInvariant();
        var stored = $"sha256${salt}${hex}";

        var v = new Sha256SaltedVerifier();
        v.Verify(plaintext, stored).Should().BeTrue();
        v.Verify("otra", stored).Should().BeFalse();
    }

    [Fact]
    public void Sha256Salted_accepts_base64_variant()
    {
        var salt = "rB";
        var plaintext = "hola";
        var b64 = Convert.ToBase64String(
            SHA256.HashData(Encoding.UTF8.GetBytes(salt + plaintext)));
        var stored = $"sha256${salt}${b64}";

        new Sha256SaltedVerifier().Verify(plaintext, stored).Should().BeTrue();
    }

    [Fact]
    public void Sha256Salted_rejects_malformed_input()
    {
        var v = new Sha256SaltedVerifier();
        v.Verify("x", "no-tiene-formato").Should().BeFalse();
        v.Verify("x", "md5$salt$hash").Should().BeFalse();
        v.Verify("", "sha256$s$h").Should().BeFalse();
    }

    [Fact]
    public void Plaintext_verifier_constant_time_equals()
    {
        var v = new PlaintextVerifier();
        v.Verify("clave", "clave").Should().BeTrue();
        v.Verify("clave", "Clave").Should().BeFalse();
        v.Verify("clave", "clave  ").Should().BeFalse();
        v.Verify("", "").Should().BeFalse(); // misma política que el resto: vacío = no auth
    }

    [Fact]
    public void Registry_maps_every_algorithm()
    {
        var reg = new HashVerifierRegistry();
        foreach (HashAlgorithm alg in Enum.GetValues<HashAlgorithm>())
        {
            reg.TryGet(alg, out var verifier).Should().BeTrue($"el registro debería cubrir {alg}");
            verifier.Should().NotBeNull();
        }
    }
}

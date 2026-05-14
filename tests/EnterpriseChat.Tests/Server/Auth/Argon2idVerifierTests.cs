using System.Text;
using EnterpriseChat.Server.Auth.Hashers;
using FluentAssertions;
using Konscious.Security.Cryptography;

namespace EnterpriseChat.Tests.Server.Auth;

public class Argon2idVerifierTests
{
    [Fact]
    public void Verifies_round_trip_hash()
    {
        // Generamos un hash Argon2id con parámetros realistas pero
        // baratos para que el test no tarde — m=1024 KiB, t=2, p=1.
        var password = "p4ssw0rd";
        var salt = Encoding.UTF8.GetBytes("salt16bytes12345");
        var (memKib, iter, par, hashLen) = (1024, 2, 1, 32);

        byte[] hash;
        using (var argon = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            MemorySize = memKib,
            Iterations = iter,
            DegreeOfParallelism = par,
        })
        {
            hash = argon.GetBytes(hashLen);
        }

        var stored = $"$argon2id$v=19$m={memKib},t={iter},p={par}${Base64NoPad(salt)}${Base64NoPad(hash)}";

        var v = new Argon2idVerifier();
        v.Verify(password, stored).Should().BeTrue();
        v.Verify("otra-clave", stored).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-argon")]
    [InlineData("$argon2id$v=19$m=1024,t=2,p=1$badsalt")]
    [InlineData("$argon2d$v=19$m=1024,t=2,p=1$c2FsdA$aGFzaA")]
    public void Rejects_malformed_input(string stored)
    {
        new Argon2idVerifier().Verify("password", stored).Should().BeFalse();
    }

    private static string Base64NoPad(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=');
}

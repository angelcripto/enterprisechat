using System.Security.Cryptography;
using EnterpriseChat.Server.Crypto;
using FluentAssertions;

namespace EnterpriseChat.Tests.Server.Auth;

public class AppCryptoTests
{
    [Fact]
    public void Round_trip_string()
    {
        var key = AppCrypto.DecodeKey(AppCrypto.GenerateBase64Key());
        var crypto = new AppCrypto(key);

        var ciphertext = crypto.EncryptString("contraseña con eñe y emoji 🔒");
        ciphertext.Should().NotBeNullOrEmpty();
        crypto.DecryptString(ciphertext).Should().Be("contraseña con eñe y emoji 🔒");
    }

    [Fact]
    public void Two_encryptions_of_same_value_differ()
    {
        var crypto = new AppCrypto(AppCrypto.DecodeKey(AppCrypto.GenerateBase64Key()));
        var a = crypto.EncryptString("same");
        var b = crypto.EncryptString("same");
        a.Should().NotBe(b, "el nonce aleatorio debería garantizar IND-CPA");
    }

    [Fact]
    public void Tampered_blob_throws()
    {
        var crypto = new AppCrypto(AppCrypto.DecodeKey(AppCrypto.GenerateBase64Key()));
        var ciphertext = crypto.EncryptString("dato");
        var bytes = Convert.FromBase64String(ciphertext);
        // Flip a byte en mitad del ciphertext para forzar fallo de tag.
        bytes[bytes.Length / 2] ^= 0xFF;
        var tampered = Convert.ToBase64String(bytes);

        Action act = () => crypto.DecryptString(tampered);
        act.Should().Throw<CryptographicException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-base64-$$$")]
    [InlineData("c2hvcnQ=")] // base64 válido pero longitud insuficiente
    public void Rejects_invalid_keys_or_inputs(string input)
    {
        Action decodeKey = () => AppCrypto.DecodeKey(input);
        decodeKey.Should().Throw<ArgumentException>();
    }
}

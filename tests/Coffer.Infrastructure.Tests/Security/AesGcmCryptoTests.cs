using System.Security.Cryptography;
using Coffer.Infrastructure.Security;
using FluentAssertions;

namespace Coffer.Infrastructure.Tests.Security;

public class AesGcmCryptoTests
{
    private static readonly byte[] _key = BuildDeterministic_key(seed: 42);
    private static readonly byte[] _plaintext = "the quick brown fox jumps over the lazy dog"u8.ToArray();

    [Fact]
    public void Encrypt_ThenDecrypt_RoundTrips()
    {
        var result = AesGcmCrypto.Encrypt(_plaintext, _key);

        var decrypted = AesGcmCrypto.Decrypt(result.Ciphertext, result.Iv, result.Tag, _key);

        decrypted.Should().Equal(_plaintext);
    }

    [Fact]
    public void Decrypt_WithTamperedCiphertext_ThrowsCryptographicException()
    {
        var result = AesGcmCrypto.Encrypt(_plaintext, _key);
        result.Ciphertext[0] ^= 0xFF;

        var act = () => AesGcmCrypto.Decrypt(result.Ciphertext, result.Iv, result.Tag, _key);

        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Decrypt_WithWrong_key_ThrowsCryptographicException()
    {
        var result = AesGcmCrypto.Encrypt(_plaintext, _key);
        var wrong_key = BuildDeterministic_key(seed: 99);

        var act = () => AesGcmCrypto.Decrypt(result.Ciphertext, result.Iv, result.Tag, wrong_key);

        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Encrypt_ProducesUniqueIvForEachCall()
    {
        var first = AesGcmCrypto.Encrypt(_plaintext, _key);
        var second = AesGcmCrypto.Encrypt(_plaintext, _key);

        first.Iv.Should().NotEqual(second.Iv);
    }

    private static byte[] BuildDeterministic_key(int seed)
    {
        var key = new byte[32];
#pragma warning disable CA5394
        new Random(seed).NextBytes(key);
#pragma warning restore CA5394
        return key;
    }
}

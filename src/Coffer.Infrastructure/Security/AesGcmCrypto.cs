using System.Security.Cryptography;

namespace Coffer.Infrastructure.Security;

public sealed record AesGcmResult(byte[] Iv, byte[] Ciphertext, byte[] Tag);

public static class AesGcmCrypto
{
    public const int IvBytes = 12;
    public const int TagBytes = 16;

    public static AesGcmResult Encrypt(byte[] plaintext, byte[] key, byte[]? associatedData = null)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        ArgumentNullException.ThrowIfNull(key);

        var iv = RandomNumberGenerator.GetBytes(IvBytes);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagBytes];

        using var aes = new AesGcm(key, TagBytes);
        aes.Encrypt(iv, plaintext, ciphertext, tag, associatedData);

        return new AesGcmResult(iv, ciphertext, tag);
    }

    public static byte[] Decrypt(
        byte[] ciphertext,
        byte[] iv,
        byte[] tag,
        byte[] key,
        byte[]? associatedData = null)
    {
        ArgumentNullException.ThrowIfNull(ciphertext);
        ArgumentNullException.ThrowIfNull(iv);
        ArgumentNullException.ThrowIfNull(tag);
        ArgumentNullException.ThrowIfNull(key);

        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(key, TagBytes);
        aes.Decrypt(iv, ciphertext, tag, plaintext, associatedData);

        return plaintext;
    }
}

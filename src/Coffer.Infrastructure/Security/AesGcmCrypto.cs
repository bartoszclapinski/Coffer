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

    /// <summary>
    /// Decrypts an AES-GCM ciphertext and returns the plaintext. The caller owns the returned
    /// buffer and is responsible for calling <see cref="Array.Clear(System.Array,int,int)"/> on
    /// it once finished when the contents are sensitive (e.g. a DEK or master key).
    /// </summary>
    /// <exception cref="CryptographicException">
    /// Thrown when the authentication tag does not match the ciphertext (tampering, wrong key,
    /// or wrong associated data).
    /// </exception>
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

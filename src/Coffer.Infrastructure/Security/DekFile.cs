using System.Text;
using Coffer.Core.Security;

namespace Coffer.Infrastructure.Security;

/// <summary>
/// The on-disk encrypted DEK. Version 1 stores a single password-wrapped copy of the DEK; version 2
/// (dual-wrap, doc 09) appends a second, seed-wrapped copy so the DEK can be unlocked by <em>either</em>
/// the Argon2-derived master key <em>or</em> the PBKDF2-derived BIP39 recovery key. The seed blob needs no
/// salt/KDF params in the file — the recovery key is derived purely from the mnemonic plus a fixed app
/// passphrase. Reading is backward-compatible: a v1 file loads with null seed fields.
/// </summary>
public sealed record DekFile(
    byte Version,
    Argon2Parameters ArgonParameters,
    byte[] Salt,
    byte[] Iv,
    byte[] Tag,
    byte[] Ciphertext,
    byte[]? SeedIv = null,
    byte[]? SeedTag = null,
    byte[]? SeedCiphertext = null)
{
    /// <summary>Version written by new vaults — dual-wrapped (password + seed).</summary>
    public const byte CurrentVersion = 2;

    /// <summary>Legacy password-only version, still readable so existing vaults keep working.</summary>
    public const byte PasswordOnlyVersion = 1;

    /// <summary>True when the file carries a seed-wrapped copy of the DEK (v2).</summary>
    public bool HasSeedWrap => SeedIv is not null && SeedTag is not null && SeedCiphertext is not null;

    /// <summary>
    /// Writes a new DEK file, failing if one already exists. <see cref="FileMode.CreateNew"/> is an atomic
    /// create-or-fail: it closes the TOCTOU window between a caller's <c>File.Exists</c> pre-flight and this
    /// write, so a second process that created the file in the gap gets an IOException instead of a silent
    /// overwrite. Use <see cref="WriteReplaceAsync"/> to intentionally rewrite an existing file.
    /// </summary>
    public static async Task WriteAsync(DekFile file, string path, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(path);

        EnsureDirectory(path);
        var payload = Serialize(file);

        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await stream.WriteAsync(payload, ct);
    }

    /// <summary>
    /// Atomically replaces an existing DEK file (or creates it if absent). Writes a temp file next to the
    /// target then moves it into place, so a crash mid-write never destroys the only copy of the key. Used
    /// when re-wrapping the DEK (seed recovery, enabling seed recovery).
    /// </summary>
    public static async Task WriteReplaceAsync(DekFile file, string path, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(path);

        EnsureDirectory(path);
        var payload = Serialize(file);

        var tmp = path + ".tmp";
        await using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await stream.WriteAsync(payload, ct);
            await stream.FlushAsync(ct);
        }

        File.Move(tmp, path, overwrite: true);
    }

    public static async Task<DekFile> ReadAsync(string path, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("DEK file not found.", path);
        }

        var bytes = await File.ReadAllBytesAsync(path, ct);

        try
        {
            using var ms = new MemoryStream(bytes);
            using var reader = new BinaryReader(ms);

            var version = reader.ReadByte();
            if (version is not (PasswordOnlyVersion or CurrentVersion))
            {
                throw new InvalidDataException(
                    $"DEK file version {version} is not supported (expected {PasswordOnlyVersion} or {CurrentVersion}).");
            }

            var memorySizeKb = reader.ReadInt32();
            var iterations = reader.ReadInt32();
            var parallelism = reader.ReadInt32();
            var outputBytes = reader.ReadInt32();
            var saltBytesParam = reader.ReadInt32();

            var salt = ReadLengthPrefixedBytes(reader, "salt");
            if (salt.Length != saltBytesParam)
            {
                throw new InvalidDataException(
                    $"DEK file salt length ({salt.Length}) does not match Argon2Parameters.SaltBytes ({saltBytesParam}).");
            }

            var iv = ReadLengthPrefixedBytes(reader, "iv");
            var tag = ReadLengthPrefixedBytes(reader, "tag");
            var ciphertext = ReadLengthPrefixedBytes(reader, "ciphertext");

            byte[]? seedIv = null;
            byte[]? seedTag = null;
            byte[]? seedCiphertext = null;
            if (version >= CurrentVersion)
            {
                seedIv = ReadLengthPrefixedBytes(reader, "seed iv");
                seedTag = ReadLengthPrefixedBytes(reader, "seed tag");
                seedCiphertext = ReadLengthPrefixedBytes(reader, "seed ciphertext");
            }

            return new DekFile(
                version,
                new Argon2Parameters(memorySizeKb, iterations, parallelism, outputBytes, saltBytesParam),
                salt,
                iv,
                tag,
                ciphertext,
                seedIv,
                seedTag,
                seedCiphertext);
        }
        catch (EndOfStreamException ex)
        {
            throw new InvalidDataException("DEK file is malformed or truncated.", ex);
        }
    }

    private static byte[] Serialize(DekFile file)
    {
        if (file.Version >= CurrentVersion && !file.HasSeedWrap)
        {
            throw new InvalidOperationException("A version 2 DEK file must include the seed-wrapped key.");
        }

        using var ms = new MemoryStream();
        using (var writer = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(file.Version);
            writer.Write(file.ArgonParameters.MemorySizeKb);
            writer.Write(file.ArgonParameters.Iterations);
            writer.Write(file.ArgonParameters.Parallelism);
            writer.Write(file.ArgonParameters.OutputBytes);
            writer.Write(file.ArgonParameters.SaltBytes);
            WriteLengthPrefixedBytes(writer, file.Salt);
            WriteLengthPrefixedBytes(writer, file.Iv);
            WriteLengthPrefixedBytes(writer, file.Tag);
            WriteLengthPrefixedBytes(writer, file.Ciphertext);

            if (file.Version >= CurrentVersion)
            {
                WriteLengthPrefixedBytes(writer, file.SeedIv!);
                WriteLengthPrefixedBytes(writer, file.SeedTag!);
                WriteLengthPrefixedBytes(writer, file.SeedCiphertext!);
            }
        }

        return ms.ToArray();
    }

    private static void EnsureDirectory(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private static void WriteLengthPrefixedBytes(BinaryWriter writer, byte[] data)
    {
        writer.Write(data.Length);
        writer.Write(data);
    }

    private static byte[] ReadLengthPrefixedBytes(BinaryReader reader, string fieldName)
    {
        var length = reader.ReadInt32();
        if (length < 0)
        {
            throw new InvalidDataException($"DEK file has negative length for '{fieldName}'.");
        }

        var data = reader.ReadBytes(length);
        if (data.Length != length)
        {
            throw new InvalidDataException($"DEK file truncated reading '{fieldName}'.");
        }

        return data;
    }
}

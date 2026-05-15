using System.Text;
using Coffer.Core.Security;

namespace Coffer.Infrastructure.Security;

public sealed record DekFile(
    byte Version,
    Argon2Parameters ArgonParameters,
    byte[] Salt,
    byte[] Iv,
    byte[] Tag,
    byte[] Ciphertext)
{
    public const byte CurrentVersion = 1;

    public static async Task WriteAsync(DekFile file, string path, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(path);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        byte[] payload;
        using (var ms = new MemoryStream())
        {
            using (var writer = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(file.Version);
                writer.Write(file.ArgonParameters.MemorySizeKb);
                writer.Write(file.ArgonParameters.Iterations);
                writer.Write(file.ArgonParameters.Parallelism);
                writer.Write(file.ArgonParameters.OutputBytes);
                writer.Write(file.ArgonParameters.SaltBytes);
                writer.Write(file.Salt.Length);
                writer.Write(file.Salt);
                writer.Write(file.Iv.Length);
                writer.Write(file.Iv);
                writer.Write(file.Tag.Length);
                writer.Write(file.Tag);
                writer.Write(file.Ciphertext.Length);
                writer.Write(file.Ciphertext);
            }
            payload = ms.ToArray();
        }

        await File.WriteAllBytesAsync(path, payload, ct);
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
            var memorySizeKb = reader.ReadInt32();
            var iterations = reader.ReadInt32();
            var parallelism = reader.ReadInt32();
            var outputBytes = reader.ReadInt32();
            var saltBytesParam = reader.ReadInt32();

            var salt = ReadLengthPrefixedBytes(reader, "salt");
            var iv = ReadLengthPrefixedBytes(reader, "iv");
            var tag = ReadLengthPrefixedBytes(reader, "tag");
            var ciphertext = ReadLengthPrefixedBytes(reader, "ciphertext");

            return new DekFile(
                version,
                new Argon2Parameters(memorySizeKb, iterations, parallelism, outputBytes, saltBytesParam),
                salt,
                iv,
                tag,
                ciphertext);
        }
        catch (EndOfStreamException ex)
        {
            throw new InvalidDataException("DEK file is malformed or truncated.", ex);
        }
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

using Coffer.Core.Security;
using Coffer.Infrastructure.Security;
using FluentAssertions;

namespace Coffer.Infrastructure.Tests.Security;

public class DekFileTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _filePath;

    public DekFileTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Coffer.Tests-" + Guid.NewGuid().ToString("N"));
        _filePath = Path.Combine(_tempDir, "dek.encrypted");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, recursive: true);
            }
            catch
            {
                // Best-effort cleanup.
            }
        }
    }

    [Fact]
    public async Task WriteThenRead_RoundTrip_ReturnsEquivalentFile()
    {
        var original = new DekFile(
            Version: DekFile.CurrentVersion,
            ArgonParameters: Argon2Parameters.Default,
            Salt: new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 },
            Iv: new byte[] { 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28 },
            Tag: new byte[] { 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44 },
            Ciphertext: new byte[] { 45, 46, 47, 48, 49, 50, 51, 52 });

        await DekFile.WriteAsync(original, _filePath, CancellationToken.None);
        var loaded = await DekFile.ReadAsync(_filePath, CancellationToken.None);

        loaded.Version.Should().Be(original.Version);
        loaded.ArgonParameters.Should().Be(original.ArgonParameters);
        loaded.Salt.Should().Equal(original.Salt);
        loaded.Iv.Should().Equal(original.Iv);
        loaded.Tag.Should().Equal(original.Tag);
        loaded.Ciphertext.Should().Equal(original.Ciphertext);
    }

    [Fact]
    public async Task ReadAsync_FromMissingFile_ThrowsFileNotFoundException()
    {
        var act = async () => await DekFile.ReadAsync(_filePath, CancellationToken.None);

        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task ReadAsync_FromCorruptedFile_ThrowsInvalidDataException()
    {
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllBytesAsync(_filePath, new byte[] { 1, 2, 3 });

        var act = async () => await DekFile.ReadAsync(_filePath, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task ReadAsync_FromUnsupportedVersion_ThrowsInvalidDataException()
    {
        var original = new DekFile(
            Version: DekFile.CurrentVersion,
            ArgonParameters: Argon2Parameters.Default,
            Salt: new byte[Argon2Parameters.Default.SaltBytes],
            Iv: new byte[12],
            Tag: new byte[16],
            Ciphertext: new byte[32]);
        await DekFile.WriteAsync(original, _filePath, CancellationToken.None);

        // Tamper the version byte (first byte of file) to an unsupported value.
        var bytes = await File.ReadAllBytesAsync(_filePath);
        bytes[0] = 0xFF;
        await File.WriteAllBytesAsync(_filePath, bytes);

        var act = async () => await DekFile.ReadAsync(_filePath, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidDataException>()
            .WithMessage("DEK file version * is not supported*");
    }
}

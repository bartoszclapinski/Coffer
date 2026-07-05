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
    public async Task WriteThenRead_V2DualWrap_RoundTrips()
    {
        var original = new DekFile(
            Version: DekFile.CurrentVersion,
            ArgonParameters: Argon2Parameters.Default,
            Salt: new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 },
            Iv: new byte[] { 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28 },
            Tag: new byte[] { 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44 },
            Ciphertext: new byte[] { 45, 46, 47, 48, 49, 50, 51, 52 },
            SeedIv: new byte[] { 60, 61, 62, 63, 64, 65, 66, 67, 68, 69, 70, 71 },
            SeedTag: new byte[] { 80, 81, 82, 83, 84, 85, 86, 87, 88, 89, 90, 91, 92, 93, 94, 95 },
            SeedCiphertext: new byte[] { 100, 101, 102, 103, 104, 105, 106, 107 });

        await DekFile.WriteAsync(original, _filePath, CancellationToken.None);
        var loaded = await DekFile.ReadAsync(_filePath, CancellationToken.None);

        loaded.Version.Should().Be(DekFile.CurrentVersion);
        loaded.ArgonParameters.Should().Be(original.ArgonParameters);
        loaded.Salt.Should().Equal(original.Salt);
        loaded.Iv.Should().Equal(original.Iv);
        loaded.Tag.Should().Equal(original.Tag);
        loaded.Ciphertext.Should().Equal(original.Ciphertext);
        loaded.HasSeedWrap.Should().BeTrue();
        loaded.SeedIv.Should().Equal(original.SeedIv);
        loaded.SeedTag.Should().Equal(original.SeedTag);
        loaded.SeedCiphertext.Should().Equal(original.SeedCiphertext);
    }

    [Fact]
    public async Task ReadAsync_LegacyV1File_LoadsWithNoSeedWrap()
    {
        var original = new DekFile(
            Version: DekFile.PasswordOnlyVersion,
            ArgonParameters: Argon2Parameters.Default,
            Salt: new byte[Argon2Parameters.Default.SaltBytes],
            Iv: new byte[12],
            Tag: new byte[16],
            Ciphertext: new byte[32]);

        await DekFile.WriteAsync(original, _filePath, CancellationToken.None);
        var loaded = await DekFile.ReadAsync(_filePath, CancellationToken.None);

        loaded.Version.Should().Be(DekFile.PasswordOnlyVersion);
        loaded.HasSeedWrap.Should().BeFalse();
        loaded.SeedIv.Should().BeNull();
        loaded.SeedCiphertext.Should().BeNull();
    }

    [Fact]
    public async Task WriteAsync_V2WithoutSeedWrap_Throws()
    {
        var invalid = new DekFile(
            Version: DekFile.CurrentVersion,
            ArgonParameters: Argon2Parameters.Default,
            Salt: new byte[Argon2Parameters.Default.SaltBytes],
            Iv: new byte[12],
            Tag: new byte[16],
            Ciphertext: new byte[32]); // no seed blob

        var act = async () => await DekFile.WriteAsync(invalid, _filePath, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task WriteReplaceAsync_OverwritesAnExistingFile()
    {
        var first = new DekFile(
            DekFile.PasswordOnlyVersion, Argon2Parameters.Default,
            new byte[Argon2Parameters.Default.SaltBytes], new byte[12], new byte[16], new byte[32]);
        await DekFile.WriteAsync(first, _filePath, CancellationToken.None);

        var second = first with { Ciphertext = new byte[] { 9, 9, 9, 9 } };
        await DekFile.WriteReplaceAsync(second, _filePath, CancellationToken.None);

        var loaded = await DekFile.ReadAsync(_filePath, CancellationToken.None);
        loaded.Ciphertext.Should().Equal(9, 9, 9, 9);
        File.Exists(_filePath + ".tmp").Should().BeFalse("the temp file is moved into place");
    }

    [Fact]
    public async Task WriteAsync_WhenFileExists_Throws()
    {
        var file = new DekFile(
            Version: DekFile.PasswordOnlyVersion,
            ArgonParameters: Argon2Parameters.Default,
            Salt: new byte[Argon2Parameters.Default.SaltBytes],
            Iv: new byte[12],
            Tag: new byte[16],
            Ciphertext: new byte[32]);

        await DekFile.WriteAsync(file, _filePath, CancellationToken.None);

        // CreateNew must refuse to clobber an existing DEK file (TOCTOU guard).
        var act = async () => await DekFile.WriteAsync(file, _filePath, CancellationToken.None);

        await act.Should().ThrowAsync<IOException>();
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
            Version: DekFile.PasswordOnlyVersion,
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

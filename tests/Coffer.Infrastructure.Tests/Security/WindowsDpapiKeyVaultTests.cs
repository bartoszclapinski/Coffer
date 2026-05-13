using System.Runtime.Versioning;
using Coffer.Infrastructure.Security;
using FluentAssertions;

namespace Coffer.Infrastructure.Tests.Security;

[SupportedOSPlatform("windows")]
public class WindowsDpapiKeyVaultTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _cachePath;

    public WindowsDpapiKeyVaultTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Coffer.Tests-" + Guid.NewGuid().ToString("N"));
        _cachePath = Path.Combine(_tempDir, "master-key.dpapi.cache");
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

    [SkippableFact]
    public async Task SetThenGet_RoundTrip_ReturnsSameBytes()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "DPAPI only available on Windows");

        var vault = new WindowsDpapiKeyVault(_cachePath);
        var original = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0xCA, 0xFE, 0xBA, 0xBE };

        await vault.SetCachedMasterKeyAsync(original, TimeSpan.FromMinutes(5), CancellationToken.None);
        var retrieved = await vault.GetCachedMasterKeyAsync(CancellationToken.None);

        retrieved.Should().NotBeNull();
        retrieved.Should().Equal(original);
    }

    [SkippableFact]
    public async Task Set_WritesEncryptedFile_NotPlaintext()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "DPAPI only available on Windows");

        var vault = new WindowsDpapiKeyVault(_cachePath);
        var original = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0xCA, 0xFE, 0xBA, 0xBE };

        await vault.SetCachedMasterKeyAsync(original, TimeSpan.FromMinutes(5), CancellationToken.None);
        var fileBytes = await File.ReadAllBytesAsync(_cachePath);

        ContainsSequence(fileBytes, original).Should().BeFalse(
            "the original master key bytes should not appear in the DPAPI-encrypted file");
    }

    [SkippableFact]
    public async Task Get_AfterTtlExpired_ReturnsNullAndDeletesFile()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "DPAPI only available on Windows");

        var vault = new WindowsDpapiKeyVault(_cachePath);
        await vault.SetCachedMasterKeyAsync(new byte[] { 1, 2, 3 }, TimeSpan.FromMilliseconds(50), CancellationToken.None);
        await Task.Delay(150);

        var retrieved = await vault.GetCachedMasterKeyAsync(CancellationToken.None);

        retrieved.Should().BeNull();
        File.Exists(_cachePath).Should().BeFalse();
    }

    [SkippableFact]
    public async Task Get_WhenFileMissing_ReturnsNull()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "DPAPI only available on Windows");

        var vault = new WindowsDpapiKeyVault(_cachePath);

        var retrieved = await vault.GetCachedMasterKeyAsync(CancellationToken.None);

        retrieved.Should().BeNull();
    }

    [SkippableFact]
    public async Task Get_WhenFileCorrupted_ReturnsNull()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "DPAPI only available on Windows");

        Directory.CreateDirectory(_tempDir);
        await File.WriteAllBytesAsync(_cachePath, new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF });

        var vault = new WindowsDpapiKeyVault(_cachePath);
        var retrieved = await vault.GetCachedMasterKeyAsync(CancellationToken.None);

        retrieved.Should().BeNull();
    }

    [SkippableFact]
    public async Task Invalidate_DeletesCacheFile()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "DPAPI only available on Windows");

        var vault = new WindowsDpapiKeyVault(_cachePath);
        await vault.SetCachedMasterKeyAsync(new byte[] { 1, 2, 3 }, TimeSpan.FromMinutes(5), CancellationToken.None);
        File.Exists(_cachePath).Should().BeTrue();

        await vault.InvalidateMasterKeyCacheAsync(CancellationToken.None);

        File.Exists(_cachePath).Should().BeFalse();
    }

    [SkippableFact]
    public async Task Set_CreatesParentDirectory()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "DPAPI only available on Windows");

        Directory.Exists(_tempDir).Should().BeFalse();
        var vault = new WindowsDpapiKeyVault(_cachePath);

        await vault.SetCachedMasterKeyAsync(new byte[] { 1, 2, 3 }, TimeSpan.FromMinutes(5), CancellationToken.None);

        Directory.Exists(_tempDir).Should().BeTrue();
        File.Exists(_cachePath).Should().BeTrue();
    }

    private static bool ContainsSequence(byte[] haystack, byte[] needle)
    {
        if (needle.Length == 0 || needle.Length > haystack.Length)
        {
            return false;
        }

        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle))
            {
                return true;
            }
        }
        return false;
    }
}

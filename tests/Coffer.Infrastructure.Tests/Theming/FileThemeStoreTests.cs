using Coffer.Core.Theming;
using Coffer.Infrastructure.Tests.Security;
using Coffer.Infrastructure.Theming;
using FluentAssertions;

namespace Coffer.Infrastructure.Tests.Theming;

public class FileThemeStoreTests
{
    [Fact]
    public void Load_DefaultsToDark_WhenFileAbsent()
    {
        using var paths = new TestVaultPaths();
        var store = new FileThemeStore(paths);

        // With Sprint 29 complete, Dark is the app default for a fresh install.
        store.Load().Should().Be(AppTheme.Dark);
    }

    [Theory]
    [InlineData(AppTheme.Light)]
    [InlineData(AppTheme.Dark)]
    public void SaveThenLoad_RoundTrips(AppTheme theme)
    {
        using var paths = new TestVaultPaths();
        var store = new FileThemeStore(paths);

        store.Save(theme);

        new FileThemeStore(paths).Load().Should().Be(theme);
    }

    [Fact]
    public void Load_DefaultsToDark_WhenFileCorrupt()
    {
        using var paths = new TestVaultPaths();
        File.WriteAllText(Path.Combine(paths.LocalAppDataFolder, "theme.json"), "{ not valid json");
        var store = new FileThemeStore(paths);

        store.Load().Should().Be(AppTheme.Dark);
    }
}

using Coffer.Core.Localization;
using Coffer.Infrastructure.Localization;
using Coffer.Infrastructure.Tests.Security;
using FluentAssertions;

namespace Coffer.Infrastructure.Tests.Localization;

public class FileLanguageStoreTests
{
    [Fact]
    public void Load_DefaultsToPolish_WhenFileAbsent()
    {
        using var paths = new TestVaultPaths();
        var store = new FileLanguageStore(paths);

        store.Load().Should().Be(AppLanguage.Polish);
    }

    [Theory]
    [InlineData(AppLanguage.Polish)]
    [InlineData(AppLanguage.English)]
    public void SaveThenLoad_RoundTrips(AppLanguage language)
    {
        using var paths = new TestVaultPaths();
        var store = new FileLanguageStore(paths);

        store.Save(language);

        new FileLanguageStore(paths).Load().Should().Be(language);
    }

    [Fact]
    public void Load_DefaultsToPolish_WhenFileCorrupt()
    {
        using var paths = new TestVaultPaths();
        File.WriteAllText(Path.Combine(paths.LocalAppDataFolder, "language.json"), "{ not valid json");
        var store = new FileLanguageStore(paths);

        store.Load().Should().Be(AppLanguage.Polish);
    }
}

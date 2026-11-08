using Coffer.Infrastructure.Logging;
using Coffer.Infrastructure.Security;
using FluentAssertions;

namespace Coffer.Infrastructure.Tests.Security;

public class CofferDataRootTests
{
    private static readonly string _localAppData = Path.Combine(Path.GetTempPath(), "fake-local-app-data");

    [Fact]
    public void Resolve_DebugBuildWithoutOverride_UsesTheDevFolder()
    {
        CofferDataRoot.Resolve(null, isDebugBuild: true, _localAppData)
            .Should().Be(Path.Combine(_localAppData, "Coffer.Dev"));
    }

    [Fact]
    public void Resolve_ReleaseBuildWithoutOverride_UsesTheRealVaultFolder()
    {
        CofferDataRoot.Resolve(null, isDebugBuild: false, _localAppData)
            .Should().Be(Path.Combine(_localAppData, "Coffer"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Resolve_WithOverride_WinsInEveryBuild(bool isDebugBuild)
    {
        var custom = Path.Combine(Path.GetTempPath(), "coffer-custom-root");

        CofferDataRoot.Resolve(custom, isDebugBuild, _localAppData).Should().Be(custom);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_WithBlankOverride_FallsBackToTheBuildDefault(string blank)
    {
        CofferDataRoot.Resolve(blank, isDebugBuild: false, _localAppData)
            .Should().Be(Path.Combine(_localAppData, "Coffer"));
    }

    [Fact]
    public void Resolve_WithRelativeOverride_IsMadeAbsolute()
    {
        Path.IsPathFullyQualified(CofferDataRoot.Resolve("relative-root", isDebugBuild: true, _localAppData))
            .Should().BeTrue();
    }

    [Fact]
    public void Resolve_WithRelativeOverride_IsAnchoredAtTheBaseDirectory_NotTheCurrentDirectory()
    {
        var baseDirectory = Path.Combine(_localAppData, "app");

        CofferDataRoot.Resolve("relative-root", isDebugBuild: true, _localAppData, baseDirectory)
            .Should().Be(Path.Combine(baseDirectory, "relative-root"));
    }

    [Fact]
    public void IsDebugBuild_MatchesTheTestBuildConfiguration()
    {
#if DEBUG
        CofferDataRoot.IsDebugBuild.Should().BeTrue("a Debug test run must never resolve the real vault");
#else
        CofferDataRoot.IsDebugBuild.Should().BeFalse();
#endif
    }

    [Fact]
    public void VaultPaths_AndLogs_FollowTheSameRoot()
    {
        var root = CofferDataRoot.Resolve(null, isDebugBuild: true, _localAppData);

        var paths = new CofferVaultPaths(root);

        paths.LocalAppDataFolder.Should().Be(root);
        paths.DatabaseFile.Should().Be(Path.Combine(root, "coffer.db"));
        paths.EncryptedDekFilePath.Should().Be(Path.Combine(root, "dek.encrypted"));
        SerilogConfiguration.GetLogsDirectory(root).Should().Be(Path.Combine(root, "logs"));
    }
}

namespace Coffer.Infrastructure.Security;

/// <summary>
/// Resolves the one folder that holds everything Coffer writes — vault, database, backups, secrets, logs.
/// <list type="number">
/// <item><c>COFFER_DATA_DIR</c>, when set to a non-blank value, wins (made absolute).</item>
/// <item>Otherwise a Debug build uses <c>%LocalAppData%\Coffer.Dev</c> and a Release build
/// <c>%LocalAppData%\Coffer</c> (the platform equivalent of
/// <see cref="Environment.SpecialFolder.LocalApplicationData"/> elsewhere).</item>
/// </list>
/// Splitting by build configuration means a feature branch run from the IDE (with its not-yet-released
/// migrations) can never open the real vault. <see cref="Resolve"/> is pure so the choice is testable;
/// <see cref="Current"/> feeds it the process environment and this assembly's build configuration.
/// </summary>
public static class CofferDataRoot
{
    /// <summary>The environment variable that overrides the data root.</summary>
    public const string OverrideVariable = "COFFER_DATA_DIR";

    /// <summary>The folder name under LocalAppData for Release builds (the real vault).</summary>
    public const string ReleaseFolderName = "Coffer";

    /// <summary>The folder name under LocalAppData for Debug builds (the development vault).</summary>
    public const string DebugFolderName = "Coffer.Dev";

    /// <summary>Whether this assembly was compiled in the Debug configuration.</summary>
    public static bool IsDebugBuild =>
#if DEBUG
        true;
#else
        false;
#endif

    /// <summary>The data root for this process: <see cref="Resolve"/> over the live environment.</summary>
    public static string Current =>
        Resolve(
            Environment.GetEnvironmentVariable(OverrideVariable),
            IsDebugBuild,
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    /// <summary>
    /// Picks the data root: <paramref name="overrideDirectory"/> (made absolute) when it is not blank,
    /// otherwise <paramref name="localAppData"/>\<c>Coffer.Dev</c> for a Debug build or
    /// <paramref name="localAppData"/>\<c>Coffer</c> for a Release build.
    /// </summary>
    public static string Resolve(string? overrideDirectory, bool isDebugBuild, string localAppData)
    {
        if (!string.IsNullOrWhiteSpace(overrideDirectory))
        {
            return Path.GetFullPath(overrideDirectory.Trim());
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(localAppData);
        return Path.Combine(localAppData, isDebugBuild ? DebugFolderName : ReleaseFolderName);
    }
}

using System.Text.Json;
using Coffer.Core.Security;
using Coffer.Core.Theming;

namespace Coffer.Infrastructure.Theming;

/// <summary>
/// <see cref="IThemeStore"/> backed by a small plaintext JSON file in the vault folder
/// (<c>theme.json</c>). Deliberately not in the encrypted DB: the choice is non-sensitive
/// and must be readable on the pre-login screens before the DEK exists. Any read failure
/// falls back to <see cref="AppTheme.Light"/> rather than blocking startup.
/// </summary>
public sealed class FileThemeStore : IThemeStore
{
    private readonly string _filePath;

    public FileThemeStore(IVaultPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _filePath = Path.Combine(paths.LocalAppDataFolder, "theme.json");
    }

    public AppTheme Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return AppTheme.Light;
            }

            var dto = JsonSerializer.Deserialize<ThemeDto>(File.ReadAllText(_filePath));
            return dto is not null && Enum.TryParse<AppTheme>(dto.Theme, out var theme)
                ? theme
                : AppTheme.Light;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return AppTheme.Light;
        }
    }

    public void Save(AppTheme theme)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(_filePath, JsonSerializer.Serialize(new ThemeDto { Theme = theme.ToString() }));
    }

    private sealed class ThemeDto
    {
        public string Theme { get; set; } = "";
    }
}

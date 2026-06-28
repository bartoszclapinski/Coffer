using System.Text.Json;
using Coffer.Core.Localization;
using Coffer.Core.Security;

namespace Coffer.Infrastructure.Localization;

/// <summary>
/// <see cref="ILanguageStore"/> backed by a small plaintext JSON file in the vault folder
/// (<c>language.json</c>). Deliberately not in the encrypted DB: the choice is non-sensitive
/// and must be readable on the pre-login screens before the DEK exists. Any read failure
/// falls back to <see cref="AppLanguage.Polish"/> rather than blocking startup.
/// </summary>
public sealed class FileLanguageStore : ILanguageStore
{
    private readonly string _filePath;

    public FileLanguageStore(IVaultPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _filePath = Path.Combine(paths.LocalAppDataFolder, "language.json");
    }

    public AppLanguage Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return AppLanguage.Polish;
            }

            var dto = JsonSerializer.Deserialize<LanguageDto>(File.ReadAllText(_filePath));
            return dto is not null && Enum.TryParse<AppLanguage>(dto.Language, out var language)
                ? language
                : AppLanguage.Polish;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return AppLanguage.Polish;
        }
    }

    public void Save(AppLanguage language)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(_filePath, JsonSerializer.Serialize(new LanguageDto { Language = language.ToString() }));
    }

    private sealed class LanguageDto
    {
        public string Language { get; set; } = "";
    }
}

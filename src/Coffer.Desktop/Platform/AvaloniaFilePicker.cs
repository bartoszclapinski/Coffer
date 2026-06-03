using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Coffer.Core.Import;
using Microsoft.Extensions.Logging;

namespace Coffer.Desktop.Platform;

/// <summary>
/// Avalonia <see cref="IStorageProvider"/>-backed <see cref="IFilePicker"/>. Opens
/// the OS file-open dialog over the active main window and returns a
/// <see cref="PickedFile"/> whose stream the caller owns. Keeps all Avalonia
/// storage types inside Desktop (hard rule #4).
/// </summary>
public sealed class AvaloniaFilePicker : IFilePicker
{
    private static readonly FilePickerFileType _statementFiles = new("Wyciągi bankowe (PDF, CSV)")
    {
        Patterns = ["*.pdf", "*.csv"],
    };

    private readonly ILogger<AvaloniaFilePicker> _logger;

    public AvaloniaFilePicker(ILogger<AvaloniaFilePicker> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public async Task<PickedFile?> PickStatementFileAsync(CancellationToken ct)
    {
        var window = (Avalonia.Application.Current?.ApplicationLifetime
            as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (window?.StorageProvider is not { CanOpen: true } storage)
        {
            // A null result here means "no pickable surface", not "user cancelled".
            // Log it so that case is distinguishable from a normal cancellation, which
            // returns null silently below.
            _logger.LogWarning(
                "Statement file picker unavailable — no main window or storage provider that can open.");
            return null;
        }

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Wybierz wyciąg bankowy",
            AllowMultiple = false,
            FileTypeFilter = [_statementFiles],
        }).ConfigureAwait(true);

        if (files.Count == 0)
        {
            return null;
        }

        var file = files[0];

        // Copy into memory so the picked file's lifetime is independent of the
        // OS handle — the import reads the stream from position 0 and may re-read it.
        var source = await file.OpenReadAsync().ConfigureAwait(true);
        await using (source.ConfigureAwait(true))
        {
            var buffer = new MemoryStream();
            await source.CopyToAsync(buffer, ct).ConfigureAwait(true);
            buffer.Position = 0;
            return new PickedFile(buffer, file.Name);
        }
    }
}

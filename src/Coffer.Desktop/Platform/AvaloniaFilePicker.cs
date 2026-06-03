using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Coffer.Core.Import;

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

    public async Task<PickedFile?> PickStatementFileAsync(CancellationToken ct)
    {
        var window = (Avalonia.Application.Current?.ApplicationLifetime
            as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (window?.StorageProvider is not { CanOpen: true } storage)
        {
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

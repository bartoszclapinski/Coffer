namespace Coffer.Core.Import;

/// <summary>
/// Opens the platform file-open dialog for choosing a bank-statement file. Sits
/// behind this interface (hard rule #4) so no platform dialog type leaks into
/// <c>Coffer.Core</c> / <c>Coffer.Application</c>; the Avalonia <c>StorageProvider</c>
/// implementation lives in <c>Coffer.Desktop</c>.
/// </summary>
public interface IFilePicker
{
    /// <summary>
    /// Prompts the user to pick a statement file (PDF or CSV). Returns the chosen
    /// file, or <c>null</c> when the user cancels the dialog.
    /// </summary>
    Task<PickedFile?> PickStatementFileAsync(CancellationToken ct);
}

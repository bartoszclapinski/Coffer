namespace Coffer.Core.Import;

/// <summary>
/// Parses a bank statement, deduplicates against already-stored transactions, and
/// persists the new rows plus an <c>ImportSession</c> in a single database
/// transaction. The implementation lives in <c>Coffer.Infrastructure</c> (it needs
/// the EF context, the parser registry, and the description normalizer); this
/// abstraction lets <c>Coffer.Application</c> view models drive it without taking
/// an Infrastructure dependency.
/// </summary>
public interface IImportStatementUseCase
{
    /// <summary>
    /// Runs the import pipeline (read → detect → parse → dedup → save), reporting
    /// each stage through <paramref name="progress"/> when supplied.
    /// </summary>
    /// <exception cref="Coffer.Core.Parsing.UnsupportedBankException">The statement's bank/format has no registered parser.</exception>
    Task<ImportSummary> ExecuteAsync(
        ImportRequest request,
        IProgress<ImportProgress>? progress,
        CancellationToken ct);
}

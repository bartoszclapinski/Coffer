namespace Coffer.Core.Import;

/// <summary>
/// A single progress notification raised by <see cref="IImportStatementUseCase"/>
/// as the pipeline advances through its <see cref="ImportStage"/>s.
/// </summary>
/// <param name="Stage">The stage that has just begun.</param>
public sealed record ImportProgress(ImportStage Stage);

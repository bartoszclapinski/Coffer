using Coffer.Shared.Parsing;

namespace Coffer.Core.Import;

/// <summary>
/// Input to <see cref="IImportStatementUseCase"/>: the statement to parse and the
/// account it belongs to. The account is supplied explicitly because the PKO
/// "Historia rachunku" CSV omits the account number, so the user confirms the
/// target account at import time rather than relying on the file.
/// </summary>
/// <param name="Statement">The statement bytes + format; the use case reads it and does not dispose it.</param>
/// <param name="AccountId">The existing account these transactions are imported into.</param>
public sealed record ImportRequest(StatementInput Statement, Guid AccountId);

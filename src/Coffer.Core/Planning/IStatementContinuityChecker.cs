namespace Coffer.Core.Planning;

/// <summary>
/// Finds gaps between imported statement periods per account. Because the planner seeds its opening
/// balance from the running sum of transactions, a gap silently corrupts that balance — this surfaces
/// the gaps so the owner can be warned to import the missing statements.
/// </summary>
public interface IStatementContinuityChecker
{
    Task<IReadOnlyList<StatementGap>> FindGapsAsync(CancellationToken ct);
}

using System.Text;
using Coffer.Infrastructure.Parsing.Pko;
using Coffer.Shared.Parsing;
using Xunit.Abstractions;

namespace Coffer.Infrastructure.Tests.Parsing.Pko;

/// <summary>
/// Manual verification against a real PKO BP "Historia rachunku" CSV that is kept
/// out of git (<c>tests/.local-fixtures/</c>, gitignored). Skips when the file is
/// absent, so CI and other developers are unaffected. Run it locally and eyeball
/// the printed <see cref="ParseResult"/> — it must never assert on real values
/// (hard rules #5 / #11: nothing real leaks into the repo or test output history).
/// </summary>
public class PkoHistoriaCsvRealStatementManualHarness
{
    private const string _realFileName = "Zestawienie operacji za 01.01.2026 - 31.01.2026.csv";

    private readonly ITestOutputHelper _output;

    public PkoHistoriaCsvRealStatementManualHarness(ITestOutputHelper output)
    {
        _output = output;
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    [SkippableFact]
    public async Task Parse_RealPkoHistoriaCsv_PrintsSummary()
    {
        var path = FindLocalFixture(_realFileName);
        Skip.If(path is null, $"Real CSV not present at tests/.local-fixtures/{_realFileName}; skipping.");

        await using var stream = File.OpenRead(path!);
        var input = new StatementInput(stream, StatementFormat.Csv, Path.GetFileName(path));

        var result = await new PkoHistoriaCsvParser().ParseAsync(input, CancellationToken.None);

        _output.WriteLine($"BankCode:    {result.BankCode}");
        _output.WriteLine($"Currency:    {result.Currency}");
        _output.WriteLine($"Period:      {result.PeriodFrom:yyyy-MM-dd} -> {result.PeriodTo:yyyy-MM-dd}");
        _output.WriteLine($"Confidence:  {result.Confidence}");
        _output.WriteLine($"Tx count:    {result.Transactions.Count}");
        _output.WriteLine($"Credits:     {result.Transactions.Count(t => t.Amount > 0)}");
        _output.WriteLine($"Debits:      {result.Transactions.Count(t => t.Amount < 0)}");
        foreach (var warning in result.Warnings)
        {
            _output.WriteLine($"Warning:     {warning}");
        }
    }

    private static string? FindLocalFixture(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "tests", ".local-fixtures", fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            candidate = Path.Combine(dir.FullName, ".local-fixtures", fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        return null;
    }
}

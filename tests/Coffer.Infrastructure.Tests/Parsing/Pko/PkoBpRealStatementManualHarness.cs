using Coffer.Infrastructure.Parsing.Pko;
using UglyToad.PdfPig;
using Xunit.Abstractions;

namespace Coffer.Infrastructure.Tests.Parsing.Pko;

/// <summary>
/// Manual verification harness for the standard-checking parser. Reads a real
/// PKO BP statement from <c>tests/.local-fixtures/pko-checking.real.pdf</c>
/// (gitignored per hard rule #5) and prints the parsed <c>ParseResult</c> to
/// the test output. <see cref="SkippableFactAttribute"/> skips the test when
/// the fixture file is absent, so CI on machines without the fixture stays
/// green.
/// </summary>
/// <remarks>
/// Invoke locally via:
/// <code>
/// dotnet test --filter "FullyQualifiedName~PkoBpRealStatementManualHarness"
/// </code>
/// Output flows through <see cref="ITestOutputHelper"/>; pair the command with
/// <c>--logger "console;verbosity=detailed"</c> to see it.
/// </remarks>
public class PkoBpRealStatementManualHarness
{
    private readonly ITestOutputHelper _output;

    public PkoBpRealStatementManualHarness(ITestOutputHelper output)
    {
        _output = output;
    }

    [SkippableFact]
    [Trait("Category", "ManualOnly")]
    public async Task Parse_RealPkoCheckingStatement_PrintsResult()
    {
        var path = LocateFixture();
        Skip.If(!File.Exists(path),
            $"Drop a real PKO BP 'Wyciąg z rachunku' PDF at '{path}' to run this manual verification.");

        using var pdf = PdfDocument.Open(path);
        var parser = new PkoBpStatementParser();
        var result = await parser.ParseAsync(pdf, CancellationToken.None);

        _output.WriteLine($"Bank          : {result.BankCode}");
        _output.WriteLine($"Account       : {result.AccountNumber}");
        _output.WriteLine($"Currency      : {result.Currency}");
        _output.WriteLine($"Period        : {result.PeriodFrom:yyyy-MM-dd} → {result.PeriodTo:yyyy-MM-dd}");
        _output.WriteLine($"Confidence    : {result.Confidence}");
        _output.WriteLine($"Warnings      : {string.Join("; ", result.Warnings)}");
        _output.WriteLine($"Transactions  : {result.Transactions.Count}");
        _output.WriteLine(string.Empty);
        foreach (var (tx, index) in result.Transactions.Select((t, i) => (t, i)))
        {
            _output.WriteLine($"[{index,3}] {tx.Date:yyyy-MM-dd} {tx.Amount,12:F2} {tx.Currency} | {tx.Description}");
        }
    }

    private static string LocateFixture()
    {
        // Walk up from the test assembly directory until we find the repo root.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "tests")))
        {
            dir = dir.Parent;
        }
        if (dir is null)
        {
            return string.Empty;
        }
        return Path.Combine(dir.FullName, "tests", ".local-fixtures", "pko-checking.real.pdf");
    }
}

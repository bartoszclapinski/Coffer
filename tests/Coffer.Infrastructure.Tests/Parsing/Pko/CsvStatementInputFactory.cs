using System.Text;
using Coffer.Shared.Parsing;

namespace Coffer.Infrastructure.Tests.Parsing.Pko;

/// <summary>
/// Builds <see cref="StatementInput"/> instances from in-memory CSV text encoded
/// as Windows-1250 (the PKO "Historia rachunku" export encoding), so tests can
/// exercise the parser/detector without a committed binary fixture.
/// </summary>
internal static class CsvStatementInputFactory
{
    private static readonly Encoding _windows1250;

    static CsvStatementInputFactory()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        _windows1250 = Encoding.GetEncoding(1250);
    }

    public static StatementInput FromCsv(string csv, string? fileName = null)
    {
        var bytes = _windows1250.GetBytes(csv);
        return new StatementInput(new MemoryStream(bytes), StatementFormat.Csv, fileName);
    }

    public static StatementInput FromGoldenFile()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory, "Parsing", "Pko", "Fixtures", "pko-historia.golden.csv");
        var bytes = File.ReadAllBytes(path);
        return new StatementInput(new MemoryStream(bytes), StatementFormat.Csv, "pko-historia.golden.csv");
    }
}

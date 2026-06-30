using Coffer.Core.Parsing;
using Coffer.Infrastructure.Parsing;
using Coffer.Infrastructure.Parsing.Pko;
using Coffer.Shared.Parsing;
using FluentAssertions;

namespace Coffer.Infrastructure.Tests.Parsing;

public class StatementParserRegistryTests
{
    private static StatementParserRegistry Registry() =>
        new(new IStatementParser[] { new PkoHistoriaCsvParser() });

    [Fact]
    public void Resolve_PkoFingerprintCsv_ReturnsPkoCsvParser()
    {
        var registry = Registry();
        var fingerprint = new BankFingerprint("PKO_BP", "PKO Bank Polski", 1);

        var parser = registry.Resolve(fingerprint, StatementFormat.Csv);

        parser.BankCode.Should().Be("PKO_BP");
        parser.Format.Should().Be(StatementFormat.Csv);
    }

    [Fact]
    public void Resolve_PkoFingerprintPdf_ThrowsUnsupportedBankException()
    {
        var registry = Registry();
        var fingerprint = new BankFingerprint("PKO_BP", "PKO Bank Polski", 1);

        // No PDF parser is registered — PKO is parsed via CSV only.
        var act = () => registry.Resolve(fingerprint, StatementFormat.Pdf);

        act.Should().Throw<UnsupportedBankException>().Which.BankCode.Should().Be("PKO_BP");
    }

    [Fact]
    public void Resolve_UnknownBank_ThrowsUnsupportedBankException()
    {
        var registry = Registry();
        var fingerprint = new BankFingerprint("MBANK", "mBank S.A.", 1);

        var act = () => registry.Resolve(fingerprint, StatementFormat.Csv);

        act.Should().Throw<UnsupportedBankException>().Which.BankCode.Should().Be("MBANK");
    }

    [Fact]
    public void Resolve_NullFingerprint_ThrowsUnsupportedBankException()
    {
        var registry = Registry();

        var act = () => registry.Resolve(null, StatementFormat.Csv);

        act.Should().Throw<UnsupportedBankException>().Which.BankCode.Should().Be("UNKNOWN");
    }

    [Fact]
    public void Constructor_EmptyParserList_BuildsButResolveAlwaysThrows()
    {
        var registry = new StatementParserRegistry(Array.Empty<IStatementParser>());
        var fingerprint = new BankFingerprint("PKO_BP", "PKO Bank Polski", 1);

        var act = () => registry.Resolve(fingerprint, StatementFormat.Csv);

        act.Should().Throw<UnsupportedBankException>();
    }

    [Fact]
    public void Resolve_UnknownBank_WithFallback_ReturnsFallback()
    {
        var fallback = new StubFallbackParser();
        var registry = new StatementParserRegistry(new IStatementParser[] { new PkoHistoriaCsvParser() }, fallback);
        var fingerprint = new BankFingerprint("MBANK", "mBank S.A.", 1);

        var parser = registry.Resolve(fingerprint, StatementFormat.Csv);

        parser.Should().BeSameAs(fallback);
    }

    [Fact]
    public void Resolve_NullFingerprint_WithFallback_ReturnsFallback()
    {
        var fallback = new StubFallbackParser();
        var registry = new StatementParserRegistry(new IStatementParser[] { new PkoHistoriaCsvParser() }, fallback);

        var parser = registry.Resolve(null, StatementFormat.Pdf);

        parser.Should().BeSameAs(fallback);
    }

    [Fact]
    public void Resolve_KnownBank_WithFallback_PrefersDeterministicParser()
    {
        var fallback = new StubFallbackParser();
        var registry = new StatementParserRegistry(new IStatementParser[] { new PkoHistoriaCsvParser() }, fallback);
        var fingerprint = new BankFingerprint("PKO_BP", "PKO Bank Polski", 1);

        var parser = registry.Resolve(fingerprint, StatementFormat.Csv);

        parser.BankCode.Should().Be("PKO_BP");
        parser.Should().NotBeSameAs(fallback);
    }

    private sealed class StubFallbackParser : IStatementParser
    {
        public string BankCode => "AI_FALLBACK";

        public StatementFormat Format => StatementFormat.Pdf;

        public bool CanHandle(BankFingerprint fingerprint) => true;

        public Task<ParseResult> ParseAsync(StatementInput input, CancellationToken ct) =>
            throw new NotSupportedException();
    }
}

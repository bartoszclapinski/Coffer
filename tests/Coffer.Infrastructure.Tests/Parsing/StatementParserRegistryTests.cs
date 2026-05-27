using Coffer.Core.Parsing;
using Coffer.Infrastructure.Parsing;
using Coffer.Infrastructure.Parsing.Pko;
using FluentAssertions;

namespace Coffer.Infrastructure.Tests.Parsing;

public class StatementParserRegistryTests
{
    [Fact]
    public void Resolve_PkoFingerprint_ReturnsPkoParser()
    {
        var registry = new StatementParserRegistry(new IStatementParser[] { new PkoBpStatementParser() });
        var fingerprint = new BankFingerprint("PKO_BP", "PKO Bank Polski", 1);

        var parser = registry.Resolve(fingerprint);

        parser.BankCode.Should().Be("PKO_BP");
    }

    [Fact]
    public void Resolve_UnknownFingerprint_ThrowsUnsupportedBankException()
    {
        var registry = new StatementParserRegistry(new IStatementParser[] { new PkoBpStatementParser() });
        var fingerprint = new BankFingerprint("MBANK", "mBank S.A.", 1);

        var act = () => registry.Resolve(fingerprint);

        var thrown = act.Should().Throw<UnsupportedBankException>();
        thrown.Which.BankCode.Should().Be("MBANK");
    }

    [Fact]
    public void Resolve_NullFingerprint_ThrowsUnsupportedBankException()
    {
        var registry = new StatementParserRegistry(new IStatementParser[] { new PkoBpStatementParser() });

        var act = () => registry.Resolve(null);

        var thrown = act.Should().Throw<UnsupportedBankException>();
        thrown.Which.BankCode.Should().Be("UNKNOWN");
    }

    [Fact]
    public void Constructor_EmptyParserList_BuildsButResolveAlwaysThrows()
    {
        var registry = new StatementParserRegistry(Array.Empty<IStatementParser>());
        var fingerprint = new BankFingerprint("PKO_BP", "PKO Bank Polski", 1);

        var act = () => registry.Resolve(fingerprint);

        act.Should().Throw<UnsupportedBankException>();
    }
}

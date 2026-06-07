using Coffer.Core.Ai;
using Coffer.Infrastructure.AI;
using Coffer.Infrastructure.Tests.Categorization;
using FluentAssertions;

namespace Coffer.Infrastructure.Tests.AI;

public class AiUsageLedgerTests : CategorizationDbTest
{
    [Fact]
    public async Task RecordAsync_ThenGetCurrentMonthSpend_AccumulatesAcrossCalls()
    {
        await using (await MigratedContextAsync())
        {
        }

        var ledger = new AiUsageLedger(Factory, new AiPricing());
        var usage = new AiUsage(AiDefaults.ClaudeProvider, "claude-haiku-4-5", 1_000_000, 1_000_000);

        await ledger.RecordAsync(usage, AiPurpose.Categorization, CancellationToken.None);
        await ledger.RecordAsync(usage, AiPurpose.Categorization, CancellationToken.None);

        var spend = await ledger.GetCurrentMonthSpendPlnAsync(CancellationToken.None);

        // Each call: (1M*1.00 + 1M*5.00)/1M = 6 USD = 24 PLN; two calls = 48 PLN.
        spend.Should().Be(48.00m);
    }

    [Fact]
    public async Task GetCurrentMonthByPurpose_GroupsSpend()
    {
        await using (await MigratedContextAsync())
        {
        }

        var ledger = new AiUsageLedger(Factory, new AiPricing());
        var usage = new AiUsage(AiDefaults.ClaudeProvider, "claude-haiku-4-5", 1_000_000, 0);

        await ledger.RecordAsync(usage, AiPurpose.Categorization, CancellationToken.None);
        await ledger.RecordAsync(usage, AiPurpose.Chat, CancellationToken.None);

        var byPurpose = await ledger.GetCurrentMonthByPurposeAsync(CancellationToken.None);

        byPurpose.Should().HaveCount(2);
        byPurpose.Should().Contain(p => p.Purpose == AiPurpose.Categorization);
        byPurpose.Should().Contain(p => p.Purpose == AiPurpose.Chat);
    }
}

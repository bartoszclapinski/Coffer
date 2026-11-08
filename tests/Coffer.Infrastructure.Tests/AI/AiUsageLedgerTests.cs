using Coffer.Core.Ai;
using Coffer.Infrastructure.AI;
using Coffer.Infrastructure.Tests.Categorization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

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

    [Fact]
    public async Task RecordAsync_LocalProviderUsage_KeepsTokensAtZeroCost()
    {
        await using (await MigratedContextAsync())
        {
        }

        var ledger = new AiUsageLedger(Factory, new AiPricing());
        var usage = new AiUsage(AiDefaults.OllamaProvider, "qwen2.5:3b", 1_000_000, 1_000_000);

        await ledger.RecordAsync(usage, AiPurpose.Categorization, CancellationToken.None);

        (await ledger.GetCurrentMonthSpendPlnAsync(CancellationToken.None)).Should().Be(0m);
        await using var db = Factory.CreateDbContext();
        var entry = await db.AiUsageEntries.SingleAsync();
        entry.Provider.Should().Be(AiDefaults.OllamaProvider, "local usage is still recorded for visibility");
        entry.InputTokens.Should().Be(1_000_000);
    }
}

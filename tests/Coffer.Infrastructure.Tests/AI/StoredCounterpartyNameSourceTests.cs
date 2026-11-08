using Coffer.Core.Domain;
using Coffer.Infrastructure.AI;
using Coffer.Infrastructure.Tests.Categorization;
using FluentAssertions;

namespace Coffer.Infrastructure.Tests.AI;

public class StoredCounterpartyNameSourceTests : CategorizationDbTest
{
    [Fact]
    public async Task GetKnownCounterpartyNames_ReturnsLabelledNamesOnly()
    {
        await SeedAsync(
            ("Nazwa odbiorcy: JAN TESTOWY Tytuł: Zwrot", "JAN TESTOWY"),
            ("NAZWA NADAWCY/ODBIORCY: ANNA PRZYKLADOWA TYTUL: CZYNSZ", "ANNA PRZYKLADOWA"),
            ("Nazwa i nr identyfikatora: P 44051401458 Nazwa odbiorcy: URZAD SKARBOWY", "URZAD SKARBOWY"),
            ("BIEDRONKA 1234 TORUN", "BIEDRONKA"),
            ("Nazwa odbiorcy: jan testowy Tytuł: Prezent", "jan testowy"));

        var names = await new StoredCounterpartyNameSource(Factory).GetKnownCounterpartyNamesAsync(CancellationToken.None);

        names.Select(n => n.ToUpperInvariant()).Should().BeEquivalentTo(
            ["JAN TESTOWY", "ANNA PRZYKLADOWA", "URZAD SKARBOWY"],
            "only labelled name fields are counterparties; an unlabelled shop merchant is merchant signal");
    }

    [Fact]
    public async Task GetKnownCounterpartyNames_ReadsWholeQuotedNamesAndOtherBanksLabels()
    {
        await SeedAsync(
            ("Nazwa odbiorcy: P.H.U. \"ANEX\" ANNA FIKCYJNA Adres odbiorcy: UL. POLNA 1; 87-100 TORUN Tytuł: FV", "x"),
            ("Odbiorca: PIOTR TESTOWY ul. Polna 1 00-001 TESTOWO Tytuł: Zwrot", "x"),
            ("DANE NADAWCY: MARIA TESTOWA TYTUL: ZWROT", "x"),
            ("Kontrahent: JAN FIKCYJNY", "x"));

        var names = await new StoredCounterpartyNameSource(Factory).GetKnownCounterpartyNamesAsync(CancellationToken.None);

        names.Should().BeEquivalentTo(
            ["P.H.U. \"ANEX\" ANNA FIKCYJNA", "PIOTR TESTOWY", "MARIA TESTOWA", "JAN FIKCYJNY"],
            "a value runs to the next label (not to a quote or semicolon) and the address part is not a name");
    }

    [Fact]
    public async Task GetKnownCounterpartyNames_EmptyDatabase_ReturnsEmpty()
    {
        await (await MigratedContextAsync()).DisposeAsync();

        var names = await new StoredCounterpartyNameSource(Factory).GetKnownCounterpartyNamesAsync(CancellationToken.None);

        names.Should().BeEmpty();
    }

    private async Task SeedAsync(params (string Description, string Merchant)[] rows)
    {
        await using var db = await MigratedContextAsync();
        var account = new Account
        {
            Id = Guid.NewGuid(),
            Name = "Konto testowe",
            BankCode = "PKO_BP",
            AccountNumber = "PL00TEST",
            Currency = "PLN",
            Type = AccountType.Checking,
            CreatedAt = DateTime.UtcNow,
        };
        var session = new ImportSession
        {
            Id = Guid.NewGuid(),
            FileName = "synthetic.csv",
            FileHash = "SYNTHETIC",
            BankCode = "PKO_BP",
            PeriodFrom = new DateOnly(2026, 1, 1),
            PeriodTo = new DateOnly(2026, 1, 31),
            ImportedAt = DateTime.UtcNow,
            Status = ImportStatus.Completed,
        };
        db.Accounts.Add(account);
        db.ImportSessions.Add(session);
        foreach (var (description, merchant) in rows)
        {
            db.Transactions.Add(new Transaction
            {
                Id = Guid.NewGuid(),
                AccountId = account.Id,
                ImportSessionId = session.Id,
                Date = new DateOnly(2026, 1, 10),
                Amount = -10m,
                Currency = "PLN",
                Description = description,
                NormalizedDescription = description.ToUpperInvariant(),
                Merchant = merchant,
                Hash = Guid.NewGuid().ToString("N"),
                CreatedAt = DateTime.UtcNow,
            });
        }

        await db.SaveChangesAsync();
    }
}

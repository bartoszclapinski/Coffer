using Coffer.Application.Tests.Fakes;
using Coffer.Application.ViewModels.Import;
using Coffer.Core.Domain;
using Coffer.Core.Import;
using Coffer.Core.Parsing;
using Coffer.Core.Transactions;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Coffer.Application.Tests.ViewModels.Import;

public class ImportViewModelTests
{
    private static readonly AccountListItem _account = new(Guid.NewGuid(), "Konto PKO", "PKO_BP");

    private static PickedFile NewCsv(string name = "wyciag.csv") =>
        new(new MemoryStream([1, 2, 3]), name);

    private static ImportViewModel Create(
        out FakeFilePicker picker,
        out FakeImportStatementUseCase useCase,
        out FakeAccountService accounts,
        PickedFile? picked = null,
        ILogger<ImportViewModel>? logger = null,
        params AccountListItem[] seedAccounts)
    {
        picker = new FakeFilePicker(picked);
        useCase = new FakeImportStatementUseCase();
        accounts = new FakeAccountService(seedAccounts);
        return new ImportViewModel(picker, useCase, accounts, logger ?? NullLogger<ImportViewModel>.Instance);
    }

    [Fact]
    public async Task LoadAccounts_WithExisting_SelectsFirstAndDoesNotForceCreate()
    {
        var vm = Create(out _, out _, out _, seedAccounts: _account);

        await vm.LoadAccountsCommand.ExecuteAsync(null);

        vm.Accounts.Should().ContainSingle();
        vm.SelectedAccount.Should().Be(_account);
        vm.IsCreatingNewAccount.Should().BeFalse();
    }

    [Fact]
    public async Task LoadAccounts_WhenEmpty_DefaultsToCreateMode()
    {
        var vm = Create(out _, out _, out _);

        await vm.LoadAccountsCommand.ExecuteAsync(null);

        vm.Accounts.Should().BeEmpty();
        vm.IsCreatingNewAccount.Should().BeTrue();
        vm.SelectedAccount.Should().BeNull();
    }

    [Fact]
    public async Task Browse_SetsPickedFileName()
    {
        var vm = Create(out _, out _, out _, picked: NewCsv());

        await vm.BrowseCommand.ExecuteAsync(null);

        vm.HasPickedFile.Should().BeTrue();
        vm.PickedFileName.Should().Be("wyciag.csv");
    }

    [Fact]
    public async Task Import_HappyPath_PopulatesSummaryAndResetsState()
    {
        var vm = Create(out _, out var useCase, out _, picked: NewCsv(), seedAccounts: _account);
        await vm.LoadAccountsCommand.ExecuteAsync(null);
        await vm.BrowseCommand.ExecuteAsync(null);
        useCase.Result = new ImportSummary(Guid.NewGuid(), 8, 2, 5, false, ["Account number missing"]);

        await vm.ImportCommand.ExecuteAsync(null);

        useCase.Calls.Should().Be(1);
        useCase.LastAccountId.Should().Be(_account.Id);
        vm.HasSummary.Should().BeTrue();
        vm.SummaryAdded.Should().Be(8);
        vm.SummarySkipped.Should().Be(2);
        vm.Warnings.Should().ContainSingle();
        vm.IsImporting.Should().BeFalse();
        vm.CurrentStage.Should().BeNull();
        vm.HasPickedFile.Should().BeFalse("the picked file is consumed once imported");
        vm.ErrorMessage.Should().BeEmpty();
    }

    [Fact]
    public async Task Import_WhileRunning_ReportsImportingThenClears()
    {
        var vm = Create(out _, out var useCase, out _, picked: NewCsv(), seedAccounts: _account);
        await vm.LoadAccountsCommand.ExecuteAsync(null);
        await vm.BrowseCommand.ExecuteAsync(null);
        useCase.Gate = new TaskCompletionSource();

        var importing = vm.ImportCommand.ExecuteAsync(null);

        vm.IsImporting.Should().BeTrue();

        useCase.Gate.SetResult();
        await importing;

        vm.IsImporting.Should().BeFalse();
        vm.HasSummary.Should().BeTrue();
    }

    [Fact]
    public async Task Import_CreatingNewAccount_CreatesThenImportsIntoIt()
    {
        var vm = Create(out _, out var useCase, out var accounts, picked: NewCsv());
        await vm.LoadAccountsCommand.ExecuteAsync(null);
        await vm.BrowseCommand.ExecuteAsync(null);
        vm.IsCreatingNewAccount = true;
        vm.NewAccountName = "Nowe konto";
        vm.NewAccountNumber = "PL61109010140000071219812874";
        vm.NewAccountType = AccountType.Checking;

        await vm.ImportCommand.ExecuteAsync(null);

        accounts.CreateCalls.Should().Be(1);
        accounts.LastCreated!.Name.Should().Be("Nowe konto");
        useCase.Calls.Should().Be(1);
        useCase.LastAccountId.Should().NotBe(Guid.Empty);
        vm.IsCreatingNewAccount.Should().BeFalse("the inline form closes once the account is created");
        vm.HasSummary.Should().BeTrue();
    }

    [Fact]
    public async Task Import_CreatingNewAccountWithBlankCurrency_DoesNotImport()
    {
        var vm = Create(out _, out var useCase, out var accounts, picked: NewCsv());
        await vm.LoadAccountsCommand.ExecuteAsync(null);
        await vm.BrowseCommand.ExecuteAsync(null);
        vm.IsCreatingNewAccount = true;
        vm.NewAccountName = "Nowe konto";
        vm.NewAccountNumber = "PL61109010140000071219812874";
        vm.NewAccountCurrency = "  ";

        await vm.ImportCommand.ExecuteAsync(null);

        accounts.CreateCalls.Should().Be(0, "Currency is non-null on every monetary entity (rule #9)");
        useCase.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Import_UnsupportedBank_ShowsPolishMessage()
    {
        var vm = Create(out _, out var useCase, out _, picked: NewCsv(), seedAccounts: _account);
        await vm.LoadAccountsCommand.ExecuteAsync(null);
        await vm.BrowseCommand.ExecuteAsync(null);
        useCase.Throw = new UnsupportedBankException("MBANK");

        await vm.ImportCommand.ExecuteAsync(null);

        vm.HasSummary.Should().BeFalse();
        vm.ErrorMessage.Should().Contain("banku");
        vm.IsImporting.Should().BeFalse();
    }

    [Fact]
    public async Task Import_UnsupportedExtension_ShowsFormatMessageWithoutCallingUseCase()
    {
        var vm = Create(out _, out var useCase, out _, picked: NewCsv("statement.txt"), seedAccounts: _account);
        await vm.LoadAccountsCommand.ExecuteAsync(null);
        await vm.BrowseCommand.ExecuteAsync(null);

        await vm.ImportCommand.ExecuteAsync(null);

        useCase.Calls.Should().Be(0);
        vm.ErrorMessage.Should().Contain("format");
    }

    [Fact]
    public async Task Import_ParserFailure_DoesNotLeakRawCellToUiOrLog()
    {
        var logger = new CapturingLogger<ImportViewModel>();
        var vm = Create(out _, out var useCase, out _, picked: NewCsv(), logger: logger, seedAccounts: _account);
        await vm.LoadAccountsCommand.ExecuteAsync(null);
        await vm.BrowseCommand.ExecuteAsync(null);

        // Mirrors PolishAmountParser/PolishDateParser, which embed the raw statement cell
        // in the exception message. Importing must never surface it to the UI or the log.
        useCase.Throw = new FormatException("Cannot parse Polish amount: '1 234,56 PLN'.");

        await vm.ImportCommand.ExecuteAsync(null);

        vm.ErrorMessage.Should().NotContain("1 234,56");
        vm.ErrorMessage.Should().NotBeEmpty();
        vm.HasSummary.Should().BeFalse();
        logger.Entries.Should().NotBeEmpty("the failure should still be logged for diagnostics");
        logger.Entries.Should().NotContain(e => e.Contains("1 234,56"),
            "rules #6/#7 — raw statement content must never reach the log");
    }
}

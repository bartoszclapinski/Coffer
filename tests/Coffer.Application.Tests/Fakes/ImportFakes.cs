using Coffer.Core.Accounts;
using Coffer.Core.Import;
using Coffer.Core.Transactions;

namespace Coffer.Application.Tests.Fakes;

/// <summary>Returns a pre-set <see cref="PickedFile"/> (or null) and counts calls.</summary>
internal sealed class FakeFilePicker : IFilePicker
{
    private readonly PickedFile? _result;

    public FakeFilePicker(PickedFile? result = null) => _result = result;

    public int Calls { get; private set; }

    public Task<PickedFile?> PickStatementFileAsync(CancellationToken ct)
    {
        Calls++;
        return Task.FromResult(_result);
    }
}

/// <summary>
/// Configurable import use case. By default returns a summary; can be made to gate on
/// a <see cref="TaskCompletionSource"/> (to observe the running state) or throw.
/// </summary>
internal sealed class FakeImportStatementUseCase : IImportStatementUseCase
{
    public ImportSummary Result { get; set; } =
        new(Guid.NewGuid(), 8, 0, false, []);

    public Exception? Throw { get; set; }

    public TaskCompletionSource? Gate { get; set; }

    public int Calls { get; private set; }

    public Guid LastAccountId { get; private set; }

    public async Task<ImportSummary> ExecuteAsync(
        ImportRequest request,
        IProgress<ImportProgress>? progress,
        CancellationToken ct)
    {
        Calls++;
        LastAccountId = request.AccountId;

        if (Gate is not null)
        {
            await Gate.Task.ConfigureAwait(false);
        }

        if (Throw is not null)
        {
            throw Throw;
        }

        progress?.Report(new ImportProgress(ImportStage.Saving));
        return Result;
    }
}

/// <summary>In-memory account service: serves a seeded list and records inline creates.</summary>
internal sealed class FakeAccountService : IAccountService
{
    private readonly List<AccountListItem> _accounts;

    public FakeAccountService(params AccountListItem[] accounts) => _accounts = [.. accounts];

    public int CreateCalls { get; private set; }

    public NewAccount? LastCreated { get; private set; }

    public Task<IReadOnlyList<AccountListItem>> GetAllAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<AccountListItem>>([.. _accounts]);

    public Task<Guid> CreateAsync(NewAccount account, CancellationToken ct)
    {
        CreateCalls++;
        LastCreated = account;
        var id = Guid.NewGuid();
        _accounts.Add(new AccountListItem(id, account.Name, account.BankCode));
        return Task.FromResult(id);
    }
}

/// <summary>Returns a fixed transaction list and account list.</summary>
internal sealed class FakeGetTransactionsQuery : IGetTransactionsQuery
{
    private readonly IReadOnlyList<TransactionListItem> _items;
    private readonly IReadOnlyList<AccountListItem> _accounts;

    public FakeGetTransactionsQuery(
        IReadOnlyList<TransactionListItem>? items = null,
        IReadOnlyList<AccountListItem>? accounts = null)
    {
        _items = items ?? [];
        _accounts = accounts ?? [];
    }

    public Task<IReadOnlyList<TransactionListItem>> ExecuteAsync(TransactionQueryFilter filter, CancellationToken ct) =>
        Task.FromResult(_items);

    public Task<IReadOnlyList<AccountListItem>> GetAccountsAsync(CancellationToken ct) =>
        Task.FromResult(_accounts);
}

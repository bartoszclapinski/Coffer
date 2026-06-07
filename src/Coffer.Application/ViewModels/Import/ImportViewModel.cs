using System.Collections.ObjectModel;
using Coffer.Core.Accounts;
using Coffer.Core.Domain;
using Coffer.Core.Import;
using Coffer.Core.Parsing;
using Coffer.Core.Transactions;
using Coffer.Shared.Parsing;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Coffer.Application.ViewModels.Import;

/// <summary>
/// View-model behind the Import page. Drives the three-step flow: pick the target
/// account (existing or inline-created), browse for a statement file, then run
/// <see cref="IImportStatementUseCase"/> while surfacing per-stage progress and a
/// final summary. Parser/bank failures are translated into Polish UI messages that
/// never leak statement row content.
/// </summary>
public sealed partial class ImportViewModel : ObservableObject
{
    private const string _unsupportedBankMessage =
        "Nie rozpoznano banku tego wyciągu. Obsługiwany jest obecnie PKO BP (CSV „Historia rachunku”).";
    private const string _unsupportedFormatMessage =
        "Nieobsługiwany format pliku. Wybierz plik PDF lub CSV.";
    private const string _parseFailureMessage =
        "Nie udało się odczytać wyciągu. Sprawdź, czy plik nie jest uszkodzony i czy to wyciąg z obsługiwanego banku.";
    private const string _noAccountMessage =
        "Wybierz konto lub utwórz nowe przed importem.";
    private const string _newAccountIncompleteMessage =
        "Podaj nazwę, numer i walutę nowego konta.";
    private const string _genericFailureMessage =
        "Import nie powiódł się. Spróbuj ponownie.";

    private readonly IFilePicker _filePicker;
    private readonly IImportStatementUseCase _importUseCase;
    private readonly IAccountService _accountService;
    private readonly ILogger<ImportViewModel> _logger;

    private PickedFile? _pickedFile;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    private AccountListItem? _selectedAccount;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    private bool _isCreatingNewAccount;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    private string _newAccountName = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    private string _newAccountNumber = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    private string _newAccountCurrency = "PLN";

    [ObservableProperty]
    private string _newAccountBankCode = "PKO_BP";

    [ObservableProperty]
    private AccountType _newAccountType = AccountType.Checking;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    [NotifyPropertyChangedFor(nameof(HasPickedFile))]
    private string _pickedFileName = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    [NotifyPropertyChangedFor(nameof(StageLabel))]
    private ImportStage? _currentStage;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    private bool _isImporting;

    [ObservableProperty]
    private bool _hasSummary;

    [ObservableProperty]
    private int _summaryAdded;

    [ObservableProperty]
    private int _summarySkipped;

    [ObservableProperty]
    private bool _summaryAlreadyImported;

    [ObservableProperty]
    private string _errorMessage = "";

    public ImportViewModel(
        IFilePicker filePicker,
        IImportStatementUseCase importUseCase,
        IAccountService accountService,
        ILogger<ImportViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(filePicker);
        ArgumentNullException.ThrowIfNull(importUseCase);
        ArgumentNullException.ThrowIfNull(accountService);
        ArgumentNullException.ThrowIfNull(logger);

        _filePicker = filePicker;
        _importUseCase = importUseCase;
        _accountService = accountService;
        _logger = logger;
    }

    public ObservableCollection<AccountListItem> Accounts { get; } = [];

    public ObservableCollection<string> Warnings { get; } = [];

    public IReadOnlyList<AccountType> AccountTypes { get; } = Enum.GetValues<AccountType>();

    public bool HasPickedFile => !string.IsNullOrEmpty(PickedFileName);

    public string StageLabel => CurrentStage switch
    {
        ImportStage.ReadingFile => "Odczytywanie pliku…",
        ImportStage.DetectingBank => "Rozpoznawanie banku…",
        ImportStage.Parsing => "Przetwarzanie wyciągu…",
        ImportStage.Deduplicating => "Sprawdzanie duplikatów…",
        ImportStage.Categorizing => "Kategoryzowanie transakcji…",
        ImportStage.Saving => "Zapisywanie transakcji…",
        _ => "",
    };

    private bool CanImport =>
        !IsImporting
        && HasPickedFile
        && (IsCreatingNewAccount
            ? !string.IsNullOrWhiteSpace(NewAccountName)
                && !string.IsNullOrWhiteSpace(NewAccountNumber)
                && !string.IsNullOrWhiteSpace(NewAccountCurrency)
            : SelectedAccount is not null);

    [RelayCommand]
    private async Task LoadAccountsAsync(CancellationToken ct)
    {
        try
        {
            var accounts = await _accountService.GetAllAsync(ct).ConfigureAwait(true);
            Accounts.Clear();
            foreach (var account in accounts)
            {
                Accounts.Add(account);
            }

            IsCreatingNewAccount = Accounts.Count == 0;
            SelectedAccount = Accounts.Count > 0 ? Accounts[0] : null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load accounts for the import screen");
            ErrorMessage = _genericFailureMessage;
        }
    }

    [RelayCommand]
    private async Task BrowseAsync(CancellationToken ct)
    {
        ErrorMessage = "";
        try
        {
            var picked = await _filePicker.PickStatementFileAsync(ct).ConfigureAwait(true);
            if (picked is null)
            {
                return;
            }

            DiscardPickedFile();
            _pickedFile = picked;
            PickedFileName = picked.FileName;
            ResetSummary();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "File picker failed");
            ErrorMessage = _genericFailureMessage;
        }
    }

    [RelayCommand(CanExecute = nameof(CanImport))]
    private async Task ImportAsync(CancellationToken ct)
    {
        if (_pickedFile is null)
        {
            return;
        }

        if (!TryResolveFormat(_pickedFile.FileName, out var format))
        {
            ErrorMessage = _unsupportedFormatMessage;
            return;
        }

        IsImporting = true;
        ErrorMessage = "";
        ResetSummary();
        try
        {
            Guid accountId;
            if (IsCreatingNewAccount)
            {
                if (string.IsNullOrWhiteSpace(NewAccountName)
                    || string.IsNullOrWhiteSpace(NewAccountNumber)
                    || string.IsNullOrWhiteSpace(NewAccountCurrency))
                {
                    ErrorMessage = _newAccountIncompleteMessage;
                    return;
                }

                accountId = await _accountService.CreateAsync(
                    new NewAccount(
                        NewAccountName.Trim(),
                        NewAccountBankCode,
                        NewAccountNumber.Trim(),
                        NewAccountCurrency.Trim().ToUpperInvariant(),
                        NewAccountType),
                    ct).ConfigureAwait(true);

                await LoadAccountsAsync(ct).ConfigureAwait(true);
                SelectedAccount = Accounts.FirstOrDefault(a => a.Id == accountId);
                IsCreatingNewAccount = false;
            }
            else if (SelectedAccount is { } account)
            {
                accountId = account.Id;
            }
            else
            {
                ErrorMessage = _noAccountMessage;
                return;
            }

            _pickedFile.Content.Position = 0;
            var statement = new StatementInput(_pickedFile.Content, format, _pickedFile.FileName);
            var progress = new Progress<ImportProgress>(p => CurrentStage = p.Stage);

            var summary = await _importUseCase
                .ExecuteAsync(new ImportRequest(statement, accountId), progress, ct)
                .ConfigureAwait(true);

            SummaryAdded = summary.Added;
            SummarySkipped = summary.Skipped;
            SummaryAlreadyImported = summary.AlreadyImported;
            Warnings.Clear();
            foreach (var warning in summary.Warnings)
            {
                Warnings.Add(warning);
            }

            HasSummary = true;
            DiscardPickedFile();
            PickedFileName = "";
        }
        catch (UnsupportedBankException ex)
        {
            _logger.LogWarning(ex, "Import rejected — unsupported bank {BankCode}", ex.BankCode);
            ErrorMessage = _unsupportedBankMessage;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Never pass the exception object to the logger: parser failures embed the
            // raw statement cell (e.g. FormatException "Cannot parse Polish amount: '1 234,56'")
            // in Message, and logging the exception would leak it (rules #6/#7). Log only the
            // type and stack trace — enough to locate the throw without the offending value.
            _logger.LogError(
                "Statement import failed ({ExceptionType})\n{StackTrace}",
                ex.GetType().FullName,
                ex.StackTrace);
            ErrorMessage = ex is FormatException or InvalidDataException
                ? _parseFailureMessage
                : _genericFailureMessage;
        }
        finally
        {
            IsImporting = false;
            CurrentStage = null;
        }
    }

    private static bool TryResolveFormat(string fileName, out StatementFormat format)
    {
        var extension = Path.GetExtension(fileName);
        if (string.Equals(extension, ".csv", StringComparison.OrdinalIgnoreCase))
        {
            format = StatementFormat.Csv;
            return true;
        }

        if (string.Equals(extension, ".pdf", StringComparison.OrdinalIgnoreCase))
        {
            format = StatementFormat.Pdf;
            return true;
        }

        format = default;
        return false;
    }

    private void ResetSummary()
    {
        HasSummary = false;
        SummaryAdded = 0;
        SummarySkipped = 0;
        SummaryAlreadyImported = false;
        Warnings.Clear();
    }

    private void DiscardPickedFile()
    {
        _pickedFile?.Content.Dispose();
        _pickedFile = null;
    }
}

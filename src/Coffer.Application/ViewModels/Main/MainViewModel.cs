using System.Reflection;
using Coffer.Application.ViewModels.Import;
using Coffer.Application.ViewModels.Transactions;
using Coffer.Core.Security;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Coffer.Application.ViewModels.Main;

/// <summary>
/// Shell view-model behind the post-login <c>MainWindow</c>. Hosts the sidebar
/// navigation and swaps <see cref="CurrentPage"/> between the section view-models
/// (Import, Transactions). Still owns the logout command and <see cref="LoggedOut"/>
/// event that <c>App.axaml.cs</c> subscribes to in order to swap windows.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly ILoginService _loginService;
    private readonly ILogger<MainViewModel> _logger;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsImportActive))]
    [NotifyPropertyChangedFor(nameof(IsTransactionsActive))]
    private ObservableObject? _currentPage;

    public MainViewModel(
        ImportViewModel importViewModel,
        TransactionsViewModel transactionsViewModel,
        ILoginService loginService,
        ILogger<MainViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(importViewModel);
        ArgumentNullException.ThrowIfNull(transactionsViewModel);
        ArgumentNullException.ThrowIfNull(loginService);
        ArgumentNullException.ThrowIfNull(logger);

        Import = importViewModel;
        Transactions = transactionsViewModel;
        _loginService = loginService;
        _logger = logger;
        AppVersion = ResolveAppVersion();

        CurrentPage = Import;
        Import.LoadAccountsCommand.Execute(null);
    }

    public string AppVersion { get; }

    public ImportViewModel Import { get; }

    public TransactionsViewModel Transactions { get; }

    public bool IsImportActive => ReferenceEquals(CurrentPage, Import);

    public bool IsTransactionsActive => ReferenceEquals(CurrentPage, Transactions);

    public event EventHandler? LoggedOut;

    [RelayCommand]
    private void ShowImport()
    {
        if (IsImportActive)
        {
            return;
        }

        CurrentPage = Import;
        Import.LoadAccountsCommand.Execute(null);
    }

    [RelayCommand]
    private void ShowTransactions()
    {
        if (IsTransactionsActive)
        {
            return;
        }

        CurrentPage = Transactions;
        Transactions.LoadCommand.Execute(null);
    }

    [RelayCommand]
    private async Task LogoutAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _loginService
                .LogoutAsync(CancellationToken.None)
                .ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            // LogoutAsync inside LoginService is already defensive; if it still throws
            // we log here and still raise LoggedOut — leaving the user stuck on the
            // MainWindow is a worse outcome than navigating away despite the failure.
            _logger.LogError(ex, "Logout failed; still raising LoggedOut event");
        }
        finally
        {
            IsBusy = false;
            LoggedOut?.Invoke(this, EventArgs.Empty);
        }
    }

    private static string ResolveAppVersion()
    {
        var entry = Assembly.GetEntryAssembly();
        var informational = entry?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            return informational;
        }
        var version = entry?.GetName().Version?.ToString();
        return string.IsNullOrWhiteSpace(version) ? "unknown" : version;
    }
}

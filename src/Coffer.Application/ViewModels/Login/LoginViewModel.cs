using Coffer.Core.Security;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Coffer.Application.ViewModels.Login;

/// <summary>
/// View-model behind <c>LoginWindow</c>. Owns a single password property, busy
/// state, and an error message. <see cref="LoginCommand"/> drives
/// <see cref="ILoginService.LoginWithPasswordAsync"/> and translates the typed
/// exceptions into specific Polish UI messages.
/// </summary>
public sealed partial class LoginViewModel : ObservableObject
{
    private const string _wrongPasswordMessage = "Nieprawidłowe hasło.";
    private const string _vaultCorruptedFormatMessage =
        "Plik sejfu jest uszkodzony. W Sprincie 7 pojawi się odzyskiwanie z frazy BIP39.";
    private const string _vaultCorruptedIoMessage =
        "Nie udało się odczytać pliku sejfu. Sprawdź czy folder %LocalAppData%\\Coffer\\ jest dostępny.";
    private const string _vaultMissingMessage =
        "Brak pliku sejfu. Uruchom aplikację ponownie.";
    private const string _genericFailureMessage =
        "Nie udało się zalogować. Spróbuj ponownie.";

    private readonly ILoginService _loginService;
    private readonly ILogger<LoginViewModel> _logger;

    [ObservableProperty]
    private string _password = "";

    [ObservableProperty]
    private string _errorMessage = "";

    [ObservableProperty]
    private bool _isBusy;

    public LoginViewModel(ILoginService loginService, ILogger<LoginViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(loginService);
        ArgumentNullException.ThrowIfNull(logger);

        _loginService = loginService;
        _logger = logger;
    }

    public event EventHandler<LoginCompletedEventArgs>? LoginCompleted;

    [RelayCommand]
    private async Task LoginAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = "";
        try
        {
            await _loginService
                .LoginWithPasswordAsync(Password, CancellationToken.None)
                .ConfigureAwait(true);
            LoginCompleted?.Invoke(this, LoginCompletedEventArgs.Instance);
        }
        catch (InvalidMasterPasswordException)
        {
            ErrorMessage = _wrongPasswordMessage;
            Password = "";
        }
        catch (VaultCorruptedException ex)
        {
            ErrorMessage = ex.Reason switch
            {
                VaultCorruptionReason.DekFileFormat => _vaultCorruptedFormatMessage,
                VaultCorruptionReason.DekFileIo => _vaultCorruptedIoMessage,
                _ => _genericFailureMessage,
            };
            _logger.LogWarning(ex, "Login failed — vault corrupted ({Reason})", ex.Reason);
        }
        catch (VaultMissingException ex)
        {
            ErrorMessage = _vaultMissingMessage;
            _logger.LogWarning(ex, "Login attempted with no DEK file present");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during login");
            ErrorMessage = _genericFailureMessage;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void ClearSensitive()
    {
        Password = "";
    }
}

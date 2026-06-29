using Coffer.Application.Localization;
using Coffer.Core.Security;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Coffer.Application.ViewModels.Login;

/// <summary>
/// View-model behind <c>LoginWindow</c>. Owns a single password property, busy
/// state, and an error message. <see cref="LoginCommand"/> drives
/// <see cref="ILoginService.LoginWithPasswordAsync"/> and translates the typed
/// exceptions into specific localized UI messages.
/// </summary>
public sealed partial class LoginViewModel : ObservableObject
{
    private readonly ILoginService _loginService;
    private readonly ILocalizer _localizer;
    private readonly ILogger<LoginViewModel> _logger;

    [ObservableProperty]
    private string _password = "";

    [ObservableProperty]
    private string _errorMessage = "";

    [ObservableProperty]
    private bool _isBusy;

    public LoginViewModel(ILoginService loginService, ILocalizer localizer, ILogger<LoginViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(loginService);
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(logger);

        _loginService = loginService;
        _localizer = localizer;
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
            ErrorMessage = _localizer["Login.Error.WrongPassword"];
            Password = "";
        }
        catch (VaultCorruptedException ex)
        {
            ErrorMessage = ex.Reason switch
            {
                VaultCorruptionReason.DekFileFormat => _localizer["Login.Error.VaultCorruptedFormat"],
                VaultCorruptionReason.DekFileIo => _localizer["Login.Error.VaultCorruptedIo"],
                _ => _localizer["Login.Error.Generic"],
            };
            _logger.LogWarning(ex, "Login failed — vault corrupted ({Reason})", ex.Reason);
        }
        catch (VaultMissingException ex)
        {
            ErrorMessage = _localizer["Login.Error.VaultMissing"];
            _logger.LogWarning(ex, "Login attempted with no DEK file present");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during login");
            ErrorMessage = _localizer["Login.Error.Generic"];
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

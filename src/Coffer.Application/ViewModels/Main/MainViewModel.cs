using System.Reflection;
using Coffer.Core.Security;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Coffer.Application.ViewModels.Main;

/// <summary>
/// View-model behind the post-login <c>MainWindow</c>. Sprint 6 surface is
/// intentionally narrow: app version, a busy flag during the logout call, and a
/// command that drives <see cref="ILoginService.LogoutAsync"/> + raises
/// <see cref="LoggedOut"/>. App.axaml.cs subscribes to swap windows.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly ILoginService _loginService;
    private readonly ILogger<MainViewModel> _logger;

    [ObservableProperty]
    private bool _isBusy;

    public MainViewModel(ILoginService loginService, ILogger<MainViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(loginService);
        ArgumentNullException.ThrowIfNull(logger);

        _loginService = loginService;
        _logger = logger;
        AppVersion = ResolveAppVersion();
    }

    public string AppVersion { get; }

    public event EventHandler? LoggedOut;

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

using Coffer.Core.Security;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Coffer.Application.ViewModels.Setup;

public sealed partial class SetupWizardViewModel : ObservableObject
{
    private readonly ISetupService _setupService;
    private readonly ISeedManager _seedManager;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentStepViewModel))]
    private SetupStep _currentStep = SetupStep.Welcome;

    [ObservableProperty]
    private string _mnemonic = "";

    public SetupWizardViewModel(
        ISetupService setupService,
        ISeedManager seedManager,
        IPasswordStrengthChecker strengthChecker)
    {
        ArgumentNullException.ThrowIfNull(setupService);
        ArgumentNullException.ThrowIfNull(seedManager);
        ArgumentNullException.ThrowIfNull(strengthChecker);

        _setupService = setupService;
        _seedManager = seedManager;

        Welcome = new WelcomeStepViewModel();
        Password = new MasterPasswordStepViewModel(strengthChecker, () => Mnemonic);
        SeedDisplay = new BipSeedDisplayStepViewModel();
        Verification = new BipSeedVerificationStepViewModel(() => Mnemonic);
        Confirm = new ConfirmStepViewModel(CompleteSetupAsync);
    }

    public WelcomeStepViewModel Welcome { get; }

    public MasterPasswordStepViewModel Password { get; }

    public BipSeedDisplayStepViewModel SeedDisplay { get; }

    public BipSeedVerificationStepViewModel Verification { get; }

    public ConfirmStepViewModel Confirm { get; }

    public bool IsBusy => Confirm.IsBusy;

    public ObservableObject CurrentStepViewModel => CurrentStep switch
    {
        SetupStep.Welcome => Welcome,
        SetupStep.Password => Password,
        SetupStep.SeedDisplay => SeedDisplay,
        SetupStep.Verification => Verification,
        SetupStep.Confirm => Confirm,
        _ => Welcome,
    };

    public event EventHandler<SetupCompletedEventArgs>? SetupCompleted;

    [RelayCommand]
    private void Next()
    {
        switch (CurrentStep)
        {
            case SetupStep.Welcome:
                Mnemonic = _seedManager.GenerateMnemonic();
                SeedDisplay.SetMnemonic(Mnemonic);
                CurrentStep = SetupStep.Password;
                break;

            case SetupStep.Password:
                if (Password.IsValid)
                {
                    CurrentStep = SetupStep.SeedDisplay;
                }
                break;

            case SetupStep.SeedDisplay:
                CurrentStep = SetupStep.Verification;
                break;

            case SetupStep.Verification:
                if (Verification.IsValid)
                {
                    CurrentStep = SetupStep.Confirm;
                }
                break;

            case SetupStep.Confirm:
                // Final step is committed via Confirm.CreateVaultCommand, not Next.
                break;
        }
    }

    [RelayCommand]
    private void Back()
    {
        if (CurrentStep > SetupStep.Welcome)
        {
            CurrentStep--;
        }
    }

    private async Task CompleteSetupAsync()
    {
        try
        {
            // ConfigureAwait(true): SetupCompleted drives window construction in App, which
            // must run on the UI thread. Argon2 derivation runs via Task.Run, so without this
            // the continuation (and the event) would stay on a thread-pool thread and Avalonia
            // would reject the off-thread window build. Mirrors LoginViewModel.
            await _setupService
                .CompleteSetupAsync(Password.Password, Mnemonic, CancellationToken.None)
                .ConfigureAwait(true);
            OnSetupCompleted(success: true, error: null);
        }
        catch (Exception ex)
        {
            OnSetupCompleted(success: false, error: ex);
            throw;
        }
    }

    private void OnSetupCompleted(bool success, Exception? error) =>
        SetupCompleted?.Invoke(this, new SetupCompletedEventArgs(success, error));

    public void ClearSensitive()
    {
        Mnemonic = "";
        Welcome.ClearSensitive();
        Password.ClearSensitive();
        SeedDisplay.ClearSensitive();
        Verification.ClearSensitive();
        Confirm.ClearSensitive();
    }
}

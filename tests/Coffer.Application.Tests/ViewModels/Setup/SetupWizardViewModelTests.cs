using Coffer.Application.ViewModels.Setup;
using Coffer.Core.Security;
using FluentAssertions;

namespace Coffer.Application.Tests.ViewModels.Setup;

public class SetupWizardViewModelTests
{
    private const string _fakeMnemonic =
        "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about";

    [Fact]
    public void Next_FromWelcome_GeneratesMnemonicAndAdvancesToPassword()
    {
        var setup = new FakeSetupService();
        var seedManager = new FakeSeedManager(_fakeMnemonic);
        var checker = new FakePasswordStrengthChecker(4);
        var vm = new SetupWizardViewModel(setup, seedManager, checker);

        vm.NextCommand.Execute(null);

        vm.CurrentStep.Should().Be(SetupStep.Password);
        vm.Mnemonic.Should().Be(_fakeMnemonic);
    }

    [Fact]
    public void Next_FromPasswordWithInvalidPassword_DoesNotAdvance()
    {
        var setup = new FakeSetupService();
        var seedManager = new FakeSeedManager(_fakeMnemonic);
        var checker = new FakePasswordStrengthChecker(4);
        var vm = new SetupWizardViewModel(setup, seedManager, checker);
        vm.NextCommand.Execute(null); // Welcome → Password
        // Password is empty; IsValid = false.

        vm.NextCommand.Execute(null);

        vm.CurrentStep.Should().Be(SetupStep.Password);
    }

    [Fact]
    public async Task Complete_OnSuccess_RaisesSetupCompletedWithSuccess()
    {
        var setup = new FakeSetupService();
        var seedManager = new FakeSeedManager(_fakeMnemonic);
        var checker = new FakePasswordStrengthChecker(4);
        var vm = new SetupWizardViewModel(setup, seedManager, checker);

        SetupCompletedEventArgs? capturedArgs = null;
        vm.SetupCompleted += (_, args) => capturedArgs = args;

        await vm.Confirm.CreateVaultCommand.ExecuteAsync(null);

        capturedArgs.Should().NotBeNull();
        capturedArgs!.Success.Should().BeTrue();
        capturedArgs.Error.Should().BeNull();
    }

    [Fact]
    public async Task Complete_OnFailure_RaisesSetupCompletedWithError()
    {
        var failure = new InvalidOperationException("setup failed");
        var setup = new FakeSetupService(throwOnComplete: failure);
        var seedManager = new FakeSeedManager(_fakeMnemonic);
        var checker = new FakePasswordStrengthChecker(4);
        var vm = new SetupWizardViewModel(setup, seedManager, checker);

        SetupCompletedEventArgs? capturedArgs = null;
        vm.SetupCompleted += (_, args) => capturedArgs = args;

        await vm.Confirm.CreateVaultCommand.ExecuteAsync(null);

        capturedArgs.Should().NotBeNull();
        capturedArgs!.Success.Should().BeFalse();
        capturedArgs.Error.Should().BeSameAs(failure);
        vm.Confirm.ErrorMessage.Should().NotBeEmpty();
    }

    private sealed class FakeSetupService : ISetupService
    {
        private readonly Exception? _throwOnComplete;

        public FakeSetupService(Exception? throwOnComplete = null)
        {
            _throwOnComplete = throwOnComplete;
        }

        public Task CompleteSetupAsync(string masterPassword, string mnemonic, CancellationToken ct)
        {
            if (_throwOnComplete is not null)
            {
                return Task.FromException(_throwOnComplete);
            }
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSeedManager : ISeedManager
    {
        private readonly string _mnemonic;

        public FakeSeedManager(string mnemonic) => _mnemonic = mnemonic;

        public string GenerateMnemonic() => _mnemonic;

        public bool IsValid(string mnemonic) => mnemonic == _mnemonic;

        public Task<byte[]> DeriveRecoveryKeyAsync(string mnemonic, string passphrase, CancellationToken ct) =>
            Task.FromResult(new byte[32]);
    }

    private sealed class FakePasswordStrengthChecker : IPasswordStrengthChecker
    {
        private readonly int _score;

        public FakePasswordStrengthChecker(int score) => _score = score;

        public PasswordStrength Evaluate(string password) =>
            new(_score, Warning: null, Suggestions: Array.Empty<string>());
    }
}

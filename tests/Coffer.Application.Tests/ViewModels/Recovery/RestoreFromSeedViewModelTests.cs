using Coffer.Application.Tests.Fakes;
using Coffer.Application.ViewModels.Recovery;
using Coffer.Core.Security;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Coffer.Application.Tests.ViewModels.Recovery;

public class RestoreFromSeedViewModelTests
{
    private const string ValidSeed =
        "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about";
    private const string StrongPassword = "StrongPassword!12";

    private static RestoreFromSeedViewModel Create(
        FakeSeedRecoveryService recovery, int passwordScore = 4) =>
        new(
            recovery,
            new FakeSeedManager(ValidSeed),
            new FakePasswordStrengthChecker(passwordScore),
            new FakeLocalizer(),
            NullLogger<RestoreFromSeedViewModel>.Instance);

    [Fact]
    public void Recover_CannotExecute_WithInvalidSeed()
    {
        var vm = Create(new FakeSeedRecoveryService());
        vm.Seed = "not a real seed";
        vm.NewPassword = StrongPassword;
        vm.ConfirmPassword = StrongPassword;

        vm.RecoverCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void Recover_CannotExecute_WithMismatchedPassword()
    {
        var vm = Create(new FakeSeedRecoveryService());
        vm.Seed = ValidSeed;
        vm.NewPassword = StrongPassword;
        vm.ConfirmPassword = "DifferentPassword!34";

        vm.RecoverCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void Recover_CannotExecute_WithWeakPassword()
    {
        var vm = Create(new FakeSeedRecoveryService(), passwordScore: 1);
        vm.Seed = ValidSeed;
        vm.NewPassword = StrongPassword;
        vm.ConfirmPassword = StrongPassword;

        vm.RecoverCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task Recover_WithValidInput_CallsServiceWithNormalizedSeedAndRaisesCompleted()
    {
        var recovery = new FakeSeedRecoveryService();
        var vm = Create(recovery);
        vm.Seed = "  ABANDON  abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about ";
        vm.NewPassword = StrongPassword;
        vm.ConfirmPassword = StrongPassword;

        var completed = 0;
        vm.RecoveryCompleted += (_, _) => completed++;

        vm.RecoverCommand.CanExecute(null).Should().BeTrue();
        await vm.RecoverCommand.ExecuteAsync(null);

        recovery.RecoverCalls.Should().Be(1);
        recovery.LastMnemonic.Should().Be(ValidSeed, "the seed is whitespace- and case-normalized");
        recovery.LastNewPassword.Should().Be(StrongPassword);
        completed.Should().Be(1);
        vm.Seed.Should().BeEmpty("sensitive input is cleared after success");
        vm.NewPassword.Should().BeEmpty();
    }

    [Fact]
    public async Task Recover_OnInvalidSeed_ShowsInvalidSeedMessageAndDoesNotComplete()
    {
        var recovery = new FakeSeedRecoveryService { RecoverThrow = new InvalidRecoverySeedException() };
        var vm = Create(recovery);
        vm.Seed = ValidSeed;
        vm.NewPassword = StrongPassword;
        vm.ConfirmPassword = StrongPassword;

        var completed = 0;
        vm.RecoveryCompleted += (_, _) => completed++;

        await vm.RecoverCommand.ExecuteAsync(null);

        vm.ErrorMessage.Should().Be("Restore.Seed.Error.InvalidSeed");
        completed.Should().Be(0);
    }

    [Fact]
    public async Task Recover_OnV1Vault_ShowsUnavailableMessage()
    {
        var recovery = new FakeSeedRecoveryService { RecoverThrow = new SeedRecoveryUnavailableException() };
        var vm = Create(recovery);
        vm.Seed = ValidSeed;
        vm.NewPassword = StrongPassword;
        vm.ConfirmPassword = StrongPassword;

        await vm.RecoverCommand.ExecuteAsync(null);

        vm.ErrorMessage.Should().Be("Restore.Seed.Error.Unavailable");
    }
}

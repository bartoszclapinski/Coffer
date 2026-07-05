using Coffer.Application.Tests.Fakes;
using Coffer.Application.ViewModels.Recovery;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Coffer.Application.Tests.ViewModels.Recovery;

public class EnableSeedRecoveryViewModelTests
{
    // words[2] and words[6] of this mnemonic are both "abandon" (the verification answers).
    private const string Seed =
        "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about";

    private static EnableSeedRecoveryViewModel Create(FakeSeedRecoveryService recovery) =>
        new(recovery, new FakeSeedManager(Seed), new FakeLocalizer(),
            NullLogger<EnableSeedRecoveryViewModel>.Instance);

    [Fact]
    public void Enable_CannotExecute_BeforeVerification()
    {
        var vm = Create(new FakeSeedRecoveryService());

        vm.Display.Words.Should().HaveCount(12, "the generated seed is shown for the owner to write down");
        vm.EnableCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task Enable_AfterCorrectVerification_CallsServiceAndRaisesCompleted()
    {
        var recovery = new FakeSeedRecoveryService();
        var vm = Create(recovery);
        vm.Verification.Word3 = "abandon";
        vm.Verification.Word7 = "abandon";

        var completed = 0;
        vm.Completed += (_, _) => completed++;

        vm.EnableCommand.CanExecute(null).Should().BeTrue();
        await vm.EnableCommand.ExecuteAsync(null);

        recovery.EnableCalls.Should().Be(1);
        recovery.LastEnableMnemonic.Should().Be(Seed);
        vm.Enabled.Should().BeTrue();
        completed.Should().Be(1);
    }

    [Fact]
    public void Cancel_RaisesCancelRequested()
    {
        var vm = Create(new FakeSeedRecoveryService());

        var cancelled = 0;
        vm.CancelRequested += (_, _) => cancelled++;

        vm.CancelCommand.Execute(null);

        cancelled.Should().Be(1);
        vm.Enabled.Should().BeFalse();
    }
}

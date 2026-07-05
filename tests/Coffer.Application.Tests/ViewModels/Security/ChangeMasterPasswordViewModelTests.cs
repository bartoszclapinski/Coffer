using Coffer.Application.Tests.Fakes;
using Coffer.Application.ViewModels.Security;
using Coffer.Core.Security;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Coffer.Application.Tests.ViewModels.Security;

public class ChangeMasterPasswordViewModelTests
{
    private const string CurrentPassword = "CurrentPassword!12";
    private const string NewPassword = "BrandNewPassword!34";

    private static ChangeMasterPasswordViewModel Create(
        FakeMasterPasswordService service, int passwordScore = 4) =>
        new(service, new FakePasswordStrengthChecker(passwordScore), new FakeLocalizer(),
            NullLogger<ChangeMasterPasswordViewModel>.Instance);

    [Fact]
    public void Change_CannotExecute_WithMismatchedNewPassword()
    {
        var vm = Create(new FakeMasterPasswordService());
        vm.CurrentPassword = CurrentPassword;
        vm.NewPassword = NewPassword;
        vm.ConfirmPassword = "DoesNotMatch!56";

        vm.ChangeCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void Change_CannotExecute_WhenNewEqualsCurrent()
    {
        var vm = Create(new FakeMasterPasswordService());
        vm.CurrentPassword = CurrentPassword;
        vm.NewPassword = CurrentPassword;
        vm.ConfirmPassword = CurrentPassword;

        vm.ChangeCommand.CanExecute(null).Should().BeFalse("a change must pick a different password");
    }

    [Fact]
    public void Change_CannotExecute_WithWeakNewPassword()
    {
        var vm = Create(new FakeMasterPasswordService(), passwordScore: 1);
        vm.CurrentPassword = CurrentPassword;
        vm.NewPassword = NewPassword;
        vm.ConfirmPassword = NewPassword;

        vm.ChangeCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task Change_WithValidInput_CallsServiceAndRaisesCompleted()
    {
        var service = new FakeMasterPasswordService();
        var vm = Create(service);
        vm.CurrentPassword = CurrentPassword;
        vm.NewPassword = NewPassword;
        vm.ConfirmPassword = NewPassword;

        var completed = 0;
        vm.Completed += (_, _) => completed++;

        vm.ChangeCommand.CanExecute(null).Should().BeTrue();
        await vm.ChangeCommand.ExecuteAsync(null);

        service.Calls.Should().Be(1);
        service.LastCurrent.Should().Be(CurrentPassword);
        service.LastNew.Should().Be(NewPassword);
        vm.Changed.Should().BeTrue();
        completed.Should().Be(1);
        vm.CurrentPassword.Should().BeEmpty("sensitive fields are cleared after success");
        vm.NewPassword.Should().BeEmpty();
    }

    [Fact]
    public async Task Change_OnWrongCurrentPassword_ShowsMessageAndDoesNotComplete()
    {
        var service = new FakeMasterPasswordService { Throw = new InvalidMasterPasswordException() };
        var vm = Create(service);
        vm.CurrentPassword = CurrentPassword;
        vm.NewPassword = NewPassword;
        vm.ConfirmPassword = NewPassword;

        var completed = 0;
        vm.Completed += (_, _) => completed++;

        await vm.ChangeCommand.ExecuteAsync(null);

        vm.ErrorMessage.Should().Be("ChangePassword.Error.WrongCurrent");
        completed.Should().Be(0);
        vm.CurrentPassword.Should().BeEmpty("the wrong current password is cleared for a retry");
    }

    [Fact]
    public void Cancel_RaisesCancelRequested()
    {
        var vm = Create(new FakeMasterPasswordService());

        var cancelled = 0;
        vm.CancelRequested += (_, _) => cancelled++;

        vm.CancelCommand.Execute(null);

        cancelled.Should().Be(1);
    }
}

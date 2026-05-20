using Coffer.Application.ViewModels.Login;
using Coffer.Core.Security;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Coffer.Application.Tests.ViewModels.Login;

public class LoginViewModelTests
{
    [Fact]
    public async Task LoginCommand_OnSuccess_RaisesLoginCompletedAndClearsBusy()
    {
        var service = new FakeLoginService();
        var vm = new LoginViewModel(service, NullLogger<LoginViewModel>.Instance)
        {
            Password = "correct",
        };

        var raised = 0;
        vm.LoginCompleted += (_, _) => raised++;

        await vm.LoginCommand.ExecuteAsync(null);

        raised.Should().Be(1);
        vm.ErrorMessage.Should().BeEmpty();
        vm.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task LoginCommand_OnInvalidMasterPassword_SetsErrorAndClearsPassword()
    {
        var service = new FakeLoginService
        {
            ThrowOnLogin = new InvalidMasterPasswordException(),
        };
        var vm = new LoginViewModel(service, NullLogger<LoginViewModel>.Instance)
        {
            Password = "wrong",
        };

        await vm.LoginCommand.ExecuteAsync(null);

        vm.ErrorMessage.Should().Be("Nieprawidłowe hasło.");
        vm.Password.Should().BeEmpty();
        vm.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task LoginCommand_OnVaultCorrupted_SetsReasonSpecificMessage()
    {
        var service = new FakeLoginService
        {
            ThrowOnLogin = new VaultCorruptedException(
                VaultCorruptionReason.DekFileFormat,
                "malformed"),
        };
        var vm = new LoginViewModel(service, NullLogger<LoginViewModel>.Instance)
        {
            Password = "anything",
        };

        await vm.LoginCommand.ExecuteAsync(null);

        vm.ErrorMessage.Should().Contain("Plik sejfu jest uszkodzony");
    }

    [Fact]
    public async Task LoginCommand_OnGenericException_SetsGenericMessage()
    {
        var service = new FakeLoginService
        {
            ThrowOnLogin = new InvalidOperationException("unexpected"),
        };
        var vm = new LoginViewModel(service, NullLogger<LoginViewModel>.Instance)
        {
            Password = "anything",
        };

        await vm.LoginCommand.ExecuteAsync(null);

        vm.ErrorMessage.Should().Be("Nie udało się zalogować. Spróbuj ponownie.");
    }

    [Fact]
    public void ClearSensitive_ZeroesPassword()
    {
        var vm = new LoginViewModel(new FakeLoginService(), NullLogger<LoginViewModel>.Instance)
        {
            Password = "something",
        };

        vm.ClearSensitive();

        vm.Password.Should().BeEmpty();
    }

    private sealed class FakeLoginService : ILoginService
    {
        public Exception? ThrowOnLogin { get; set; }

        public Task<bool> TryLoginFromCachedKeyAsync(CancellationToken ct) =>
            Task.FromResult(false);

        public Task LoginWithPasswordAsync(string masterPassword, CancellationToken ct) =>
            ThrowOnLogin is null
                ? Task.CompletedTask
                : Task.FromException(ThrowOnLogin);

        public Task LogoutAsync(CancellationToken ct) => Task.CompletedTask;
    }
}

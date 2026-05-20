using Coffer.Application.ViewModels.Main;
using Coffer.Core.Security;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Coffer.Application.Tests.ViewModels.Main;

public class MainViewModelTests
{
    [Fact]
    public async Task LogoutCommand_CallsLoginServiceLogoutAndRaisesLoggedOut()
    {
        var service = new RecordingLoginService();
        var vm = new MainViewModel(service, NullLogger<MainViewModel>.Instance);

        var raised = 0;
        vm.LoggedOut += (_, _) => raised++;

        await vm.LogoutCommand.ExecuteAsync(null);

        service.LogoutCalls.Should().Be(1);
        raised.Should().Be(1);
    }

    [Fact]
    public async Task LogoutCommand_WhenServiceThrows_StillRaisesLoggedOut()
    {
        var service = new RecordingLoginService
        {
            ThrowOnLogout = new InvalidOperationException("test"),
        };
        var vm = new MainViewModel(service, NullLogger<MainViewModel>.Instance);

        var raised = 0;
        vm.LoggedOut += (_, _) => raised++;

        await vm.LogoutCommand.ExecuteAsync(null);

        raised.Should().Be(1,
            "leaving the user stuck on MainWindow because LogoutAsync failed is worse than navigating anyway");
    }

    [Fact]
    public void AppVersion_ReturnsNonEmptyString()
    {
        var vm = new MainViewModel(new RecordingLoginService(), NullLogger<MainViewModel>.Instance);

        vm.AppVersion.Should().NotBeNullOrEmpty();
    }

    private sealed class RecordingLoginService : ILoginService
    {
        public int LogoutCalls { get; private set; }

        public Exception? ThrowOnLogout { get; set; }

        public Task<bool> TryLoginFromCachedKeyAsync(CancellationToken ct) =>
            Task.FromResult(false);

        public Task LoginWithPasswordAsync(string masterPassword, CancellationToken ct) =>
            Task.CompletedTask;

        public Task LogoutAsync(CancellationToken ct)
        {
            LogoutCalls++;
            return ThrowOnLogout is null
                ? Task.CompletedTask
                : Task.FromException(ThrowOnLogout);
        }
    }
}

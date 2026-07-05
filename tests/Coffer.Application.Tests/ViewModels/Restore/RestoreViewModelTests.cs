using Coffer.Application.Tests.Fakes;
using Coffer.Application.ViewModels.Restore;
using Coffer.Core.Backup;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Coffer.Application.Tests.ViewModels.Restore;

public class RestoreViewModelTests
{
    private static RestoreViewModel Create(FakeRestoreService service) =>
        new(service, new FakeLocalizer(), NullLogger<RestoreViewModel>.Instance);

    [Fact]
    public async Task Load_PopulatesSnapshotRows()
    {
        var service = new FakeRestoreService();
        service.Snapshots.Add(new SnapshotInfo(new DateOnly(2026, 7, 3), "coffer-2026-07-03.db", 1536));
        service.Snapshots.Add(new SnapshotInfo(new DateOnly(2026, 7, 1), "coffer-2026-07-01.db", 800));
        var vm = Create(service);

        await vm.LoadCommand.ExecuteAsync(null);

        vm.HasSnapshots.Should().BeTrue();
        vm.Snapshots.Should().HaveCount(2);
        vm.Snapshots[0].DisplayDate.Should().Be("2026-07-03");
        vm.Snapshots[0].DisplaySize.Should().Be("1.5 KB");
        vm.Snapshots[1].DisplaySize.Should().Be("800 B");
    }

    [Fact]
    public async Task Load_WithNoSnapshots_LeavesEmptyState()
    {
        var vm = Create(new FakeRestoreService());

        await vm.LoadCommand.ExecuteAsync(null);

        vm.HasSnapshots.Should().BeFalse();
        vm.Snapshots.Should().BeEmpty();
    }

    [Fact]
    public async Task Restore_StagesSelectedSnapshotAndRequestsClose()
    {
        var service = new FakeRestoreService();
        service.Snapshots.Add(new SnapshotInfo(new DateOnly(2026, 7, 3), "coffer-2026-07-03.db", 10));
        var vm = Create(service);
        await vm.LoadCommand.ExecuteAsync(null);
        vm.SelectedSnapshot = vm.Snapshots[0];

        var closed = 0;
        vm.CloseRequested += (_, _) => closed++;

        await vm.RestoreCommand.ExecuteAsync(null);

        service.StageCalls.Should().Be(1);
        service.LastStagedDate.Should().Be(new DateOnly(2026, 7, 3));
        vm.Staged.Should().BeTrue();
        closed.Should().Be(1);
    }

    [Fact]
    public void Restore_WithNoSelection_CannotExecute()
    {
        var vm = Create(new FakeRestoreService());

        vm.RestoreCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task Restore_WhenStagingFails_KeepsDialogOpenWithAMessage()
    {
        var service = new FakeRestoreService { StageThrow = new FileNotFoundException("gone") };
        service.Snapshots.Add(new SnapshotInfo(new DateOnly(2026, 7, 3), "coffer-2026-07-03.db", 10));
        var vm = Create(service);
        await vm.LoadCommand.ExecuteAsync(null);
        vm.SelectedSnapshot = vm.Snapshots[0];

        var closed = 0;
        vm.CloseRequested += (_, _) => closed++;

        await vm.RestoreCommand.ExecuteAsync(null);

        vm.Staged.Should().BeFalse();
        closed.Should().Be(0, "the dialog stays open so the owner sees the failure");
        vm.StatusMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Cancel_RequestsCloseWithoutStaging()
    {
        var service = new FakeRestoreService();
        var vm = Create(service);

        var closed = 0;
        vm.CloseRequested += (_, _) => closed++;

        vm.CancelCommand.Execute(null);

        vm.Staged.Should().BeFalse();
        service.StageCalls.Should().Be(0);
        closed.Should().Be(1);
    }
}

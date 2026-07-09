using Coffer.Application.ViewModels.Shell;
using FluentAssertions;

namespace Coffer.Application.Tests.ViewModels.Shell;

public class CommandPaletteViewModelTests
{
    private static PaletteCommand Cmd(string title, Action? run = null) =>
        new(title, "NAVIGATE", "squares-four", run ?? (static () => { }));

    private static CommandPaletteViewModel OpenWith(params PaletteCommand[] commands)
    {
        var vm = new CommandPaletteViewModel();
        vm.Open(commands);
        return vm;
    }

    [Fact]
    public void Open_ShowsAllCommandsSelectsFirstAndClearsQuery()
    {
        var vm = OpenWith(Cmd("Go to Overview"), Cmd("Go to Budgets"));

        vm.IsOpen.Should().BeTrue();
        vm.Query.Should().BeEmpty();
        vm.Results.Should().HaveCount(2);
        vm.SelectedIndex.Should().Be(0);
    }

    [Fact]
    public void Query_FiltersBySubsequenceAndResetsSelection()
    {
        var vm = OpenWith(Cmd("Go to Overview"), Cmd("Go to Budgets"), Cmd("Switch theme"));

        vm.Query = "bud";

        vm.Results.Should().ContainSingle().Which.Title.Should().Be("Go to Budgets");
        vm.SelectedIndex.Should().Be(0);
    }

    [Fact]
    public void Query_SubsequenceMatchesNonContiguousCharacters()
    {
        var vm = OpenWith(Cmd("Switch theme"), Cmd("Go to Budgets"));

        vm.Query = "sth"; // S-witch T-H-eme

        vm.Results.Should().ContainSingle().Which.Title.Should().Be("Switch theme");
    }

    [Fact]
    public void Query_NoMatch_ClearsResultsAndSelection()
    {
        var vm = OpenWith(Cmd("Go to Overview"));

        vm.Query = "zzz";

        vm.Results.Should().BeEmpty();
        vm.SelectedIndex.Should().Be(-1);
    }

    [Fact]
    public void MoveSelection_WrapsBothEnds()
    {
        var vm = OpenWith(Cmd("A"), Cmd("B"), Cmd("C"));

        vm.MoveSelection(-1);
        vm.SelectedIndex.Should().Be(2, "moving up from the first item wraps to the last");

        vm.MoveSelection(1);
        vm.SelectedIndex.Should().Be(0, "moving down from the last item wraps to the first");
    }

    [Fact]
    public void ExecuteSelected_RunsSelectedActionAndCloses()
    {
        var ran = "";
        var vm = OpenWith(
            Cmd("A", () => ran = "A"),
            Cmd("B", () => ran = "B"));
        vm.SelectedIndex = 1;

        vm.ExecuteSelected();

        ran.Should().Be("B");
        vm.IsOpen.Should().BeFalse();
    }

    [Fact]
    public void ExecuteSelected_WithNoSelection_IsNoOp()
    {
        var ran = false;
        var vm = OpenWith(Cmd("A", () => ran = true));
        vm.Query = "zzz"; // filters everything out -> SelectedIndex -1

        vm.ExecuteSelected();

        ran.Should().BeFalse();
    }

    [Fact]
    public void Close_SetsIsOpenFalse()
    {
        var vm = OpenWith(Cmd("A"));

        vm.Close();

        vm.IsOpen.Should().BeFalse();
    }
}

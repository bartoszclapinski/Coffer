using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Coffer.Application.ViewModels.Shell;

/// <summary>
/// The ⌘K command palette: a filtered, keyboard-driven launcher over the shell's navigation
/// and quick actions. The host (<c>MainViewModel</c>) supplies the command set on open — nav
/// entries plus toggles — so titles reflect the current language every time it opens.
/// Framework-free; the overlay view binds to this state.
/// </summary>
public sealed partial class CommandPaletteViewModel : ObservableObject
{
    private IReadOnlyList<PaletteCommand> _all = [];

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private string _query = "";

    [ObservableProperty]
    private int _selectedIndex;

    public ObservableCollection<PaletteCommand> Results { get; } = [];

    /// <summary>Opens the palette with a fresh command set (empty query, first item selected).</summary>
    public void Open(IReadOnlyList<PaletteCommand> commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        _all = commands;
        Query = "";
        Filter();
        IsOpen = true;
    }

    [RelayCommand]
    public void Close() => IsOpen = false;

    partial void OnQueryChanged(string value) => Filter();

    private void Filter()
    {
        Results.Clear();
        foreach (var command in _all)
        {
            if (Matches(command.Title, Query))
            {
                Results.Add(command);
            }
        }

        SelectedIndex = Results.Count > 0 ? 0 : -1;
    }

    /// <summary>Case-insensitive subsequence match (each query char appears in order) — a light fuzzy filter.</summary>
    private static bool Matches(string title, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        var t = title.ToLowerInvariant();
        var q = query.Trim().ToLowerInvariant();
        var i = 0;
        foreach (var c in t)
        {
            if (i < q.Length && c == q[i])
            {
                i++;
            }
        }

        return i == q.Length;
    }

    public void MoveSelection(int delta)
    {
        if (Results.Count == 0)
        {
            return;
        }

        var next = SelectedIndex + delta;
        if (next < 0)
        {
            next = Results.Count - 1;
        }
        else if (next >= Results.Count)
        {
            next = 0;
        }

        SelectedIndex = next;
    }

    /// <summary>Runs the selected command and closes the palette. No-op if nothing is selected.</summary>
    public void ExecuteSelected()
    {
        if (SelectedIndex < 0 || SelectedIndex >= Results.Count)
        {
            return;
        }

        var command = Results[SelectedIndex];
        Close();
        command.Run();
    }
}

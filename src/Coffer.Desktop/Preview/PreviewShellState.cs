using CommunityToolkit.Mvvm.ComponentModel;

namespace Coffer.Desktop.Preview;

/// <summary>
/// Minimal stand-in for the shell's balance-privacy state, used as the DataContext of the
/// dev-only preview windows so a migrated screen's <c>$parent[Window].DataContext.HideBalances</c>
/// money-blur binding resolves without the real <c>MainViewModel</c>.
/// </summary>
internal sealed partial class PreviewShellState : ObservableObject
{
    [ObservableProperty]
    private bool _hideBalances;
}

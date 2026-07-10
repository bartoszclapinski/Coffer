namespace Coffer.Application.ViewModels.Shell;

/// <summary>
/// A single entry in the command palette: a localized <see cref="Title"/>, a right-aligned
/// <see cref="Category"/> tag (e.g. NAVIGATE / SETTING / ACTION), a Phosphor <see cref="Icon"/>
/// name, and the <see cref="Run"/> action executed when the entry is chosen.
/// </summary>
public sealed record PaletteCommand(string Title, string Category, string Icon, Action Run);

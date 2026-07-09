using System.Collections.Generic;
using Avalonia.Markup.Xaml;

namespace Coffer.Desktop.Markup;

/// <summary>
/// Resolves a Phosphor icon <em>name</em> (matching the design spec's icon column, e.g.
/// <c>shopping-cart</c>) to its glyph string, so views reference icons by name instead of
/// pasting private-use codepoints. Pair with the <c>Coffer.Icons</c> (regular) or
/// <c>Coffer.IconsFill</c> (filled) font family.
/// <para>Usage: <c>&lt;TextBlock FontFamily="{DynamicResource Coffer.Icons}"
/// Text="{markup:PhosphorIcon shopping-cart}" /&gt;</c></para>
/// An unknown name throws at XAML load time so typos surface immediately.
/// </summary>
public sealed class PhosphorIcon : MarkupExtension
{
    // Codepoints lifted from phosphor-icons/web src/regular/style.css (the fill font shares them).
    private static readonly IReadOnlyDictionary<string, string> _glyphs = new Dictionary<string, string>
    {
        // Rail / navigation
        ["squares-four"] = "\ue464",
        ["bank"] = "\ue0b4",
        ["buildings"] = "\ue102",
        ["arrows-left-right"] = "\ue0a0",
        ["chart-pie"] = "\ue158",
        ["chart-pie-slice"] = "\ue15a",
        ["target"] = "\ue47c",
        ["chart-line-up"] = "\ue156",
        ["chart-line"] = "\ue154",
        ["gear"] = "\ue270",
        ["gear-six"] = "\ue272",
        ["file-arrow-down"] = "\ue232",
        ["upload-simple"] = "\ue4c0",
        ["robot"] = "\ue762",
        ["chat-circle-dots"] = "\ue16c",
        ["bell"] = "\ue0ce",
        ["calendar-blank"] = "\ue10a",
        ["wallet"] = "\ue68a",
        ["scales"] = "\ue750",
        ["hand-coins"] = "\uea8c",
        // Top bar / palette
        ["magnifying-glass"] = "\ue30c",
        ["list-magnifying-glass"] = "\uebe0",
        ["sun"] = "\ue472",
        ["moon"] = "\ue330",
        ["eye"] = "\ue220",
        ["eye-slash"] = "\ue224",
        ["plus"] = "\ue3d4",
        ["command"] = "\ue1c4",
        ["caret-right"] = "\ue13a",
        ["caret-up"] = "\ue13c",
        ["caret-down"] = "\ue136",
        ["arrow-right"] = "\ue06c",
        // Category icons (design spec table)
        ["shopping-cart"] = "\ue41e",
        ["fork-knife"] = "\ue262",
        ["car"] = "\ue112",
        ["repeat"] = "\ue3f6",
        ["house-line"] = "\ue2c4",
        ["shopping-bag"] = "\ue416",
        ["lightning"] = "\ue2de",
    };

    public PhosphorIcon()
    {
    }

    public PhosphorIcon(string name) => Name = name;

    /// <summary>The Phosphor icon name (as in the design spec), e.g. <c>chart-pie</c>.</summary>
    public string Name { get; set; } = "";

    /// <summary>Looks up a glyph by name. Throws <see cref="KeyNotFoundException"/> for an unknown name.</summary>
    public static string Glyph(string name) =>
        _glyphs.TryGetValue(name, out var glyph)
            ? glyph
            : throw new KeyNotFoundException(
                $"Unknown Phosphor icon '{name}'. Add its codepoint to {nameof(PhosphorIcon)}._glyphs.");

    public override object ProvideValue(IServiceProvider serviceProvider) => Glyph(Name);
}

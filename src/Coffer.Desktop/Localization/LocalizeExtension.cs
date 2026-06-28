using Avalonia.Data;
using Avalonia.Markup.Xaml;
using Coffer.Application.Localization;

namespace Coffer.Desktop.Localization;

/// <summary>
/// XAML markup extension for localized text: <c>{l:Localize Nav.Dashboard}</c>. Binds to the
/// singleton <see cref="ILocalizer"/>'s indexer so the bound control refreshes live when the
/// language changes (the localizer raises the "Item[]" indexer notification). At design time
/// (no <see cref="App.Services"/>) it returns the key so the previewer still renders.
/// </summary>
public sealed class LocalizeExtension : MarkupExtension
{
    public LocalizeExtension()
    {
    }

    public LocalizeExtension(string key) => Key = key;

    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if (App.Services?.GetService(typeof(ILocalizer)) is not ILocalizer localizer)
        {
            return Key;
        }

        return new Binding($"[{Key}]")
        {
            Source = localizer,
            Mode = BindingMode.OneWay,
        };
    }
}

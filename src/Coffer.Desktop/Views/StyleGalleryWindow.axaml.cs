using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Coffer.Core.Theming;
using Coffer.Desktop.Theme;

namespace Coffer.Desktop.Views;

/// <summary>
/// Dev-only design-system validation surface (Sprint 28-A). Launched via the
/// <c>COFFER_STYLE_GALLERY</c> environment variable, never from the normal app flow.
/// </summary>
public partial class StyleGalleryWindow : Window
{
    public StyleGalleryWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnToggleTheme(object? sender, RoutedEventArgs e)
    {
        var next = ThemeManager.Current == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark;
        ThemeManager.Apply(next);
    }
}

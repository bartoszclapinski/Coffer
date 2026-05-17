using Avalonia.Controls;
using Coffer.Core.Security;
using Microsoft.Extensions.DependencyInjection;

namespace Coffer.Desktop.Views.Setup;

public partial class BipSeedDisplayStepView : UserControl
{
    public BipSeedDisplayStepView()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttachedToVisualTree;
    }

    private void OnAttachedToVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        // Apply screen-capture protection to the parent window when the seed-display
        // step is shown. The blocker is resolved through App.Services because Views are
        // constructed by the XAML loader (no constructor-injection path).
        var topLevel = TopLevel.GetTopLevel(this);
        var hwnd = topLevel?.TryGetPlatformHandle()?.Handle ?? nint.Zero;
        if (hwnd == nint.Zero)
        {
            return;
        }

        var blocker = App.Services?.GetService<IScreenCaptureBlocker>();
        blocker?.Apply(hwnd);
    }
}

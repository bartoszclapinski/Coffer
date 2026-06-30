using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Coffer.Desktop.Views.Setup;

public partial class BipSeedDisplayStepView : UserControl
{
    public static readonly RoutedEvent<SeedDisplayReadyEventArgs> SeedDisplayReadyEvent =
        RoutedEvent.Register<BipSeedDisplayStepView, SeedDisplayReadyEventArgs>(
            nameof(SeedDisplayReady), RoutingStrategies.Bubble);

    public BipSeedDisplayStepView()
    {
        InitializeComponent();
    }

    public event EventHandler<SeedDisplayReadyEventArgs>? SeedDisplayReady
    {
        add => AddHandler(SeedDisplayReadyEvent, value);
        remove => RemoveHandler(SeedDisplayReadyEvent, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // Raise a bubbling event so an ancestor (the App-level handler on the wizard window)
        // can apply screen-capture protection to the host window. The view deliberately does
        // not resolve IScreenCaptureBlocker itself — DI lives at the composition root, not in
        // the view layer.
        var hwnd = TopLevel.GetTopLevel(this)?.TryGetPlatformHandle()?.Handle ?? nint.Zero;
        if (hwnd == nint.Zero)
        {
            return;
        }

        RaiseEvent(new SeedDisplayReadyEventArgs(SeedDisplayReadyEvent, hwnd));
    }
}

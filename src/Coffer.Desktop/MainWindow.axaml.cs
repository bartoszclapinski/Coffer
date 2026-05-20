using Avalonia.Controls;
using Avalonia.Input;
using Coffer.Core.Security;
using Microsoft.Extensions.Logging;

namespace Coffer.Desktop;

public partial class MainWindow : Window
{
    private readonly ILastActivityTracker? _activityTracker;
    private readonly ILogger<MainWindow>? _logger;

    public MainWindow()
    {
        InitializeComponent();
    }

    public MainWindow(ILastActivityTracker activityTracker, ILogger<MainWindow> logger)
        : this()
    {
        _activityTracker = activityTracker;
        _logger = logger;
        _logger.LogInformation("MainWindow created");

        // Top-level pointer and key events feed the activity tracker the
        // AutoLockMonitor polls. Tunnel-only subscription sees input before any
        // inner handler can mark it Handled, and avoids the double-fire that
        // Tunnel | Bubble would produce. PointerMoved is deliberately omitted:
        // KeyDown + PointerPressed already capture "user is active" with
        // sub-second precision against the 60-second polling cadence, and
        // pointer-moved fires per-frame on the UI thread.
        AddHandler(KeyDownEvent, OnUiActivity, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        AddHandler(PointerPressedEvent, OnUiActivity, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    private void OnUiActivity(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        _activityTracker?.RegisterActivity();
}

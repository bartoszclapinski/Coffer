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

        // Top-level pointer and key events feed the activity tracker that the
        // AutoLockMonitor polls. Subscribing here keeps the wiring co-located with
        // the window lifetime — the events stop firing as soon as the window closes.
        AddHandler(KeyDownEvent, OnUiActivity, Avalonia.Interactivity.RoutingStrategies.Tunnel | Avalonia.Interactivity.RoutingStrategies.Bubble);
        AddHandler(PointerPressedEvent, OnUiActivity, Avalonia.Interactivity.RoutingStrategies.Tunnel | Avalonia.Interactivity.RoutingStrategies.Bubble);
        AddHandler(PointerMovedEvent, OnUiActivity, Avalonia.Interactivity.RoutingStrategies.Tunnel | Avalonia.Interactivity.RoutingStrategies.Bubble);
    }

    private void OnUiActivity(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        _activityTracker?.RegisterActivity();
}

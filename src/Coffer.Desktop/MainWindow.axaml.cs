using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Coffer.Application.ViewModels.Main;
using Coffer.Application.ViewModels.Shell;
using Coffer.Core.Security;
using Microsoft.Extensions.Logging;

namespace Coffer.Desktop;

public partial class MainWindow : Window
{
    private readonly ILastActivityTracker? _activityTracker;
    private readonly ILogger<MainWindow>? _logger;
    private CommandPaletteViewModel? _palette;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
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

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_palette is not null)
        {
            _palette.PropertyChanged -= OnPalettePropertyChanged;
        }

        _palette = (DataContext as MainViewModel)?.Palette;
        if (_palette is not null)
        {
            _palette.PropertyChanged += OnPalettePropertyChanged;
        }
    }

    private void OnPalettePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Autofocus the palette input the moment it opens so the owner can type immediately.
        if (e.PropertyName == nameof(CommandPaletteViewModel.IsOpen) && _palette is { IsOpen: true })
        {
            Dispatcher.UIThread.Post(() => PaletteInput.Focus(), DispatcherPriority.Input);
        }
    }

    private void OnPaletteKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Down:
                vm.Palette.MoveSelection(1);
                e.Handled = true;
                break;
            case Key.Up:
                vm.Palette.MoveSelection(-1);
                e.Handled = true;
                break;
            case Key.Enter:
                vm.Palette.ExecuteSelected();
                e.Handled = true;
                break;
            case Key.Escape:
                vm.Palette.Close();
                e.Handled = true;
                break;
        }
    }

    private void OnPaletteItemTapped(object? sender, TappedEventArgs e) =>
        (DataContext as MainViewModel)?.Palette.ExecuteSelected();

    private void OnBackdropPressed(object? sender, PointerPressedEventArgs e) =>
        (DataContext as MainViewModel)?.Palette.Close();

    // Clicks inside the palette panel must not bubble to the backdrop (which would close it).
    private void OnPalettePressed(object? sender, PointerPressedEventArgs e) => e.Handled = true;
}

using Avalonia.Interactivity;

namespace Coffer.Desktop.Views.Setup;

public sealed class SeedDisplayReadyEventArgs : RoutedEventArgs
{
    public SeedDisplayReadyEventArgs(RoutedEvent routedEvent, nint windowHandle)
        : base(routedEvent)
    {
        WindowHandle = windowHandle;
    }

    public nint WindowHandle { get; }
}

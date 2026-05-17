namespace Coffer.Core.Security;

/// <summary>
/// Blocks screen capture / screen sharing for a specific platform window.
/// On Windows this maps to <c>SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE)</c>.
/// </summary>
/// <remarks>
/// The handle is passed as <see cref="nint"/> (BCL type) so this interface stays free
/// of platform-specific UI dependencies (Avalonia, MAUI, WPF). Callers in the UI layer
/// extract the native window handle and pass it through.
/// </remarks>
public interface IScreenCaptureBlocker
{
    void Apply(nint hwnd);
}

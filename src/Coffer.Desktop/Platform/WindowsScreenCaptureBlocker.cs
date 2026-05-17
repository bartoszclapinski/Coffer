using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Coffer.Core.Security;

namespace Coffer.Desktop.Platform;

[SupportedOSPlatform("windows")]
public sealed class WindowsScreenCaptureBlocker : IScreenCaptureBlocker
{
    private const uint _wdaExcludeFromCapture = 0x00000011;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(nint hwnd, uint affinity);

    public void Apply(nint hwnd)
    {
        if (hwnd == nint.Zero)
        {
            return;
        }

        // Best-effort: older Windows builds or virtual displays may return false. The
        // setup wizard text reminds the user not to screenshot regardless.
        _ = SetWindowDisplayAffinity(hwnd, _wdaExcludeFromCapture);
    }
}

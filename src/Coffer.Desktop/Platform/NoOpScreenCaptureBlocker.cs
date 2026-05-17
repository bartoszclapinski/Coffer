using Coffer.Core.Security;
using Microsoft.Extensions.Logging;

namespace Coffer.Desktop.Platform;

public sealed class NoOpScreenCaptureBlocker : IScreenCaptureBlocker
{
    private readonly ILogger<NoOpScreenCaptureBlocker> _logger;

    public NoOpScreenCaptureBlocker(ILogger<NoOpScreenCaptureBlocker> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public void Apply(nint hwnd)
    {
        _logger.LogWarning(
            "Screen-capture protection unavailable on this platform; the seed-display window can be captured by screenshot tools.");
    }
}

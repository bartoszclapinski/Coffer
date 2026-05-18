namespace Coffer.Application.ViewModels.Setup;

public sealed class SetupCompletedEventArgs : EventArgs
{
    public SetupCompletedEventArgs(bool success, Exception? error)
    {
        Success = success;
        Error = error;
    }

    public bool Success { get; }

    public Exception? Error { get; }
}

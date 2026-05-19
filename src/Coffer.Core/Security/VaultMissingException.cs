namespace Coffer.Core.Security;

/// <summary>
/// Thrown by <see cref="ILoginService.LoginWithPasswordAsync"/> when the DEK file is
/// not present at the expected path. This is a defensive path — the App routing in
/// <c>App.axaml.cs</c> should detect the missing file and show the setup wizard
/// instead of reaching the login flow.
/// </summary>
public sealed class VaultMissingException : Exception
{
    public VaultMissingException(string filePath)
        : base($"The vault file at {filePath} is missing.")
    {
        FilePath = filePath;
    }

    public string FilePath { get; }
}

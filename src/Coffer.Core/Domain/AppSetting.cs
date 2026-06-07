namespace Coffer.Core.Domain;

/// <summary>
/// A single key/value application setting persisted in the encrypted DB (e.g. the
/// monthly AI cap, the active provider). Non-secret config only — API keys live in
/// <c>ISecretStore</c>, never here.
/// </summary>
public class AppSetting
{
    public string Key { get; set; } = "";

    public string Value { get; set; } = "";
}

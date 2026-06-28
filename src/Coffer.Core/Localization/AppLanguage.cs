namespace Coffer.Core.Localization;

/// <summary>
/// The UI languages Coffer ships with. English is the neutral resource culture
/// (<c>Strings.resx</c>); Polish is the satellite (<c>Strings.pl.resx</c>). The
/// owner switches between them at runtime in Ustawienia/Settings.
/// </summary>
public enum AppLanguage
{
    Polish,
    English,
}

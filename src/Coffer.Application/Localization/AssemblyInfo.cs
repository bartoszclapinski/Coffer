using System.Resources;

// English is the neutral culture: Strings.resx holds the English text and serves as the
// fallback when a satellite (e.g. pl) lacks a key. Declaring it here lets the resource
// manager skip probing for an "en" satellite assembly that does not exist.
[assembly: NeutralResourcesLanguage("en")]

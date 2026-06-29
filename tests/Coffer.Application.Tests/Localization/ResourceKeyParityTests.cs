using System.Collections;
using System.Globalization;
using System.Resources;
using Coffer.Application.Localization;
using FluentAssertions;

namespace Coffer.Application.Tests.Localization;

public class ResourceKeyParityTests
{
    private static readonly ResourceManager _resources =
        new("Coffer.Application.Localization.Strings", typeof(Localizer).Assembly);

    [Fact]
    public void NeutralAndPolishResources_HaveIdenticalKeySets()
    {
        var neutral = Keys(CultureInfo.InvariantCulture);
        var polish = Keys(CultureInfo.GetCultureInfo("pl"));

        neutral.Except(polish).Should().BeEmpty("every neutral (English) key must have a Polish counterpart");
        polish.Except(neutral).Should().BeEmpty("every Polish key must have a neutral (English) counterpart");
    }

    private static HashSet<string> Keys(CultureInfo culture)
    {
        using var set = _resources.GetResourceSet(culture, createIfNotExists: true, tryParents: false)
            ?? throw new InvalidOperationException($"No resource set for culture '{culture.Name}'.");
        return set.Cast<DictionaryEntry>()
            .Select(e => (string)e.Key)
            .ToHashSet(StringComparer.Ordinal);
    }
}

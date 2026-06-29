using System.Globalization;
using Coffer.Application.Localization;
using Coffer.Core.Goals;

namespace Coffer.Application.ViewModels.Goals;

/// <summary>
/// One row in a goal's "what if you saved this much" table — a deterministic <see cref="Scenario"/>
/// from the engine, pre-formatted as display strings so the view stays markup-only. Status captions
/// and the monthly suffix are localized via the injected <see cref="ILocalizer"/>; money stays pl-PL.
/// The <see cref="StatusColor"/> hex is rendered through the shared brush converter for the badge.
/// </summary>
public sealed class GoalScenarioViewModel
{
    private static readonly CultureInfo _polish = CultureInfo.GetCultureInfo("pl-PL");

    public GoalScenarioViewModel(Scenario scenario, ILocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(localizer);

        Label = scenario.Label;
        MonthlySavingText = scenario.MonthlySaving.ToString("N2", _polish) + " zł" + localizer["Goal.PerMonthSuffix"];
        ProjectedDateText = GoalDisplay.IsUnreachable(scenario.ProjectedDate)
            ? localizer["Goal.ProjectedDate.Unreachable"]
            : GoalDisplay.FormatProjectedDate(scenario.ProjectedDate);
        StatusText = localizer[GoalDisplay.StatusKey(scenario.Status)];
        StatusColor = GoalDisplay.StatusToColor(scenario.Status);
    }

    public string Label { get; }

    public string MonthlySavingText { get; }

    public string ProjectedDateText { get; }

    public string StatusText { get; }

    public string StatusColor { get; }
}

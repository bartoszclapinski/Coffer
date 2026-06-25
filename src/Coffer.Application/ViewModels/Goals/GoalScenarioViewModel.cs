using System.Globalization;
using Coffer.Core.Goals;

namespace Coffer.Application.ViewModels.Goals;

/// <summary>
/// One row in a goal's "what if you saved this much" table — a deterministic <see cref="Scenario"/>
/// from the engine, pre-formatted as Polish display strings so the view stays markup-only. The
/// <see cref="StatusColor"/> hex is rendered through the shared brush converter for the badge.
/// </summary>
public sealed class GoalScenarioViewModel
{
    private static readonly CultureInfo _polish = CultureInfo.GetCultureInfo("pl-PL");

    public GoalScenarioViewModel(Scenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);

        Label = scenario.Label;
        MonthlySavingText = scenario.MonthlySaving.ToString("N2", _polish) + " zł/mies.";
        ProjectedDateText = GoalDisplay.FormatProjectedDate(scenario.ProjectedDate);
        StatusText = GoalDisplay.StatusToPolish(scenario.Status);
        StatusColor = GoalDisplay.StatusToColor(scenario.Status);
    }

    public string Label { get; }

    public string MonthlySavingText { get; }

    public string ProjectedDateText { get; }

    public string StatusText { get; }

    public string StatusColor { get; }
}

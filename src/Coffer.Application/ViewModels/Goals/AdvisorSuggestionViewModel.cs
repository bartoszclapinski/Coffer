using Coffer.Core.Domain;

namespace Coffer.Application.ViewModels.Goals;

/// <summary>
/// One AI cutting suggestion from the day's <see cref="AdvisorReport"/>, formatted for the Doradca
/// page. Every figure traces back to the engine and the 6-month category averages — the VM only
/// formats. The savings line is shown as "+N zł/mies." so the owner sees the monthly upside.
/// </summary>
public sealed class AdvisorSuggestionViewModel
{
    public AdvisorSuggestionViewModel(AdvisorSuggestion suggestion)
    {
        ArgumentNullException.ThrowIfNull(suggestion);

        Title = suggestion.Title;
        Description = suggestion.Description;
        CategoryText = suggestion.CategoryAffected ?? "";
        HasCategory = !string.IsNullOrWhiteSpace(suggestion.CategoryAffected);
        SavingsText = suggestion.Savings > 0m
            ? "+" + GoalDisplay.Money(suggestion.Savings) + "/mies."
            : "";
        HasSavings = suggestion.Savings > 0m;
    }

    public string Title { get; }

    public string Description { get; }

    public string CategoryText { get; }

    public bool HasCategory { get; }

    public string SavingsText { get; }

    public bool HasSavings { get; }
}

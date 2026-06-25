using System.Globalization;
using Coffer.Core.Goals;

namespace Coffer.Application.ViewModels.Goals;

/// <summary>
/// Shared Polish display formatting for the Doradca page — status/type/priority captions, badge
/// colours, and date strings — so the goal and scenario view-models stay consistent and the views
/// stay markup-only. Colours match the dashboard/alerts palette.
/// </summary>
internal static class GoalDisplay
{
    private static readonly CultureInfo _polish = CultureInfo.GetCultureInfo("pl-PL");

    public static string FormatProjectedDate(DateOnly date) =>
        date >= DateOnly.MaxValue ? "nieosiągalny" : date.ToString("d MMM yyyy", _polish);

    public static string StatusToPolish(GoalStatus status) => status switch
    {
        GoalStatus.OnTrack => "Na dobrej drodze",
        GoalStatus.NeedsAttention => "Wymaga uwagi",
        GoalStatus.AtRisk => "Zagrożony",
        GoalStatus.Late => "Po terminie",
        GoalStatus.Achieved => "Osiągnięty",
        GoalStatus.Paused => "Wstrzymany",
        _ => status.ToString(),
    };

    public static string StatusToColor(GoalStatus status) => status switch
    {
        GoalStatus.OnTrack => "#34C759",
        GoalStatus.Achieved => "#34C759",
        GoalStatus.NeedsAttention => "#FF9500",
        GoalStatus.AtRisk => "#FF3B30",
        GoalStatus.Late => "#FF3B30",
        GoalStatus.Paused => "#8E8E93",
        _ => "#8E8E93",
    };

    public static string TypeToPolish(GoalType type) => type switch
    {
        GoalType.Purchase => "Zakup",
        GoalType.LargeExpense => "Duży wydatek",
        GoalType.EmergencyFund => "Fundusz awaryjny",
        GoalType.MortgagePrepayment => "Nadpłata kredytu",
        GoalType.Investment => "Inwestycja",
        GoalType.LongTerm => "Cel długoterminowy",
        _ => type.ToString(),
    };

    public static string PriorityToPolish(Priority priority) => priority switch
    {
        Priority.Low => "Niski",
        Priority.Medium => "Średni",
        Priority.High => "Wysoki",
        _ => priority.ToString(),
    };

    public static string Money(decimal amount) => amount.ToString("N2", _polish) + " zł";
}

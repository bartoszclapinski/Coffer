using System.Globalization;
using Coffer.Core.Goals;

namespace Coffer.Application.ViewModels.Goals;

/// <summary>
/// Shared display helpers for the Doradca page — resource keys for status/type/priority captions,
/// badge colours, and money formatting — so the goal and scenario view-models stay consistent and
/// the views stay markup-only. The actual localized string lookup happens at the VM boundary via an
/// injected <c>ILocalizer</c>; this type only maps enums to keys. Colours and money formatting are
/// language-independent (money stays pl-PL, hard rule on currency formatting).
/// </summary>
internal static class GoalDisplay
{
    private static readonly CultureInfo _polish = CultureInfo.GetCultureInfo("pl-PL");

    /// <summary>Resource key for a projected date, or a pl-PL formatted date string.</summary>
    /// <remarks>
    /// Unreachable dates return the <c>Goal.ProjectedDate.Unreachable</c> key (resolve via localizer);
    /// reachable dates are formatted directly and returned as-is (no key).
    /// </remarks>
    public static string FormatProjectedDate(DateOnly date) =>
        date >= DateOnly.MaxValue ? "Goal.ProjectedDate.Unreachable" : date.ToString("d MMM yyyy", _polish);

    public static bool IsUnreachable(DateOnly date) => date >= DateOnly.MaxValue;

    public static string StatusKey(GoalStatus status) => status switch
    {
        GoalStatus.OnTrack => "Goal.Status.OnTrack",
        GoalStatus.NeedsAttention => "Goal.Status.NeedsAttention",
        GoalStatus.AtRisk => "Goal.Status.AtRisk",
        GoalStatus.Late => "Goal.Status.Late",
        GoalStatus.Achieved => "Goal.Status.Achieved",
        GoalStatus.Paused => "Goal.Status.Paused",
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

    public static string TypeKey(GoalType type) => type switch
    {
        GoalType.Purchase => "Goal.Type.Purchase",
        GoalType.LargeExpense => "Goal.Type.LargeExpense",
        GoalType.EmergencyFund => "Goal.Type.EmergencyFund",
        GoalType.MortgagePrepayment => "Goal.Type.MortgagePrepayment",
        GoalType.Investment => "Goal.Type.Investment",
        GoalType.LongTerm => "Goal.Type.LongTerm",
        _ => type.ToString(),
    };

    public static string PriorityKey(Priority priority) => priority switch
    {
        Priority.Low => "Goal.Priority.Low",
        Priority.Medium => "Goal.Priority.Medium",
        Priority.High => "Goal.Priority.High",
        _ => priority.ToString(),
    };

    public static string Money(decimal amount) => amount.ToString("N2", _polish) + " zł";
}

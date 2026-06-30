namespace Coffer.Core.Domain;

/// <summary>
/// A recurring cash flow the planner projects forward — a salary, a leasing instalment, a quarterly
/// tax, an insurance premium. Persisted and user-editable: the <see cref="AccrualOffsetMonths"/> is
/// the owner's domain knowledge (not derivable from bank data, which only records when money moved,
/// never which period a cost belongs to), and detection only proposes flows the owner then confirms.
/// <see cref="TypicalAmount"/> is a positive magnitude (decimal, rule #1) — the sign comes from
/// <see cref="Direction"/>. <see cref="Currency"/> is non-null (rule #9); <see cref="CreatedAt"/> is
/// a UTC system timestamp (rule #2).
/// </summary>
public class RecurringFlow
{
    public Guid Id { get; set; }

    public string Name { get; set; } = "";

    public FlowDirection Direction { get; set; }

    /// <summary>Merchant key this flow ties back to in history; null for manually entered flows.</summary>
    public string? MatchMerchant { get; set; }

    public Guid? MatchCategoryId { get; set; }

    /// <summary>Months between occurrences: 1 = monthly, 3 = quarterly, 12 = yearly.</summary>
    public int IntervalMonths { get; set; }

    /// <summary>Day of the month the payment/credit lands; clamped to the month's length when projected.</summary>
    public int AnchorDayOfMonth { get; set; }

    /// <summary>For <see cref="IntervalMonths"/> &gt; 1, the calendar month (1–12) an occurrence falls in; null for monthly.</summary>
    public int? AnchorMonth { get; set; }

    public decimal TypicalAmount { get; set; }

    public decimal AmountStdDev { get; set; }

    /// <summary>
    /// Months the cost belongs <i>before</i> its payment date. 0 = paid in the period it relates to;
    /// 1 = paid the month after (leasing/fuel/tax-on-prior-month). Drives accrual attribution, not the
    /// cash timeline (the payment date does that).
    /// </summary>
    public int AccrualOffsetMonths { get; set; }

    public string Currency { get; set; } = "PLN";

    public bool IsActive { get; set; }

    public FlowSource Source { get; set; }

    public DateTime CreatedAt { get; set; }
}

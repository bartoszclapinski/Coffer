namespace Coffer.Core.Domain;

/// <summary>
/// A monthly spending limit for one <see cref="Category"/>. <see cref="LimitAmount"/> is the intended
/// per-calendar-month cap (always <c>decimal</c>, hard rule #1) in <see cref="Currency"/> (non-null,
/// hard rule #9). One recurring limit applies every month — there is no per-month override in v1. This is
/// the owner's *spending* budget and is deliberately distinct from the AI *cost* cap behind
/// <c>AiBudgetGate</c>. <see cref="CreatedAt"/> is the UTC system timestamp.
/// </summary>
public class CategoryBudget
{
    public Guid Id { get; set; }

    public Guid CategoryId { get; set; }

    public Category? Category { get; set; }

    public decimal LimitAmount { get; set; }

    public string Currency { get; set; } = "PLN";

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }
}

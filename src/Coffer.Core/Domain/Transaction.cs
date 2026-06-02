namespace Coffer.Core.Domain;

/// <summary>
/// A single statement line. <see cref="Amount"/> is signed (negative = debit,
/// positive = credit) and always <c>decimal</c> (hard rule #1). <see cref="Date"/>
/// is the operation date as <see cref="DateOnly"/>; <see cref="CreatedAt"/> is the
/// UTC system timestamp. <see cref="Hash"/> deduplicates re-imports via
/// <see cref="TransactionHash"/>.
/// </summary>
public class Transaction
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    public Account Account { get; set; } = null!;

    public DateOnly Date { get; set; }

    public DateOnly? BookingDate { get; set; }

    public decimal Amount { get; set; }

    public string Currency { get; set; } = "PLN";

    public string Description { get; set; } = "";

    public string NormalizedDescription { get; set; } = "";

    public string? Merchant { get; set; }

    public Guid? CategoryId { get; set; }

    public Category? Category { get; set; }

    public string Hash { get; set; } = "";

    public Guid ImportSessionId { get; set; }

    public ImportSession ImportSession { get; set; } = null!;

    public DateTime CreatedAt { get; set; }
}

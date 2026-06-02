namespace Coffer.Core.Domain;

/// <summary>
/// One statement-import run. Groups the transactions it added and records the
/// file hash so re-importing an identical file can be detected and skipped.
/// </summary>
public class ImportSession
{
    public Guid Id { get; set; }

    public string FileName { get; set; } = "";

    public string FileHash { get; set; } = "";

    public string BankCode { get; set; } = "";

    public DateOnly PeriodFrom { get; set; }

    public DateOnly PeriodTo { get; set; }

    public DateTime ImportedAt { get; set; }

    public int TransactionsAdded { get; set; }

    public int TransactionsSkipped { get; set; }

    public ImportStatus Status { get; set; }
}

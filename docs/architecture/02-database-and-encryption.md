# 02 — Database and Encryption

## Database fundamentals

- **Engine:** SQLite via `Microsoft.Data.Sqlite`
- **Encryption:** SQLCipher via `SQLitePCLRaw.bundle_e_sqlcipher`
- **ORM:** Entity Framework Core 9
- **File location:**
  - Desktop: `%AppData%\Coffer\coffer.db`
  - Mobile: app's private storage (`FileSystem.AppDataDirectory` in MAUI)
- **Connection string:** built dynamically with key from `IKeyVault`

## SQLCipher setup — the gotcha that wastes hours

EF Core does NOT natively know about SQLCipher. The standard `UseSqlite` works, but you must set the encryption key manually on every connection. Wrong way: connection pool reuses connections without the key set.

### Correct pattern

```csharp
public sealed class CofferDbContext : DbContext
{
    private readonly string _key;

    public CofferDbContext(string key, DbContextOptions<CofferDbContext> options) : base(options)
    {
        _key = key;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        var connection = new SqliteConnection($"Data Source={DbPath};");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"PRAGMA key = '{_key}';"; // Note: parameterize properly in real code, key is binary not string
        cmd.ExecuteNonQuery();
        options.UseSqlite(connection);
    }
}
```

**Better pattern (production):** use `DbConnectionInterceptor` so every connection automatically gets `PRAGMA key` after open. Avoids passing the key through DbContext constructor.

```csharp
public sealed class SqlCipherKeyInterceptor(string base64Key) : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"PRAGMA key = \"x'{base64Key.HexEncode()}'\";"; // hex form is safest
        cmd.ExecuteNonQuery();
    }
}

// Registration
services.AddDbContext<CofferDbContext>((sp, opts) =>
{
    var keyVault = sp.GetRequiredService<IKeyVault>();
    var dek = keyVault.GetDatabaseEncryptionKey();
    opts.UseSqlite($"Data Source={DbPath};")
        .AddInterceptors(new SqlCipherKeyInterceptor(dek));
});
```

### Connection pool

Default EF Core pools connections. With SQLCipher, every connection in the pool needs `PRAGMA key` set. The interceptor above handles this. **Do not disable the pool** unless profiling shows it's a problem.

## Schema

### Core entities

```csharp
public class Account
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string BankCode { get; set; } = "";          // PKO_BP, MBANK, etc.
    public string AccountNumber { get; set; } = "";    // normalized: digits only with country prefix
    public string Currency { get; set; } = "PLN";       // ISO 4217
    public AccountType Type { get; set; }               // Checking, CreditCard, Savings, Mortgage, Investment
    public bool IsArchived { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<Tag> Tags { get; set; } = [];           // for grouping like 'mortgage-2022'
}

public class Transaction
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Account Account { get; set; } = null!;
    public DateOnly Date { get; set; }                  // operation date
    public DateOnly? BookingDate { get; set; }          // bank booking date, optional
    public decimal Amount { get; set; }                 // signed: negative = debit, positive = credit
    public string Currency { get; set; } = "PLN";
    public string Description { get; set; } = "";      // raw, as on statement
    public string NormalizedDescription { get; set; } = ""; // for matching/dedup
    public string? Merchant { get; set; }               // extracted, for rules
    public Guid? CategoryId { get; set; }
    public Category? Category { get; set; }
    public Guid? ReceiptId { get; set; }                // linked receipt if matched
    public Receipt? Receipt { get; set; }
    public string Hash { get; set; } = "";              // SHA256(date + amount + normDesc + accountNumber)
    public Guid ImportSessionId { get; set; }
    public ImportSession ImportSession { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public List<Tag> Tags { get; set; } = [];
    public List<TransactionSplit>? Splits { get; set; } // for receipt-based item-level splits
}

public class TransactionSplit
{
    public Guid Id { get; set; }
    public Guid TransactionId { get; set; }
    public Transaction Transaction { get; set; } = null!;
    public Guid? ReceiptItemId { get; set; }            // source if from receipt
    public string Description { get; set; } = "";
    public decimal Amount { get; set; }
    public Guid? CategoryId { get; set; }
}

public class Category
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#534AB7";      // CSS-style hex
    public Guid? ParentId { get; set; }                 // hierarchy if needed
    public bool IsArchived { get; set; }
}

public class Rule
{
    public Guid Id { get; set; }
    public int Priority { get; set; }                   // lower = higher priority
    public string Pattern { get; set; } = "";           // regex against NormalizedDescription
    public Guid CategoryId { get; set; }
    public bool IsEnabled { get; set; } = true;
}

public class Receipt
{
    public Guid Id { get; set; }
    public string ImagePath { get; set; } = "";        // local file path, encrypted at rest
    public DateOnly? ReceiptDate { get; set; }
    public TimeOnly? ReceiptTime { get; set; }
    public string? MerchantName { get; set; }
    public decimal? TotalAmount { get; set; }
    public string Currency { get; set; } = "PLN";
    public ReceiptStatus Status { get; set; }           // Pending, Processed, Matched, Unmatched
    public DateTime CapturedAt { get; set; }
    public Guid? MatchedTransactionId { get; set; }
    public List<ReceiptItem> Items { get; set; } = [];
}

public class ReceiptItem
{
    public Guid Id { get; set; }
    public Guid ReceiptId { get; set; }
    public string Name { get; set; } = "";
    public decimal? Quantity { get; set; }
    public decimal? UnitPrice { get; set; }
    public decimal Amount { get; set; }                 // line total after any item discount
    public Guid? CategoryId { get; set; }
}

public class ImportSession
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = "";
    public string FileHash { get; set; } = "";          // for duplicate-import detection
    public string BankCode { get; set; } = "";
    public DateOnly PeriodFrom { get; set; }
    public DateOnly PeriodTo { get; set; }
    public DateTime ImportedAt { get; set; }
    public int TransactionsAdded { get; set; }
    public int TransactionsSkipped { get; set; }        // duplicates
    public ImportStatus Status { get; set; }
}

public class Tag
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";              // e.g., 'mortgage-2022', 'holiday-2025'
    public string? Color { get; set; }
}
```

### Goals (advisor)

See `07-financial-advisor.md` for the full advisor model. Tables: `Goals`, `GoalContributions`, `GoalSnapshots`.

### Sync

See `06-sync-and-mobile.md` for the sync event log table.

## Indexes — non-negotiable

EF Core does not automatically index foreign keys or commonly-queried columns. Add explicitly:

```csharp
modelBuilder.Entity<Transaction>(e =>
{
    e.HasIndex(t => t.Date);
    e.HasIndex(t => t.CategoryId);
    e.HasIndex(t => t.AccountId);
    e.HasIndex(t => new { t.Date, t.AccountId });       // most common query shape
    e.HasIndex(t => t.Hash).IsUnique();                 // dedup
    e.HasIndex(t => t.NormalizedDescription);           // for category cache
});

modelBuilder.Entity<Receipt>(e =>
{
    e.HasIndex(r => r.ReceiptDate);
    e.HasIndex(r => r.Status);
    e.HasIndex(r => r.MatchedTransactionId);
});

modelBuilder.Entity<Rule>(e => e.HasIndex(r => r.Priority));

modelBuilder.Entity<Goal>(e => e.HasIndex(g => new { g.Status, g.Priority }));
```

## Decimal precision

Every monetary column:

```csharp
modelBuilder.Entity<Transaction>().Property(t => t.Amount).HasColumnType("decimal(18,2)");
```

Or globally via convention:

```csharp
configurationBuilder.Properties<decimal>().HavePrecision(18, 2);
```

## Migrations

### Strategy

- **Generated in development:** `dotnet ef migrations add NameInPascalCase` after schema changes.
- **Committed to repo:** in `Infrastructure/Persistence/Migrations/`.
- **Applied at app startup:** with confirmation dialog (see below).
- **Forward-only for additive changes.** Adding column, adding table, adding index = simple migration.
- **Expand-contract for destructive changes.** Two phases across two app versions:
  1. Add new column/table alongside old. Code reads/writes both. Migrate data.
  2. Next version: drop old column/table. Code uses only new.

### Pre-migration backup — required

Before EVERY `Database.Migrate()` call:

```csharp
public class MigrationRunner
{
    public async Task<MigrationResult> RunPendingMigrationsAsync(CofferDbContext db, IBackupService backup)
    {
        var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
        if (pending.Count == 0) return MigrationResult.UpToDate;

        // Mandatory pre-migration backup, retained 90 days, in dedicated subfolder
        await backup.CreatePreMigrationSnapshotAsync(db.Database.GetDbConnection().DataSource);

        // User confirms in UI before this point
        await db.Database.MigrateAsync();
        await db.SchemaInfo.AddAsync(new SchemaInfoEntry
        {
            Version = pending.Last(),
            MigratedAt = DateTime.UtcNow,
            AppVersion = AppInfo.Version
        });
        await db.SaveChangesAsync();

        return MigrationResult.Migrated(pending);
    }
}
```

### User-facing flow

On app startup:

1. Check `GetPendingMigrationsAsync()`.
2. If any: show modal dialog "Database update required. A backup will be created automatically. Continue?"
3. User confirms → backup → migrate → log to `_SchemaInfo`.
4. User cancels → app closes (cannot run with pending migrations because new code expects new schema).

### Schema version tracking

Custom table `_SchemaInfo`:

```csharp
public class SchemaInfoEntry
{
    public int Id { get; set; }
    public string Version { get; set; } = "";
    public DateTime MigratedAt { get; set; }
    public string AppVersion { get; set; } = "";
}
```

Allows manually rolling back: pick a backup from before a specific schema version.

### Downgrade detection

If `_SchemaInfo` shows a version newer than what the running app code knows, refuse to start:

> "Your data was created with Coffer version X.Y. Please update the app or restore from a backup."

This prevents data corruption from running an old app against a new schema.

### SQLite ALTER TABLE limits

SQLite has limited `ALTER TABLE`. EF Core's "rebuild table" handles complex changes by copying data to a new table. **In SQLCipher this is slow** for large tables (~30 seconds for 100k rows).

For destructive migrations on tables that may be large (`Transactions`, `SyncEvents`):
- Show progress UI
- Block app close during migration
- Test on realistic data sizes before release

## Querying patterns

### Always async

```csharp
var transactions = await db.Transactions
    .Where(t => t.Date >= from && t.Date <= to)
    .Include(t => t.Category)
    .OrderByDescending(t => t.Date)
    .ToListAsync(ct);
```

### Aggregate in SQL, not in memory

Bad:
```csharp
var all = await db.Transactions.ToListAsync();          // loads everything
var sums = all.GroupBy(t => t.CategoryId).Select(g => new { g.Key, Total = g.Sum(t => t.Amount) });
```

Good:
```csharp
var sums = await db.Transactions
    .Where(t => t.Date >= from && t.Date <= to)
    .GroupBy(t => t.CategoryId)
    .Select(g => new { CategoryId = g.Key, Total = g.Sum(t => t.Amount) })
    .ToListAsync(ct);
```

### AsNoTracking for read-only

For lists shown in UI without editing:

```csharp
db.Transactions.AsNoTracking().Where(...).ToListAsync();
```

### Default 6-month window for transaction list

Per UX decision, the transactions view defaults to last 6 months. Filter UI lets user expand. This keeps memory usage predictable and queries fast on ~12k+ transaction histories.

```csharp
var sixMonthsAgo = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-6));
var transactions = await db.Transactions
    .AsNoTracking()
    .Where(t => t.Date >= sixMonthsAgo)
    .OrderByDescending(t => t.Date)
    .ToListAsync(ct);
```

## DataGrid virtualization

Avalonia and MAUI both virtualize `DataGrid` / `CollectionView` by default. Things that break virtualization:
- Wrapping the grid in a `ScrollViewer` (the grid does its own scrolling).
- Setting fixed `MaxHeight` smaller than content.
- Using `<ItemsControl>` with `<StackPanel>` IsVirtualizing="False".

Always use `VirtualizingStackPanel` or the framework's built-in virtualization mode.

## Concurrency

App is single-user, but background workers (sync, daily backup, snapshot generation for goals) run alongside the UI. Use:

- `IDbContextFactory<CofferDbContext>` for short-lived contexts in workers
- One context per logical unit of work, never shared across threads
- `using var db = await _factory.CreateDbContextAsync();`

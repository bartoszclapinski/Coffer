# Code conventions

These are the conventions for this codebase. The `.editorconfig` enforces most of them; this document explains the why and adds rules `.editorconfig` cannot express.

## Language

- **All code, comments, identifiers, and docs are in English.** UI labels and prompts shown to the user are in Polish.
- Don't mix languages within a file.
- Polish-only data (transaction descriptions, merchant names) is fine — that's data, not code.

## Naming

### General

- Be explicit. `transactions` over `tx`, `transaction` over `t`, `transactionId` over `txId`.
- Exception: well-known short names in tight scopes are fine (`var sum = items.Sum(i => i.Amount)`).
- No Hungarian notation. No `m_`, `_`, or `s_` prefixes for plain fields (use C# conventions: `_camelCase` for private fields, `PascalCase` for everything else).

### Specific

| Kind | Convention | Example |
|---|---|---|
| Class, struct, enum, record | `PascalCase` | `TransactionRepository` |
| Interface | `IPascalCase` | `IStatementParser` |
| Method | `PascalCase` | `ImportStatementAsync` |
| Async methods | suffix `Async` | `ParseAsync` |
| Public properties | `PascalCase` | `TargetAmount` |
| Private fields | `_camelCase` | `_dbContext` |
| Local variables | `camelCase` | `var totalAmount = ...` |
| Constants | `PascalCase` | `const int MaxRetries = 3` |
| Enum members | `PascalCase` | `GoalStatus.OnTrack` |
| Test methods | `MethodName_Scenario_ExpectedResult` | `Categorize_KnownDescription_ReturnsCachedCategory` |
| Files | match the primary type | `TransactionRepository.cs` |

### Boolean naming

Use predicates: `IsArchived`, `HasReceipt`, `CanProceed`, `WasSuccessful`. Not `Active`, `Receipt` or `Success`.

### Collection naming

Pluralize: `Transactions`, `Categories`. For dictionaries, mention key→value: `categoryById`, `pricesByItem`.

## File organization

- One public type per file (small private helpers may share).
- File name matches the type name.
- Folders mirror namespaces. `Coffer.Infrastructure.Parsers.Pko` lives in `Infrastructure/Parsers/Pko/`.
- Group related types in subfolders: `Receipts/`, `Goals/`, `Sync/`. Avoid catch-all `Helpers/` folders unless the helper is genuinely cross-cutting.

## Style

### `var` vs explicit type

Use `var` when the right-hand side makes the type obvious (object initialization, casts, well-known LINQ results). Use explicit types when the type is non-obvious or matters for understanding.

```csharp
var transactions = await db.Transactions.ToListAsync();        // obvious
var matcher = new ReceiptMatcher(...);                          // obvious
decimal totalAmount = items.Sum(i => i.Amount);                 // explicit makes intent clear
IStatementParser parser = registry.Resolve(fingerprint);        // explicit emphasizes the abstraction
```

### Expression-bodied members

Use them for one-liners. Don't force them when they hurt readability.

```csharp
public string FullName => $"{FirstName} {LastName}";              // good

public decimal CalculateTotalWithDiscount(IEnumerable<LineItem> items, decimal discountPercent) =>
    items.Sum(i => i.Amount * i.Quantity) * (1 - discountPercent / 100);    // OK but a regular method body is fine too
```

### `null` handling

- Prefer non-nullable references where possible. Enable `<Nullable>enable</Nullable>` in every project.
- Use null-forgiving (`!`) sparingly and only when justified. Add a comment explaining the invariant.
- Use `ArgumentNullException.ThrowIfNull(arg)` over manual checks for non-nullable arguments at API boundaries.
- Use `??` and `??=` for default values.

### Async

- All I/O methods are `async` and return `Task` / `Task<T>` / `ValueTask` / `IAsyncEnumerable<T>`.
- Pass `CancellationToken ct` as the last parameter. Never default it on internal/library methods.
- `ConfigureAwait(false)` is **not** required in app code — it's primarily for library code where you don't control sync context.
- Don't mix `.Result` or `.Wait()` with `async`. If you need a sync entry point, document the deadlock risk explicitly and call `.GetAwaiter().GetResult()` only at the very edge (e.g., `Main`).

### LINQ

- Prefer LINQ for readability over imperative loops, except when:
  - Profiling shows a hot path
  - The loop has multiple side effects that obscure the LINQ chain
- Avoid `ToList()` mid-chain unless you need to materialize.
- Use `AsNoTracking()` on EF queries for read-only data.

### Exception handling

- Don't catch `Exception` unless you immediately rethrow or log + return a typed result.
- Specific catches > broad catches.
- Don't use exceptions for control flow.
- Domain operations return `Result<T>` types, not throw, for expected failures.

```csharp
public sealed class Result<T>
{
    public bool IsSuccess { get; }
    public T? Value { get; }
    public string? Error { get; }
    public static Result<T> Ok(T value) => new(true, value, null);
    public static Result<T> Fail(string error) => new(false, default, error);
    private Result(bool ok, T? value, string? error) { IsSuccess = ok; Value = value; Error = error; }
}
```

Use this for parser results, AI calls that may legitimately fail, etc. Keep exceptions for "should never happen" cases.

## Comments

- Don't comment what the code does. Comment why it does it.
- TODO comments: include a date and reason. `// TODO 2026-05-10: revisit when EF Core 10 lands`
- Public APIs (anything `public` in non-internal projects) get XML doc comments.
- Don't write doc comments that just restate the method name.

## Testing

### Structure

```csharp
[Fact]
public async Task ImportStatement_DuplicateFile_SkipsAlreadyImportedTransactions()
{
    // Arrange
    var fixture = new TestFixture();
    await fixture.ImportAsync("statement-2025-11.pdf");

    // Act
    var second = await fixture.ImportAsync("statement-2025-11.pdf");

    // Assert
    second.TransactionsAdded.Should().Be(0);
    second.TransactionsSkipped.Should().BeGreaterThan(0);
}
```

### Naming

`MethodOrFeature_Scenario_ExpectedOutcome` — the test name should describe the assertion in English.

### One assertion per test, mostly

When testing multiple aspects of one behavior, use FluentAssertions' fluent chains (`.Should().NotBeNull().And.HaveCount(5)`). Resist the urge to mass-assert unrelated things.

### Test data

- Use `Bogus` for synthetic data: `new Faker<Transaction>()...`
- Use real anonymized statements for parser tests (golden files)
- For property-based testing of pure functions, use FsCheck

### Coverage targets

- `Coffer.Core` and `Coffer.Application`: aim for 80%+ line coverage of business logic. Pure C#, easy to test.
- `Coffer.Infrastructure`: cover parsers thoroughly via golden files, cover AI providers' deserialization paths
- UI projects: minimal unit tests; visual smoke tests as needed

## DI registration

Group registrations by concern in extension methods:

```csharp
public static IServiceCollection AddStatementParsers(this IServiceCollection services)
{
    services.AddSingleton<IBankDetector, FingerprintBankDetector>();
    services.AddSingleton<IStatementParser, PkoBpStatementParser>();
    services.AddSingleton<IStatementParser, MBankStatementParser>();
    services.AddSingleton<IStatementParser, AiAssistedParser>();
    services.AddSingleton<StatementParserRegistry>();
    return services;
}
```

Keep `Program.cs` / `MauiProgram.cs` calling these high-level extensions:

```csharp
services
    .AddCofferCore()
    .AddCofferInfrastructure(config)
    .AddStatementParsers()
    .AddAiProviders()
    .AddSyncServices();
```

## Avoid

- AutoMapper / similar reflection-based mappers — they hide bugs. Hand-rolled mappers are fast to write, easy to read, easy to debug.
- Static service locators. DI everything.
- Singletons that hold mutable shared state — leads to hidden coupling.
- "Util" classes with random unrelated methods. If you can't name the class with a clear noun, you don't have a class.
- Premature abstractions. If there's only one implementation today, an interface is optional unless you need it for testing.

## File header

No file headers required. Don't add license or copyright headers (this is a personal project; not needed). Don't add `// Created by ...` comments.

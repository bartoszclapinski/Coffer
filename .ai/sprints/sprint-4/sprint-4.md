# Sprint 4 — CofferDbContext + SQLCipher + first migration

**Phase:** 0 (Foundation)
**Status:** In progress
**Depends on:** sprint-3

## Goal

Register EF Core 9 + SQLCipher in `Coffer.Infrastructure`. Ship `CofferDbContext` with a single `_SchemaInfo` table (further tables arrive in Phase 1+). `SqlCipherKeyInterceptor` executes `PRAGMA key` on every opened connection, solving the connection-pool problem documented in [02-database-and-encryption.md](../../../docs/architecture/02-database-and-encryption.md). Generate and commit the first migration `InitialCreate`. `MigrationRunner` carries an optional pre-migration backup callback (hook for the future backup service). Integration tests verify: round-trip with the correct DEK, failure with a different DEK, encryption (raw bytes do not contain plaintext), and the backup-then-migrate ordering.

## Strategy

Sprint 4 lays the **persistence foundation** but **does not fully wire DI for the DEK** — that arrives in Sprint 5 (setup wizard), which will call `AddCofferDatabase(services, sp => ...)` with a real DEK source. Sprint 4 ships the `AddCofferDatabase` extension but does not call it from `AddCofferInfrastructure` automatically.

`SchemaInfoEntry` lives in `Coffer.Infrastructure/Persistence/` — it is a pure EF Core migration-tracking artefact, not domain state. Domain entities (`Transaction`, `Account`, `Category`, ...) join the schema progressively from Phase 1 onward.

Migrations are generated through a locally-pinned `dotnet-ef` tool (via a `tool-manifest`) for reproducibility across CI and developer machines.

Three PRs in the established issue-per-PR workflow (issue #10):
1. **Plan** (`chore/plan-sprint-4`, issue #18) — this document
2. **Implementation** (`feature/sprint-4-dbcontext-sqlcipher`, new issue) — code, migration, integration tests
3. **Closure** (`chore/close-sprint-4`, new issue) — post-merge bookkeeping

## Steps

### A. NuGet packages and tooling

- [x] 4.1 `Coffer.Infrastructure` — add `Microsoft.EntityFrameworkCore` (`9.*`), `Microsoft.EntityFrameworkCore.Sqlite` (`9.*`), `Microsoft.EntityFrameworkCore.Design` (`9.*`), `SQLitePCLRaw.bundle_e_sqlcipher` (`2.*`)
- [x] 4.2 `dotnet new tool-manifest` at the repo root + `dotnet tool install dotnet-ef --version 9.*` — local tool for migration generation. Commit `.config/dotnet-tools.json`.
- [x] 4.3 `tests/Coffer.Infrastructure.Tests` — no new packages (MS.DI concrete + xUnit + FluentAssertions already present)

### B. Schema entity

- [x] 4.4 `Coffer.Infrastructure/Persistence/SchemaInfoEntry.cs`:
  - `public class SchemaInfoEntry`
  - `int Id { get; set; }` (PK auto-increment)
  - `string Version { get; set; } = "";` (EF migration name, e.g. `"InitialCreate"`)
  - `DateTime MigratedAt { get; set; }` (UTC)
  - `string AppVersion { get; set; } = "";` (resolved at runtime from `Assembly.GetEntryAssembly().GetName().Version`, fallback `"0.0.0"`)

### C. DbContext and encryption interceptor

- [x] 4.5 `Coffer.Infrastructure/Persistence/CofferDbContext.cs`:
  - `public sealed class CofferDbContext : DbContext`
  - Constructor `(DbContextOptions<CofferDbContext> options) : base(options)` — DEK is delivered through the interceptor, not the constructor, per [02-database-and-encryption.md](../../../docs/architecture/02-database-and-encryption.md)
  - `DbSet<SchemaInfoEntry> SchemaInfo`
  - `OnModelCreating`: table name `_SchemaInfo`, PK `Id`, unique index on `Version`
  - Global decimal precision via `ConfigureConventions`: `configurationBuilder.Properties<decimal>().HavePrecision(18, 2)` — ready for future monetary fields per hard rule #1
- [x] 4.6 `Coffer.Infrastructure/Persistence/Encryption/SqlCipherKeyInterceptor.cs`:
  - Subclass of `DbConnectionInterceptor`
  - Constructor `(byte[] dek)` — holds DEK as a field for the interceptor's lifetime
  - Override `ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)`:
    1. `using var cmd = connection.CreateCommand();`
    2. `cmd.CommandText = $"PRAGMA key = \"x'{Convert.ToHexString(_dek)}'\";"` (hex form per the architecture doc)
    3. `cmd.ExecuteNonQuery();`
  - Memory hygiene: DEK is held as a `byte[]` field. The hex string is short-lived inside the command text. Strings are immutable in .NET — we accept the known limitation per [09-security-key-management.md](../../../docs/architecture/09-security-key-management.md).
- [x] 4.7 `Coffer.Infrastructure/Security/CofferPaths.cs` — add `DatabaseFile()` returning `Path.Combine(LocalAppDataFolder(), "coffer.db")`

### D. Migration infrastructure

- [x] 4.8 `Coffer.Infrastructure/Persistence/CofferDbContextDesignFactory.cs`:
  - `IDesignTimeDbContextFactory<CofferDbContext>`
  - `CreateDbContext(string[] args)` returns a DbContext configured with `UseSqlite("Data Source=:memory:")` without SQLCipher — `dotnet ef migrations add` does not require a real connection or DEK
- [x] 4.9 Generate the first migration: `dotnet ef migrations add InitialCreate --project src/Coffer.Infrastructure`
  - Produces `Migrations/<timestamp>_InitialCreate.cs`, `<timestamp>_InitialCreate.Designer.cs`, `CofferDbContextModelSnapshot.cs`
  - All migration files committed to the repo
- [x] 4.10 `Coffer.Infrastructure/Persistence/MigrationResult.cs`:
  - `public sealed record MigrationResult(MigrationStatus Status, IReadOnlyList<string> AppliedMigrations)`
  - `public enum MigrationStatus { UpToDate, Migrated }`
  - Static helpers `UpToDate()` and `Migrated(IEnumerable<string> applied)`
- [x] 4.11 `Coffer.Infrastructure/Persistence/MigrationRunner.cs`:
  - Constructor: `(CofferDbContext db, ILogger<MigrationRunner> logger, Func<CancellationToken, Task>? preMigrationBackup = null)`
  - `Task<MigrationResult> RunPendingMigrationsAsync(CancellationToken ct)`:
    1. `var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList()`
    2. If `pending.Count == 0` → log info "no pending migrations" → return `MigrationResult.UpToDate()`
    3. Log info `"Running {Count} pending migration(s): {Migrations}"`
    4. If `preMigrationBackup is not null` → invoke and await
    5. `await db.Database.MigrateAsync(ct)`
    6. Append `SchemaInfoEntry { Version = pending.Last(), MigratedAt = DateTime.UtcNow, AppVersion = ResolveAppVersion() }`
    7. `await db.SaveChangesAsync(ct)`
    8. Return `MigrationResult.Migrated(pending)`
  - Helper `ResolveAppVersion()` → `Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "0.0.0"`

### E. DI registration (caller-invoked, not automatic)

- [x] 4.12 `Coffer.Infrastructure/DependencyInjection/ServiceRegistration.cs` — add `AddCofferDatabase(this IServiceCollection services, Func<IServiceProvider, byte[]> dekProvider)`:
  ```csharp
  services.AddDbContextFactory<CofferDbContext>((sp, opts) =>
  {
      var dek = dekProvider(sp);
      var dbPath = CofferPaths.DatabaseFile();
      Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
      opts.UseSqlite($"Data Source={dbPath};")
          .AddInterceptors(new SqlCipherKeyInterceptor(dek));
  });
  return services;
  ```
  - `AddCofferInfrastructure` **does not** invoke `AddCofferDatabase` automatically — Sprint 5 setup wizard will call it explicitly once the master key is derived and the DEK is decrypted from the DekFile
- [x] 4.13 Register `MigrationRunner` as Transient inside `AddCofferDatabase` after `AddDbContextFactory`

### F. Integration tests

- [x] 4.14 `tests/Coffer.Infrastructure.Tests/Persistence/CofferDbContextEncryptionTests.cs` (`IDisposable` cleanup, temp folder pattern from Sprint 2 DPAPI tests):
  - 4.14.a `WriteThenRead_WithSameDek_RoundTripsEntry` — create DB with a test DEK, write a `SchemaInfoEntry`, close, reopen with the same DEK, read back
  - 4.14.b `Read_WithDifferentDek_Throws` — write with DEK1, attempt to open with DEK2 → `SqliteException` (SQLCipher returns "file is not a database" or similar)
  - 4.14.c `RawFileBytes_DoNotContainPlaintextVersionString` — encryption sanity check: after writing an entry with `Version = "TEST_SENTINEL_12345"`, the raw DB file bytes must not contain that string
- [x] 4.15 `tests/Coffer.Infrastructure.Tests/Persistence/MigrationRunnerTests.cs` (`IDisposable` cleanup):
  - 4.15.a `Run_WithPendingMigrations_InvokesBackupCallbackBeforeMigrate` — verify ordering via a recorded list (`["backup", "migrate-completed"]`)
  - 4.15.b `Run_WhenNoPendingMigrations_DoesNotInvokeBackupCallback`
  - 4.15.c `Run_AfterSuccessfulMigration_AppendsSchemaInfoEntry` — after `RunPendingMigrationsAsync`, `_SchemaInfo` contains an entry with `Version = "InitialCreate"` (or the actual generated name)
  - 4.15.d `Run_ReturnsMigratedResult_WithAppliedMigrationNames`

### G. Validation and merge

- [x] 4.16 `dotnet build` + `dotnet test` + `dotnet format --verify-no-changes` green locally
- [x] 4.17 `gh issue create` for implementation — title `feat(sprint-4): CofferDbContext + SqlCipherKeyInterceptor + InitialCreate migration`, labels `feat` + `sprint-4`
- [ ] 4.18 Commit on `feature/sprint-4-dbcontext-sqlcipher`, push, `gh pr create` with `Closes #<impl-issue>` in the body, labels `feat` + `sprint-4`
- [ ] 4.19 CI green (Konscious and NBitcoin from earlier sprints still pass; SQLCipher is cross-platform), squash-merge, branch deleted
- [ ] 4.20 `gh issue create` for closure → separate `chore/close-sprint-4` PR analogous to Sprints 1-3

## Definition of Done

1. 5 new files in `Coffer.Infrastructure/Persistence/`: `SchemaInfoEntry.cs`, `CofferDbContext.cs`, `CofferDbContextDesignFactory.cs`, `MigrationResult.cs`, `MigrationRunner.cs`
2. 1 new file in `Coffer.Infrastructure/Persistence/Encryption/`: `SqlCipherKeyInterceptor.cs`
3. `CofferPaths.DatabaseFile()` returns `%LocalAppData%/Coffer/coffer.db`
4. Migration files in `Coffer.Infrastructure/Persistence/Migrations/` (`*_InitialCreate.cs`, Designer, ModelSnapshot) committed
5. Tool manifest `.config/dotnet-tools.json` committed (with `dotnet-ef` `9.*`)
6. DI: `AddCofferDatabase(IServiceCollection, Func<IServiceProvider, byte[]> dekProvider)` extension, **not** called from `AddCofferInfrastructure` automatically (Sprint 5 will call it explicitly)
7. **7 new integration tests pass** locally and on CI (Ubuntu): 3 for encryption + 4 for `MigrationRunner`. Combined with the existing 40 tests = ~47 total.
8. `dotnet build` (0 warnings, 0 errors), `dotnet test` green, `dotnet format` green
9. PR squash-merged, implementation issue auto-closed; closure PR also merged with its own issue
10. `Coffer.Core` stays clean — no EF Core / SQLCipher / Microsoft.Data.Sqlite dependencies

## Files affected

**New:**
- `.config/dotnet-tools.json`
- `src/Coffer.Infrastructure/Persistence/SchemaInfoEntry.cs`
- `src/Coffer.Infrastructure/Persistence/CofferDbContext.cs`
- `src/Coffer.Infrastructure/Persistence/CofferDbContextDesignFactory.cs`
- `src/Coffer.Infrastructure/Persistence/MigrationResult.cs`
- `src/Coffer.Infrastructure/Persistence/MigrationRunner.cs`
- `src/Coffer.Infrastructure/Persistence/Encryption/SqlCipherKeyInterceptor.cs`
- `src/Coffer.Infrastructure/Persistence/Migrations/<timestamp>_InitialCreate.cs` (auto-generated)
- `src/Coffer.Infrastructure/Persistence/Migrations/<timestamp>_InitialCreate.Designer.cs` (auto-generated)
- `src/Coffer.Infrastructure/Persistence/Migrations/CofferDbContextModelSnapshot.cs` (auto-generated)
- `tests/Coffer.Infrastructure.Tests/Persistence/CofferDbContextEncryptionTests.cs`
- `tests/Coffer.Infrastructure.Tests/Persistence/MigrationRunnerTests.cs`

**Modified:**
- `src/Coffer.Infrastructure/Coffer.Infrastructure.csproj` — PackageReferences for EF Core + SQLCipher
- `src/Coffer.Infrastructure/Security/CofferPaths.cs` — `DatabaseFile()` helper
- `src/Coffer.Infrastructure/DependencyInjection/ServiceRegistration.cs` — `AddCofferDatabase` extension
- `.ai/sprints/sprint-4/sprint-4.md` — checkboxes, status
- `.ai/sprints/sprint-4/log.md` — progress
- `.ai/sprints/index.md` — status

## Open questions

1. **`SchemaInfoEntry` in `Coffer.Core` or `Coffer.Infrastructure`?** It is purely EF migration tracking, not domain state.
   - **Recommendation:** **`Coffer.Infrastructure/Persistence/`** — pure persistence concern. Domain entities (`Transaction`, `Account`, ...) will live in `Coffer.Core` starting from Phase 1.

2. **Should `AddCofferInfrastructure` invoke `AddCofferDatabase` automatically?**
   - Pro: consistency with the other service registrations.
   - Contra: there is no DEK source yet; Sprint 5 setup wizard will have to call it anyway.
   - **Recommendation:** separate `AddCofferDatabase(services, dekProvider)` that Sprint 5 invokes explicitly.

3. **`MigrationRunner.preMigrationBackup` — interface `IBackupService` or delegate `Func<CancellationToken, Task>`?**
   - An interface would require an implementation now; backup service is out of scope for Phase 0.
   - A delegate is lightweight; Sprint 8+ adds a real backup service, before then the callback can be null or a no-op.
   - **Recommendation:** **delegate**. Promote to a full `IBackupService` interface when the backup service ships.

4. **`MigrationResult` — record or enum?**
   - An enum is simple (`UpToDate | Migrated`) but loses information about which migrations actually ran.
   - A record (status + list of applied migration names) is richer.
   - **Recommendation:** **record**. Sprint 6 UI dialog may want to show "what just happened".

5. **Memory hygiene for the DEK inside `SqlCipherKeyInterceptor`?**
   - The interceptor holds the DEK as a `byte[]` field for its lifetime (singleton-ish via the factory). Converting to hex creates an immutable string used in the command text.
   - **Recommendation:** accept the known limitation. The DEK has to live for every connection. String memory limitations per [09-security-key-management.md](../../../docs/architecture/09-security-key-management.md).

6. **DEK rotation in Sprint 4?**
   - What if the master password changes — that re-encrypts the DEK file with a new master key; the DEK itself does not change (rotating the DEK would mean re-encrypting all data).
   - **Recommendation:** **out of scope for Sprint 4**. Master password change is a later sprint. Full DEK rotation may never be needed.

7. **`CofferDbContextDesignFactory` — in-memory SQLite or fake DEK?**
   - In-memory `Data Source=:memory:` without SQLCipher is enough for `dotnet ef migrations add` (EF tools do not need a real connection or DEK).
   - **Recommendation:** **in-memory without SQLCipher** — clean, no DEK ceremony when generating migrations.

8. **`MigrationRunner` callback when the DB does not exist yet (fresh install)?**
   - First install: DB file is missing, `pending = ["InitialCreate"]`. There is nothing to back up.
   - **Recommendation:** callback **invoked whenever `pending > 0`**. Decision "is there anything to back up" lives with the caller. Sprint 5 setup wizard will pass `preMigrationBackup: null` (fresh install). Sprint 8+ backup service will pass real backup logic.

9. **EF Core tool — global or local?**
   - Local (`dotnet new tool-manifest` + `dotnet tool install dotnet-ef`) is reproducible on CI and every developer machine. Global requires manual installation.
   - **Recommendation:** **local manifest**. `.config/dotnet-tools.json` lives in the repo, every clone runs `dotnet tool restore`.

10. **`SQLitePCLRaw.bundle_e_sqlcipher` provider initialization?**
    - The bundle auto-registers the provider via assembly attributes — adding the package alone should be sufficient. EF Core discovers providers.
    - **Recommendation:** **no explicit `Batteries_V2.Init()`** — the package handles it. If the implementation discovers it is required, add then.

## Notes

- This sprint's plan goes through its own PR (`chore/plan-sprint-4`, issue #18). Implementation has its own branch and issue; closure too.
- Hard rule #3: `Coffer.Core` does not take EF Core or SQLCipher. Infrastructure only. Domain entities will land in Core from Phase 1.
- Hard rule #1 (decimal precision): `Properties<decimal>().HavePrecision(18, 2)` is applied globally in `ConfigureConventions`, ready for future monetary fields.
- Hard rule #8 (every migration runs pre-migration-backup): `MigrationRunner` provides the mechanism. The actual backup service ships in Sprint 8+ / Phase 2.
- Sprint 4 does not yet wire `IKeyVault` ↔ DEK source ↔ DbContext together — Sprint 5 (setup wizard) performs the end-to-end wire-up: master password → Argon2 → master key → DPAPI cache → AES-GCM decrypt DekFile → DEK → SQLCipher `PRAGMA key`.
- Sprint plans and logs from Sprint 0 through Sprint 3 are still in Polish; translating them is a separate chore PR if desired.

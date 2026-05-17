# Sprint 5 — Avalonia setup wizard

**Phase:** 0 (Foundation)
**Status:** Planned
**Depends on:** sprint-1 (DI bootstrap), sprint-2 (`IKeyVault`), sprint-3 (Argon2/BIP39/AES-GCM/DekFile), sprint-4 (`CofferDbContext`/`MigrationRunner`)

## Goal

First-run experience: cold start detects "no DEK file" and presents a 5-step setup wizard. The user creates a master password (validated by zxcvbn), is shown a 12-word BIP39 mnemonic on a screen-capture-protected window, verifies they recorded it (words #3 and #7), and confirms. The wizard then derives the master key (Argon2id), generates a random DEK, AES-GCM-encrypts the DEK with the master key, writes `dek.encrypted`, caches the master key via `IKeyVault` (7-day DPAPI TTL on Windows), publishes the DEK to an `IDekHolder` so `CofferDbContext` can open the encrypted database, runs the `InitialCreate` migration via `MigrationRunner`, and finally swaps to `MainWindow`. This is the **first sprint with real interactive UI** — the end of the placeholder-window era.

## Strategy

Sprint 5 is an integration sprint: no new cryptographic primitives, no new storage formats. Every piece exists already; this sprint composes them with UI on top.

- **MVVM via CommunityToolkit.Mvvm** — first sprint that actually needs ViewModels with bindable state and commands. Source generators (`[ObservableProperty]`, `[RelayCommand]`) live in `Coffer.Application`; views in `Coffer.Desktop` bind to them.
- **`IDekHolder`** is the missing piece that bridges Sprint 4's lazy `dekProvider` and the runtime reality that the DEK is only known after setup (or login). A simple in-memory holder; throws when read before written. `AddCofferDatabase` resolves DEK through it.
- **`SetupService`** is the orchestrator that strings Argon2 → DEK → AES-GCM → DekFile → KeyVault cache → DbContext open → MigrationRunner. Lives in `Coffer.Infrastructure` because it consumes concrete infrastructure types (`AesGcmCrypto`, `DekFile`, `MigrationRunner`); the ViewModel sees it through `ISetupService` in `Coffer.Core/Security`.
- **`SetWindowDisplayAffinity`** for the seed-display window is Win32 P/Invoke — lives in `Coffer.Desktop` behind an `IScreenCaptureBlocker` interface in Core. Non-Windows hosts get a no-op implementation.
- **Routing in `Program.cs`** — cold start checks `File.Exists(CofferPaths.EncryptedDekFilePath())`. Missing → setup wizard. Exists → placeholder "Login coming in Sprint 6" message and exit (the actual login flow is Sprint 6's scope).
- **UI strings are Polish** per `docs/conventions.md` (user-facing app text). All code, identifiers, comments, and tests stay English per the language policy (memory entry).
- **Visual design is functional, not polished** — Sprint 5 ships working flow, not a brand identity. Design tokens from `docs/mockups/shared/design-tokens.css` can be lightly applied if time permits but are not a goal.

Three PRs in the established issue-per-PR workflow:
1. **Plan** (`chore/plan-sprint-5`, issue #24) — this document
2. **Implementation** (`feature/sprint-5-setup-wizard`, new issue) — code + ViewModels + Views + tests
3. **Closure** (`chore/close-sprint-5`, new issue) — post-merge bookkeeping

## Steps

### A. NuGet packages

- [ ] 5.1 `Coffer.Application` — add `CommunityToolkit.Mvvm` (`8.*`) for `[ObservableProperty]`, `[RelayCommand]`, `ObservableObject`
- [ ] 5.2 `Coffer.Infrastructure` — add `zxcvbn-core` (latest stable on NuGet) for password-strength scoring

### B. DEK holder bridge

- [ ] 5.3 `Coffer.Core/Security/IDekHolder.cs` — interface with `byte[] Dek { get; set; }` and `bool IsAvailable { get; }`
- [ ] 5.4 `Coffer.Infrastructure/Security/DekHolder.cs` — thread-safe in-memory holder; getter throws `InvalidOperationException` until set; setter clears any previous bytes (`Array.Clear`) before overwriting
- [ ] 5.5 Register `IDekHolder` as Singleton in `AddCofferInfrastructure`
- [ ] 5.6 Update `AddCofferDatabase` (Sprint 4) to default the `dekProvider` parameter to `sp => sp.GetRequiredService<IDekHolder>().Dek` when no provider is supplied — preserves the explicit override for tests but makes the production wiring straightforward

### C. Password strength service

- [ ] 5.7 `Coffer.Core/Security/IPasswordStrengthChecker.cs` — interface with `PasswordStrength Evaluate(string password)`
- [ ] 5.8 `Coffer.Core/Security/PasswordStrength.cs` — record `(int Score, string? Warning, IReadOnlyList<string> Suggestions)` (score 0-4 per zxcvbn convention)
- [ ] 5.9 `Coffer.Infrastructure/Security/ZxcvbnPasswordStrengthChecker.cs` — implementation using `zxcvbn-core`
- [ ] 5.10 Register as Singleton in `AddCofferInfrastructure`

### D. Setup orchestration

- [ ] 5.11 `Coffer.Core/Security/ISetupService.cs` — interface with `Task CompleteSetupAsync(string masterPassword, string mnemonic, CancellationToken ct)`
- [ ] 5.12 `Coffer.Infrastructure/Security/SetupService.cs` — orchestrates:
  1. `RandomNumberGenerator.GetBytes(SaltBytes)` → salt
  2. `IMasterKeyDerivation.DeriveMasterKeyAsync(password, salt, Argon2Parameters.Default, ct)` → master key
  3. `RandomNumberGenerator.GetBytes(32)` → DEK
  4. `AesGcmCrypto.Encrypt(dek, masterKey)` → ciphertext + iv + tag
  5. Build `DekFile(version=1, Argon2Parameters.Default, salt, iv, tag, ciphertext)` and `DekFile.WriteAsync(file, CofferPaths.EncryptedDekFilePath(), ct)`
  6. `IKeyVault.SetCachedMasterKeyAsync(masterKey, TimeSpan.FromDays(7), ct)`
  7. `IDekHolder.Dek = dek`
  8. Create `CofferDbContext` via `IDbContextFactory<CofferDbContext>.CreateDbContextAsync(ct)` and run `MigrationRunner.RunPendingMigrationsAsync(ct)` (backup callback `null` — fresh install)
  - `Array.Clear` master key after step 7 (DEK lives in `IDekHolder` for the process lifetime)
- [ ] 5.13 Register `ISetupService` as Transient in `AddCofferInfrastructure`

### E. ViewModels (`Coffer.Application/ViewModels/Setup/`)

- [ ] 5.14 `SetupWizardViewModel` — root coordinator. `ObservableObject` with `CurrentStep` enum and `Mnemonic` string (lifted to the wizard so the verification step can compare). `RelayCommand` `Next` / `Back`. Final `Complete` command awaits `ISetupService.CompleteSetupAsync` then raises `SetupCompleted` event
- [ ] 5.15 `WelcomeStepViewModel` — info-only; `RelayCommand` `Continue`
- [ ] 5.16 `MasterPasswordStepViewModel` — `[ObservableProperty]` `Password`, `Confirmation`, derived `Strength` (calls `IPasswordStrengthChecker`), `IsValid` (`Strength.Score >= 3 && Password == Confirmation`)
- [ ] 5.17 `BipSeedDisplayStepViewModel` — receives the mnemonic from the wizard; exposes `IReadOnlyList<string> Words` (12 entries) for the view to render in a grid
- [ ] 5.18 `BipSeedVerificationStepViewModel` — `[ObservableProperty]` `Word3`, `Word7`; `IsValid` compares case-insensitively to the actual words; reveals positions visibly so the user is not guessing the position
- [ ] 5.19 `ConfirmStepViewModel` — summary + `RelayCommand` `CreateVault` which invokes the wizard's `Complete` command

### F. Views (`Coffer.Desktop/Views/Setup/`)

- [ ] 5.20 `SetupWizardWindow.axaml` — host window with `ContentControl` driven by `SetupWizardViewModel.CurrentStepViewModel`. `DataTemplate`-based step rendering. Polish window title `"Coffer — konfiguracja sejfu"`
- [ ] 5.21 `WelcomeStepView.axaml` — explanation + "Dalej" button
- [ ] 5.22 `MasterPasswordStepView.axaml` — password + confirmation `PasswordBox`, strength `ProgressBar` (4 segments), warning text, "Dalej" enabled when `IsValid`
- [ ] 5.23 `BipSeedDisplayStepView.axaml` — 12-word grid (4×3), explanation, "Zapisałem słowa" button. Code-behind applies `IScreenCaptureBlocker.Apply(this)` on `Loaded`
- [ ] 5.24 `BipSeedVerificationStepView.axaml` — 2 labelled text inputs ("Słowo #3", "Słowo #7"), "Sprawdź" button enabled when `IsValid`
- [ ] 5.25 `ConfirmStepView.axaml` — recap + "Utwórz sejf" button; spinner during `CompleteSetupAsync`

### G. Win32 screen-capture blocker

- [ ] 5.26 `Coffer.Core/Security/IScreenCaptureBlocker.cs` — `void Apply(object window)` (window is `object` to keep Core free of Avalonia types; the Desktop implementation casts to `Avalonia.Controls.Window`)
- [ ] 5.27 `Coffer.Desktop/Platform/WindowsScreenCaptureBlocker.cs` — `[SupportedOSPlatform("windows")]`. Resolves the native window handle via Avalonia's `TopLevel.PlatformImpl.Handle.Handle` and calls `SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE = 0x11)` via P/Invoke
- [ ] 5.28 `Coffer.Desktop/Platform/NoOpScreenCaptureBlocker.cs` — for non-Windows; logs a warning and returns
- [ ] 5.29 Register the platform-correct implementation in the Desktop DI bootstrap (`OperatingSystem.IsWindows()` switch, same pattern as `IKeyVault`)

### H. App routing

- [ ] 5.30 Update `Coffer.Desktop/Program.cs` / `App.axaml.cs`:
  - Build service provider
  - On framework initialised: check `File.Exists(CofferPaths.EncryptedDekFilePath())`
  - If missing → resolve `SetupWizardWindow` from DI and set as `MainWindow`. On `SetupCompleted`, close the wizard window and open `MainWindow` (the existing Sprint 1 placeholder)
  - If exists → for Sprint 5: show a placeholder window with text "Sejf istnieje — logowanie pojawi się w następnym sprincie" and exit on close. Sprint 6 replaces this.

### I. Tests

- [ ] 5.31 `DekHolderTests` (Coffer.Infrastructure.Tests): get-before-set throws, set+get roundtrips, set clears previous bytes
- [ ] 5.32 `ZxcvbnPasswordStrengthCheckerTests`: weak password scores low, strong password scores ≥3, empty password returns score 0
- [ ] 5.33 `SetupServiceTests` (integration): `CompleteSetupAsync` writes a valid `DekFile` to a temp path, the DEK round-trips through AES-GCM with the master key, `IKeyVault.GetCachedMasterKeyAsync` returns the master key, `IDekHolder.Dek` equals the original DEK, the DB has the `_SchemaInfo` entry for `InitialCreate`. Temp folder cleanup via `IDisposable`.
- [ ] 5.34 `MasterPasswordStepViewModelTests` (Coffer.Application.Tests): empty → invalid, weak → invalid, mismatched confirmation → invalid, strong + matching → valid
- [ ] 5.35 `BipSeedVerificationStepViewModelTests`: correct words → valid (case-insensitive), wrong words → invalid

Approximately 5 new test files, 15-20 new tests. Combined with the existing 49 = ~65-70 total.

### J. Manual verification

- [ ] 5.36 Delete `%LocalAppData%/Coffer/dek.encrypted` and `coffer.db` if present
- [ ] 5.37 `dotnet run --project src/Coffer.Desktop` shows the welcome step
- [ ] 5.38 Walk through the 5 steps; complete the wizard
- [ ] 5.39 After completion, the placeholder `MainWindow` appears, `dek.encrypted` and `coffer.db` exist in `%LocalAppData%/Coffer/`, and a fresh log line `[INF] Setup completed successfully` is in `%LocalAppData%/Coffer/logs/coffer-<date>.log`

### K. Validation and merge

- [ ] 5.40 `dotnet build` + `dotnet test` + `dotnet format --verify-no-changes` green locally
- [ ] 5.41 `gh issue create` for implementation — title `feat(sprint-5): Avalonia setup wizard (first interactive UI)`, labels `feat` + `sprint-5`
- [ ] 5.42 Commit on `feature/sprint-5-setup-wizard`, push, `gh pr create` with `Closes #<impl-issue>`
- [ ] 5.43 CI green (Avalonia builds on Ubuntu; UI rendering is not tested in CI, only build + ViewModel/service tests), squash-merge, branch deleted
- [ ] 5.44 `gh issue create` for closure → separate `chore/close-sprint-5` PR analogous to Sprints 1-4

## Definition of Done

1. `IDekHolder` (Core) + `DekHolder` (Infrastructure) bridge between setup wizard and `AddCofferDatabase`
2. `IPasswordStrengthChecker` (Core) + `ZxcvbnPasswordStrengthChecker` (Infrastructure) wrap zxcvbn-core
3. `ISetupService` (Core) + `SetupService` (Infrastructure) orchestrate the end-to-end first-run flow
4. 5 ViewModels in `Coffer.Application/ViewModels/Setup/` using CommunityToolkit.Mvvm
5. 5 Views in `Coffer.Desktop/Views/Setup/` (one `SetupWizardWindow` + 4 step UserControls)
6. `IScreenCaptureBlocker` (Core) + `WindowsScreenCaptureBlocker` / `NoOpScreenCaptureBlocker` (Desktop) applied to the BIP39 display
7. `Program.cs` routes between setup wizard and the "login coming" placeholder based on `dek.encrypted` existence
8. **15-20 new tests pass**, total ~65-70; locally + on CI Ubuntu
9. Manual verification: a complete cold-start setup creates `dek.encrypted` + `coffer.db` and lands on the placeholder `MainWindow`
10. `Coffer.Core` stays free of Avalonia, CommunityToolkit.Mvvm, zxcvbn-core, Win32 references — only interfaces and value objects

## Files affected

**New:**
- `src/Coffer.Core/Security/IDekHolder.cs`
- `src/Coffer.Core/Security/IPasswordStrengthChecker.cs`
- `src/Coffer.Core/Security/PasswordStrength.cs`
- `src/Coffer.Core/Security/ISetupService.cs`
- `src/Coffer.Core/Security/IScreenCaptureBlocker.cs`
- `src/Coffer.Infrastructure/Security/DekHolder.cs`
- `src/Coffer.Infrastructure/Security/ZxcvbnPasswordStrengthChecker.cs`
- `src/Coffer.Infrastructure/Security/SetupService.cs`
- `src/Coffer.Application/ViewModels/Setup/SetupWizardViewModel.cs`
- `src/Coffer.Application/ViewModels/Setup/WelcomeStepViewModel.cs`
- `src/Coffer.Application/ViewModels/Setup/MasterPasswordStepViewModel.cs`
- `src/Coffer.Application/ViewModels/Setup/BipSeedDisplayStepViewModel.cs`
- `src/Coffer.Application/ViewModels/Setup/BipSeedVerificationStepViewModel.cs`
- `src/Coffer.Application/ViewModels/Setup/ConfirmStepViewModel.cs`
- `src/Coffer.Desktop/Views/Setup/SetupWizardWindow.axaml(.cs)`
- `src/Coffer.Desktop/Views/Setup/WelcomeStepView.axaml(.cs)`
- `src/Coffer.Desktop/Views/Setup/MasterPasswordStepView.axaml(.cs)`
- `src/Coffer.Desktop/Views/Setup/BipSeedDisplayStepView.axaml(.cs)`
- `src/Coffer.Desktop/Views/Setup/BipSeedVerificationStepView.axaml(.cs)`
- `src/Coffer.Desktop/Views/Setup/ConfirmStepView.axaml(.cs)`
- `src/Coffer.Desktop/Platform/WindowsScreenCaptureBlocker.cs`
- `src/Coffer.Desktop/Platform/NoOpScreenCaptureBlocker.cs`
- `tests/Coffer.Infrastructure.Tests/Security/DekHolderTests.cs`
- `tests/Coffer.Infrastructure.Tests/Security/ZxcvbnPasswordStrengthCheckerTests.cs`
- `tests/Coffer.Infrastructure.Tests/Security/SetupServiceTests.cs`
- `tests/Coffer.Application.Tests/ViewModels/Setup/MasterPasswordStepViewModelTests.cs`
- `tests/Coffer.Application.Tests/ViewModels/Setup/BipSeedVerificationStepViewModelTests.cs`

**Modified:**
- `src/Coffer.Application/Coffer.Application.csproj` — `CommunityToolkit.Mvvm`
- `src/Coffer.Infrastructure/Coffer.Infrastructure.csproj` — `zxcvbn-core`
- `src/Coffer.Infrastructure/DependencyInjection/ServiceRegistration.cs` — register `IDekHolder`, `IPasswordStrengthChecker`, `ISetupService`; `AddCofferDatabase` default `dekProvider`
- `src/Coffer.Desktop/Program.cs` and/or `App.axaml.cs` — first-run routing
- `src/Coffer.Desktop/DependencyInjection/DesktopServiceRegistration.cs` — register `IScreenCaptureBlocker`, wizard window, step views/VMs
- `.ai/sprints/sprint-5/sprint-5.md` — checkboxes, status
- `.ai/sprints/sprint-5/log.md` — progress
- `.ai/sprints/index.md` — status

## Open questions

1. **`ISetupService` placement — `Coffer.Core/Security` or `Coffer.Application`?**
   - The architecture doc puts use cases in Application. But `ISetupService` is closer to a domain operation than a use case — there is no business logic, just an orchestration of infrastructure services.
   - **Recommendation:** **`Coffer.Core/Security`** — same neighbourhood as `IKeyVault` / `IMasterKeyDerivation` / `ISeedManager`. Cleaner DI graph.

2. **`IDekHolder.Dek` getter throwing vs returning `byte[]?`**
   - Throwing makes the contract explicit ("must be set before any DB access").
   - Nullable forces every caller to check, polluting code.
   - **Recommendation:** throw on get-before-set. Add `IsAvailable` for the rare caller that wants to probe without throwing.

3. **`AddCofferDatabase` default `dekProvider`?**
   - Sprint 4's signature requires explicit `dekProvider`. Sprint 5 typical wiring is `sp => sp.GetRequiredService<IDekHolder>().Dek` — every caller would repeat it.
   - **Recommendation:** make `dekProvider` optional with the `IDekHolder`-resolving default. Explicit override remains for tests and unusual wiring.

4. **CommunityToolkit.Mvvm in `Coffer.Application`?**
   - Per architecture doc, MVVM lives in Application via CommunityToolkit.Mvvm. Confirmed.
   - **Recommendation:** yes; the source generators are the whole point.

5. **zxcvbn package choice?**
   - Options: `zxcvbn-core`, `Zxcvbn.Net`, etc.
   - **Recommendation:** `zxcvbn-core` — actively maintained, .NET-standard, returns `Result` with Score (0-4), Warning, and Suggestions matching the upstream library.

6. **`SetWindowDisplayAffinity` — applied to the wizard window or only the seed-display step?**
   - The mnemonic is visible only on one step. Applying to the whole window protects all steps including welcome/password.
   - **Recommendation:** apply only on the seed-display step view's `Loaded` event. Other steps do not show secrets. Keeps the override narrow.

7. **Password input — `PasswordBox` or custom?**
   - Avalonia has `TextBox` with `PasswordChar` but the right control is the explicit `PasswordBox` (Avalonia 11.0+ ships it).
   - **Recommendation:** Avalonia `TextBox` with `PasswordChar="●"`. `PasswordBox` may or may not exist depending on Avalonia version — fall back to `TextBox` if absent.

8. **What happens if user closes the wizard mid-flow?**
   - No state on disk yet, so closing is recoverable (next launch starts fresh).
   - **Recommendation:** closing the wizard window exits the app. No "save progress" mechanic.

9. **Master password handling — `string` or `char[]`?**
   - Per `docs/architecture/09-security-key-management.md` §"Memory hygiene", the architecture acknowledges that `string` passwords briefly exist in managed memory and accepts the limitation. ViewModels naturally bind to `string`.
   - **Recommendation:** `string`. Clear ViewModel `Password` property after `CompleteSetupAsync` returns.

10. **DEK lifetime in `IDekHolder` — process lifetime or scoped?**
    - The DEK is needed for every DB operation until the process exits.
    - **Recommendation:** process lifetime (Singleton). On logout/auto-lock (Sprint 6+), clear the holder via `IDekHolder.Clear()`.

11. **Migration runner backup callback in Sprint 5 — `null` (fresh install) or a stub?**
    - Sprint 4 plan explicitly said fresh install passes `null`. The architecture doc requires backup before every migration but acknowledges that on first install there is nothing to back up.
    - **Recommendation:** `null` for the first migration. Sprint 8+ backup service will hook in when there is existing data to protect.

12. **Visual styling — pull from `docs/mockups/shared/design-tokens.css`?**
    - The mockups are for the dashboard / transactions, not the setup wizard.
    - **Recommendation:** simple functional UI for Sprint 5 — default Avalonia FluentTheme is fine. Polished theming is post-Phase-0 work.

13. **Routing fallback when `dek.encrypted` exists?**
    - Sprint 6 owns the login flow. Sprint 5 cannot land login.
    - **Recommendation:** placeholder window with Polish text "Sejf już istnieje. Logowanie pojawi się w Sprint 6 — usuń `dek.encrypted` jeśli chcesz przetestować ponowny setup." Exit on close.

14. **`SetupCompleted` — event vs callback?**
    - Wizard ViewModel needs to signal "done" to whoever swaps the window.
    - **Recommendation:** plain `event EventHandler? SetupCompleted` on the ViewModel. Avalonia consumer (App) subscribes.

15. **What does the existing Sprint-1 `MainWindow` show after setup completes?**
    - Today it is "Coffer" placeholder text.
    - **Recommendation:** keep as-is for Sprint 5. Sprint 6 adds the "logged in as …" content. This sprint's job is just to land on `MainWindow`; the placeholder is fine.

## Notes

- Hard rule #3 preserved: `Coffer.Core` stays free of Avalonia, CommunityToolkit.Mvvm, zxcvbn-core, Win32 — only interfaces and value objects.
- Hard rule #6 (master password / BIP39 never logged, never to AI, never to disk in plaintext): `SetupService` logs only sanitized info ("Setup completed", not the password or mnemonic). `PromptAnonymizer` is not yet in play (AI is later phase).
- Hard rule #8 (every migration runs pre-migration backup): `MigrationRunner` provides the callback hook. Sprint 5 fresh install passes `null` (nothing to back up). The mechanism is exercised by the `Run_WhenBackupCallbackThrows_DoesNotApplyAnyMigration` test from the Sprint-4 review.
- Sprint 5 is the largest sprint so far (≈44 steps). The complexity is composition, not new primitives — every building block already ships green tests.
- After Sprint 5 the app **boots into a real interactive flow** rather than a placeholder; Sprint 6 then adds the login + auto-lock to close Phase 0.
- UI text is Polish per `docs/conventions.md`. Code, identifiers, comments, tests, and these planning files remain English per the language policy.

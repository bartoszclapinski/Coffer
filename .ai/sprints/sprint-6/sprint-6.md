# Sprint 6 — Login window, auto-lock, Phase 0 closure

**Phase:** 0 (Foundation — closing sprint)
**Status:** Closed (2026-05-20)
**Depends on:** sprint-2 (`IKeyVault` / DPAPI cache), sprint-3 (`IMasterKeyDerivation`, `AesGcmCrypto`, `DekFile`), sprint-4 (`CofferDbContext`, `MigrationRunner`), sprint-5 (`IDekHolder`, `SetupWizardWindow`, app routing for partial state)

## Goal

The reverse of Sprint 5. A vault already exists on disk; cold start either bypasses the password through the 7-day DPAPI cache or asks the user to type the master password. On success the app lands on a real `MainWindow` ("Zalogowano" + version + "Wyloguj" button). After 15 minutes of UI inactivity (or a manual logout click), the DEK leaves memory, the DPAPI cache is invalidated, and the user is dropped back to the login screen. This is the last sprint of Phase 0 — at the end of it, the roadmap's Phase 0 Definition of Done is verifiable end-to-end on Windows.

## Strategy

Sprint 6 is, like Sprint 5, an integration sprint with no new cryptographic primitives. Every building block — Argon2id key derivation, `AesGcmCrypto.Decrypt`, `DekFile.ReadAsync`, `IKeyVault.GetCachedMasterKeyAsync`, `IDekHolder.Set`/`Clear` — already exists and is tested. This sprint composes them in the reverse direction of Sprint 5's `SetupService` and adds two small primitives (`ILastActivityTracker`, `IAutoLockMonitor`) to drive the auto-lock.

- **`LoginService` is the orchestrator** — analogous to `SetupService`. Two public entry points:
  - `TryLoginFromCachedKeyAsync` — silent cold-start path; returns `true` only if DPAPI cache hit AND DEK decryption succeeded
  - `LoginWithPasswordAsync` — interactive path; derives master key with Argon2id from the password + salt stored in `dek.encrypted`, decrypts DEK, publishes via `IDekHolder.Set`, then refreshes DPAPI cache with a fresh 7-day TTL
- **`ILastActivityTracker` is intentionally thin** — `RegisterActivity()` + `LastActivityUtc` getter. Lives in Infrastructure (no platform dependencies, but matches `DekHolder` placement). Thread-safe; `MainWindow` code-behind subscribes top-level pointer / key events and calls `RegisterActivity()` from the UI thread.
- **`AutoLockMonitor` is a Timer-driven event source** — `Start(TimeSpan)` schedules a `System.Threading.Timer` that ticks every minute, compares `LastActivityUtc` against the configured timeout, raises `AutoLockTriggered` when exceeded. `Stop()` cleans up. Implements `IDisposable`. The Timer callback runs on a thread pool thread; subscribers must marshal to the UI thread themselves (App-level handler uses `Dispatcher.UIThread.Post`).
- **`LoginViewModel`** has a single `[ObservableProperty] Password` and a `[RelayCommand]` `LoginAsync`. On `InvalidMasterPasswordException`, the VM shows a red Polish error and clears `Password`. On any other failure, generic Polish error + log at Error.
- **`MainViewModel`** is new — Sprint 1's `MainWindow` had no VM, just a placeholder `TextBlock`. Now: `AppVersion` (read from entry assembly informational version) + `LogoutCommand`. The command raises a `LoggedOut` event that `App.axaml.cs` consumes to swap windows.
- **App.axaml.cs is the state machine.** Sprint 5 added 3 routes (setup wizard, partial-state error, "vault exists" placeholder). Sprint 6 replaces the third route with the real login flow and adds two more transitions: login → main (success), main → login (logout, auto-lock, or manual). The `AutoLockMonitor` lifetime is tied to "logged in" — `Start` after login success, `Stop` on logout / auto-lock.
- **No password lockout, no attempt counter.** Argon2id at the configured parameters (~1-2s) is the natural rate limiter; lockout adds complexity without value for a local single-user vault. Failed attempts log at Warning.
- **No forgot-password / BIP39 recovery flow.** Out of scope per chat sign-off; tracked for Sprint 7.

Three PRs in the established issue-per-PR workflow:
1. **Plan** (`chore/plan-sprint-6`, this document) — issue created first
2. **Implementation** (`feature/sprint-6-login-autolock`, new issue) — code + ViewModels + Views + tests
3. **Closure** (`chore/close-sprint-6`, new issue) — post-merge bookkeeping + roadmap Phase 0 checkboxes

## Steps

### A. Login orchestration (Core + Infrastructure)

- [x] 6.1 `Coffer.Core/Security/ILoginService.cs` — three methods:
  - `Task<bool> TryLoginFromCachedKeyAsync(CancellationToken ct)` — returns `true` only on full success (cache hit + DEK decrypted + holder published); any failure path returns `false` without throwing (cold-start best-effort)
  - `Task LoginWithPasswordAsync(string masterPassword, CancellationToken ct)` — throws `InvalidMasterPasswordException` on wrong password, `VaultCorruptedException` on format / tag failure unrelated to the password, `VaultMissingException` if pre-flight files are gone
  - `Task LogoutAsync(CancellationToken ct)` — clear `IDekHolder` + invalidate DPAPI cache
- [x] 6.2 `Coffer.Core/Security/InvalidMasterPasswordException.cs` — sealed, parameterless message ("The master password did not unlock the vault.")
- [x] 6.3 `Coffer.Core/Security/VaultCorruptedException.cs` — sealed, includes `Reason` (enum or string) so the UI can render a more specific Polish message when the failure is "dek.encrypted parse error" vs "AES-GCM tag mismatch with cached key"
- [x] 6.4 `Coffer.Core/Security/VaultMissingException.cs` — sealed; rare path (defensive — the App routing should catch this earlier)
- [x] 6.5 `Coffer.Infrastructure/Security/LoginService.cs` — orchestrator. Dependencies: `IMasterKeyDerivation`, `IKeyVault`, `IDekHolder`, `Func<IDbContextFactory<CofferDbContext>>` (same lazy-factory trick as `SetupService`), `ILogger<LoginService>`. Flow for `LoginWithPasswordAsync`:
  1. Pre-flight: `File.Exists(CofferPaths.EncryptedDekFilePath())` — throw `VaultMissingException` if not
  2. `await DekFile.ReadAsync(dekPath, ct)` — throws on format error; wrap in `try`/`catch` and rethrow as `VaultCorruptedException(reason: "dek-file-format")`
  3. `await _keyDerivation.DeriveMasterKeyAsync(masterPassword, file.Salt, file.ArgonParameters, ct)` — note: parameters are read from the file, not the hardcoded `Argon2Parameters.Default`, so old vaults remain decryptable if defaults change
  4. `AesGcmCrypto.Decrypt(file.Ciphertext, file.Iv, file.Tag, masterKey)` — on `CryptographicException` (auth tag mismatch), throw `InvalidMasterPasswordException` (NOT `VaultCorruptedException` — the tag fails when the key is wrong, which is the same outcome as the wrong password)
  5. `_dekHolder.Set(dek)` — publishes for `CofferDbContext`
  6. `await _keyVault.SetCachedMasterKeyAsync(masterKey, TimeSpan.FromDays(7), ct)` — sliding TTL; cache write failure logs Warning but does NOT fail the login
  7. Zero `masterKey` and `dek` (and `file.Ciphertext` / `file.Tag` / `file.Iv` on the way out) in `finally`
- [x] 6.6 `LoginService.TryLoginFromCachedKeyAsync` — flow:
  1. `var cachedMasterKey = await _keyVault.GetCachedMasterKeyAsync(ct)` — returns null on cache miss / expired
  2. If null, return `false`
  3. Read `dek.encrypted` and `AesGcmCrypto.Decrypt` with the cached master key
  4. On any exception (format, AES-GCM tag, missing file), log at Warning ("Cached key does not unlock the vault — falling back to password"), `await _keyVault.InvalidateMasterKeyCacheAsync(ct)`, return `false`
  5. On success, `_dekHolder.Set(dek)`, return `true`
  6. Zero buffers
- [x] 6.7 `LoginService.LogoutAsync` — `_dekHolder.Clear()`, then `await _keyVault.InvalidateMasterKeyCacheAsync(ct)`. No exceptions propagate (use the same `SafeRollback` pattern from `SetupService`)
- [x] 6.8 Register `ILoginService` as Transient in `AddCofferSetup` (or rename to `AddCofferAuthentication` if that reads better — the sprint-5 registration is in a method named for setup, but it logically covers all auth-orchestration services; rename + keep the API surface)

### B. Activity tracking and auto-lock

- [x] 6.9 `Coffer.Core/Security/ILastActivityTracker.cs` — two members:
  - `void RegisterActivity()` — sets `LastActivityUtc = DateTime.UtcNow`
  - `DateTime LastActivityUtc { get; }` — last registered timestamp; defaults to `DateTime.UtcNow` at construction time so the very first idle check sees a sensible value
- [x] 6.10 `Coffer.Infrastructure/Security/LastActivityTracker.cs` — thread-safe via `Volatile.Write`/`Read` on the underlying `DateTime` ticks, OR a simple `lock`. Pick whichever stays readable. Registered as Singleton in `AddCofferInfrastructure`.
- [x] 6.11 `Coffer.Core/Security/IAutoLockMonitor.cs`:
  - `void Start(TimeSpan idleTimeout)` — schedules the periodic check
  - `void Stop()` — disposes the timer
  - `event EventHandler? AutoLockTriggered` — raised once per `Start` call when `DateTime.UtcNow - LastActivityUtc >= idleTimeout`; the monitor calls `Stop()` internally before raising to prevent double-fire
  - Inherits `IDisposable` so `using` works in tests
- [x] 6.12 `Coffer.Infrastructure/Security/AutoLockMonitor.cs` — `System.Threading.Timer` with a 1-minute period (configurable in DI registration as a constant; not a public option for Sprint 6). Compares the elapsed time on every tick; thread-safe `Start`/`Stop` via `lock`. The Timer callback marshals through `lock` to avoid the rare race where `Stop` runs concurrently with a tick. Registered as Singleton.
- [x] 6.13 `Coffer.Core/Security/AutoLockOptions.cs` — `record AutoLockOptions(TimeSpan IdleTimeout)`. Default is `TimeSpan.FromMinutes(15)`, registered as a Singleton via `AddSingleton` in `AddCofferInfrastructure`. Sprint-7+ Settings UI replaces this with a configurable source; for Sprint 6 it's a constant.

### C. Login ViewModel + View (`Coffer.Application/ViewModels/Login/` + `Coffer.Desktop/Views/Login/`)

- [x] 6.14 `Coffer.Application/ViewModels/Login/LoginViewModel.cs` — `[ObservableProperty]` `Password`, `[ObservableProperty]` `ErrorMessage`, `[ObservableProperty]` `IsBusy`. `[RelayCommand]` `LoginAsync`:
  1. Set `IsBusy = true`, clear `ErrorMessage`
  2. `await _loginService.LoginWithPasswordAsync(Password, CancellationToken.None)`
  3. On `InvalidMasterPasswordException`: `ErrorMessage = "Nieprawidłowe hasło."`, `Password = ""`
  4. On `VaultCorruptedException`: `ErrorMessage = "Plik sejfu jest uszkodzony. Skontaktuj się z dokumentacją odzyskiwania."` (Sprint 7 will replace with the recovery flow)
  5. On `VaultMissingException`: `ErrorMessage = "Brak pliku sejfu. Uruchom ponownie aplikację."` (defensive — App routing should prevent this)
  6. On any other exception: log full at Error, `ErrorMessage = "Nie udało się zalogować. Spróbuj ponownie."`
  7. On success: raise `LoginCompleted` event (analogous to Sprint 5's `SetupCompleted`)
  8. `finally`: `IsBusy = false`
- [x] 6.15 `LoginCompletedEventArgs` — empty marker for now; future sprints (or the same one if needed) can attach context. Sprint 5's `SetupCompletedEventArgs(bool Success, Exception?)` is heavier because setup has more failure modes; login's failure is handled inside the VM and never reaches App-level handler.
- [x] 6.16 `LoginViewModel.ClearSensitive()` — `Password = ""`. Called by `LoginWindow.OnClosing` (mirrors Sprint 5 pattern).
- [x] 6.17 `Coffer.Desktop/Views/Login/LoginWindow.axaml` — single screen, FluentTheme defaults:
  - Polish title "Coffer — logowanie"
  - Password input (`TextBox` with `PasswordChar="●"` — same fallback as Sprint 5)
  - "Zaloguj" button bound to `LoginCommand`, disabled when `IsBusy` or `string.IsNullOrEmpty(Password)`
  - `ProgressBar` / spinner bound to `IsBusy`
  - Error `TextBlock` bound to `ErrorMessage` (red, hidden when empty)
  - Window `Closing` blocked while `IsBusy = true` (same code-behind pattern as `SetupWizardWindow`)
- [x] 6.18 `LoginWindow.axaml.cs` — code-behind subscribes `Closing` to block close while busy, calls `ClearSensitive()` on close, and forwards Enter key on the password box to the `LoginCommand`

### D. MainWindow upgrade (`Coffer.Application/ViewModels/Main/` + existing `Coffer.Desktop/Views/MainWindow.axaml`)

- [x] 6.19 `Coffer.Application/ViewModels/Main/MainViewModel.cs`:
  - `string AppVersion` — initialised from `Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown"`
  - `[RelayCommand]` `LogoutAsync` — calls `_loginService.LogoutAsync`, then raises `LoggedOut` event
  - `event EventHandler? LoggedOut`
- [x] 6.20 `src/Coffer.Desktop/Views/MainWindow.axaml` — replace the Sprint-1 `TextBlock` placeholder with:
  - Polish heading "Zalogowano"
  - "Wersja: {AppVersion}" line bound to VM
  - "Wyloguj" button bound to `LogoutCommand`
  - Plain Avalonia FluentTheme — no design tokens yet (Phase 1+ owns the styling)
- [x] 6.21 `src/Coffer.Desktop/Views/MainWindow.axaml.cs` — code-behind subscribes the top-level pointer and key events on `AttachedToVisualTree`, calling `_activityTracker.RegisterActivity()` on each. The tracker is resolved through `App.Services` (same pragma as `BipSeedDisplayStepView` — flagged as follow-up issue [#31](https://github.com/bartoszclapinski/Coffer/issues/31); fixing that for both views in one go is a candidate chore but out of Sprint 6 scope)
- [x] 6.22 Register `MainViewModel` and `LoginViewModel` as Transient in `AddCofferApplication` (or wherever the existing setup VMs are registered — keep the convention)

### E. App routing

- [x] 6.23 Update `Coffer.Desktop/App.axaml.cs`. `ResolveStartupWindow` after Sprint 5 has four routes (setup wizard, sprint-6 placeholder, partial-state error, would-be vault-exists). Sprint 6 replaces the "vault exists, both files present" branch with:
  ```csharp
  if (dekExists && dbExists)
  {
      // Try DPAPI cache first; this is the silent path that closes
      // Phase 0's "restart within 7 days bypasses password" DoD.
      var loginService = Services.GetRequiredService<ILoginService>();
      var cachedLoginSucceeded = loginService
          .TryLoginFromCachedKeyAsync(CancellationToken.None)
          .GetAwaiter().GetResult();
      if (cachedLoginSucceeded)
      {
          return BuildMainWindow(desktop);
      }
      return BuildLoginWindow(desktop);
  }
  ```
  `BuildLoginWindow` resolves `LoginWindow` + `LoginViewModel`, subscribes `LoginCompleted` to swap to `MainWindow`.
  `BuildMainWindow` resolves `MainWindow` + `MainViewModel`, subscribes `LoggedOut` to swap back to `LoginWindow`, and `Start`s the `AutoLockMonitor` with the configured `IdleTimeout`. The `AutoLockTriggered` event handler marshals via `Dispatcher.UIThread.Post`, calls `ILoginService.LogoutAsync`, and swaps to `LoginWindow`.
- [x] 6.24 Block: the synchronous `.GetAwaiter().GetResult()` on `TryLoginFromCachedKeyAsync` in `ResolveStartupWindow` is the same trade-off Sprint 5 made (Argon2 inside `SetupService.CompleteSetupAsync` is run via `await` from a `RelayCommand`, but Sprint 6 needs to decide *which window to show* before any UI exists). Cache-lookup is fast (~10-30 ms for DPAPI unprotect + AES-GCM decrypt of 60-ish bytes), so blocking the UI bootstrap is acceptable. If it becomes a perceptible delay, splash-screen pattern in Sprint 7+.
- [x] 6.25 Logout path consolidation — both manual logout (button) and auto-lock route through one method: `App.HandleLogoutAsync()` which calls `ILoginService.LogoutAsync`, stops the `AutoLockMonitor`, disposes the current `MainWindow`, builds and shows a fresh `LoginWindow`. Single source of truth prevents drift between the two paths.

### F. Tests

- [x] 6.26 `Coffer.Infrastructure.Tests/Security/LoginServiceTests.cs`:
  - `LoginWithPassword_WithCorrectPassword_PublishesDek` — uses real `Argon2KeyDerivation` + `AesGcmCrypto` with a pre-built `dek.encrypted` in a temp dir; asserts `IDekHolder.IsAvailable == true` after
  - `LoginWithPassword_WithWrongPassword_ThrowsInvalidMasterPasswordException` — asserts the specific exception type
  - `LoginWithPassword_WhenDekFileMissing_ThrowsVaultMissingException`
  - `LoginWithPassword_WhenDekFileCorrupted_ThrowsVaultCorruptedException` — write a deliberately broken file
  - `LoginWithPassword_OnSuccess_RefreshesCacheWithSevenDayTtl` — assert `IKeyVault.SetCachedMasterKeyAsync` was called with `TimeSpan.FromDays(7)` (use a fake `IKeyVault`)
  - `LoginWithPassword_WhenCacheWriteFails_StillSucceeds` — cache fake throws; login still publishes DEK and returns normally; warning logged
  - `TryLoginFromCachedKey_WithValidCache_ReturnsTrueAndPublishesDek`
  - `TryLoginFromCachedKey_WithMissingCache_ReturnsFalse`
  - `TryLoginFromCachedKey_WithCacheKeyThatDoesNotDecryptDek_ReturnsFalseAndInvalidatesCache`
  - `Logout_ClearsHolderAndInvalidatesCache`
  - `Logout_WhenCacheInvalidationFails_StillClearsHolder` — defensive
- [x] 6.27 `Coffer.Infrastructure.Tests/Security/LastActivityTrackerTests.cs`:
  - `LastActivityUtc_AfterRegisterActivity_AdvancesForward`
  - `LastActivityUtc_AfterConstruction_IsRecent` — within 1 second of `DateTime.UtcNow`
  - `RegisterActivity_FromMultipleThreads_DoesNotCorrupt` — 100 parallel calls, final value within range
- [x] 6.28 `Coffer.Infrastructure.Tests/Security/AutoLockMonitorTests.cs`:
  - `Start_WhenIdleExceedsTimeout_RaisesEventOnce` — use a very short timeout (e.g. 50 ms) and a stubbed `ILastActivityTracker` that returns a fixed-past timestamp; assert event raised exactly once
  - `Start_WhenIdleBelowTimeout_DoesNotRaiseEvent`
  - `Start_AfterAutoLockTriggered_StopsItself` — explicitly verify Stop is internally called on raise
  - `Dispose_DuringActiveTimer_DoesNotThrow`
- [x] 6.29 `Coffer.Application.Tests/ViewModels/Login/LoginViewModelTests.cs`:
  - `LoginCommand_WithCorrectPassword_RaisesLoginCompleted` — uses a fake `ILoginService`
  - `LoginCommand_WithWrongPassword_SetsErrorMessageAndClearsPassword`
  - `LoginCommand_WhenServiceThrowsGenericException_SetsGenericErrorMessage`
  - `IsBusy_DuringLogin_IsTrue` — fake service awaits a `TaskCompletionSource` so the test can observe the busy state
- [x] 6.30 `Coffer.Application.Tests/ViewModels/Main/MainViewModelTests.cs`:
  - `LogoutCommand_CallsLoginServiceLogout`
  - `LogoutCommand_OnSuccess_RaisesLoggedOut`
  - `AppVersion_Returns_NonEmptyString`

### G. Manual verification

- [x] 6.31 Cold start with `dek.encrypted` + `coffer.db` present + valid DPAPI cache → `MainWindow` appears with "Zalogowano" + version; **no password prompt** shown
- [x] 6.32 Delete `%LocalAppData%\Coffer\master-key.dpapi.cache`, restart app → `LoginWindow` appears, correct password lands on `MainWindow`, cache file recreated
- [x] 6.33 Wrong password → red Polish error visible, password field cleared, retry with correct password works
- [x] 6.34 Click "Wyloguj" → app drops back to `LoginWindow`; `master-key.dpapi.cache` deleted; correct password required again
- [x] 6.35 Idle for the configured timeout (override to 30 seconds via a temporary `AutoLockOptions` modification for the manual test — restore to 15 minutes before commit) → app drops back to `LoginWindow` without user action
- [x] 6.36 Corrupt `dek.encrypted` manually (open in hex editor, flip a byte in the ciphertext region), restart → login attempt fails with "Plik sejfu jest uszkodzony" message
- [x] 6.37 Walk through Phase 0 Definition of Done end-to-end:
  - Cold start without vault → setup wizard (Sprint 5)
  - Cold start with vault, no cache → login window
  - Correct password → MainWindow
  - Restart within 7 days → MainWindow without password
  - Clear cache → password again required
  - 15-min idle → re-login required

### H. Phase 0 closure and validation

- [x] 6.38 Update `docs/architecture/10-roadmap.md` Phase 0 checkboxes — every bullet from `git init` through the setup-wizard / DPAPI verification gets `[x]`
- [x] 6.39 `dotnet build` + `dotnet test` + `dotnet format --verify-no-changes` green locally
- [x] 6.40 `gh issue create` for implementation — title `feat(sprint-6): login window, auto-lock, MainWindow upgrade`, labels `feat` + `sprint-6`
- [x] 6.41 Commit on `feature/sprint-6-login-autolock`, push, `gh pr create` with `Closes #<impl-issue>`
- [x] 6.42 CI green (`build-and-test` + `format-check`), squash-merge, branch deleted
- [x] 6.43 `gh issue create` for closure → separate `chore/close-sprint-6` PR analogous to Sprints 1-5

## Definition of Done

1. `ILoginService` (Core) + `LoginService` (Infrastructure) orchestrate the cached-key and password paths; both throw the right exception types
2. `ILastActivityTracker` (Core) + `LastActivityTracker` (Infrastructure) provide a thread-safe activity timestamp
3. `IAutoLockMonitor` (Core) + `AutoLockMonitor` (Infrastructure) raise `AutoLockTriggered` once on idle threshold, marshaled to the UI thread by the App handler
4. `LoginViewModel` + `LoginWindow` in Avalonia handle correct/wrong password, busy state, error display, sensitive-state cleanup on close
5. `MainViewModel` + upgraded `MainWindow` show "Zalogowano" + version + "Wyloguj" button
6. App routing in `App.axaml.cs` covers all five transitions: setup (no vault), partial-state error, login (vault exists, cache miss), main (cache hit OR login success), back-to-login (logout / auto-lock)
7. **~18-22 new tests pass** (5 test files), total ~93-97 with Sprint 5's 75; locally + on CI Ubuntu
8. Manual verification: Phase 0 Definition of Done is met end-to-end (steps 6.31-6.37)
9. `docs/architecture/10-roadmap.md` Phase 0 section has every checkbox `[x]`
10. `Coffer.Core` stays free of Avalonia, CommunityToolkit.Mvvm, Win32 references — only interfaces and value objects

## Files affected

**New:**
- `src/Coffer.Core/Security/ILoginService.cs`
- `src/Coffer.Core/Security/InvalidMasterPasswordException.cs`
- `src/Coffer.Core/Security/VaultCorruptedException.cs`
- `src/Coffer.Core/Security/VaultMissingException.cs`
- `src/Coffer.Core/Security/ILastActivityTracker.cs`
- `src/Coffer.Core/Security/IAutoLockMonitor.cs`
- `src/Coffer.Core/Security/AutoLockOptions.cs`
- `src/Coffer.Infrastructure/Security/LoginService.cs`
- `src/Coffer.Infrastructure/Security/LastActivityTracker.cs`
- `src/Coffer.Infrastructure/Security/AutoLockMonitor.cs`
- `src/Coffer.Application/ViewModels/Login/LoginViewModel.cs`
- `src/Coffer.Application/ViewModels/Login/LoginCompletedEventArgs.cs`
- `src/Coffer.Application/ViewModels/Main/MainViewModel.cs`
- `src/Coffer.Desktop/Views/Login/LoginWindow.axaml`(`.cs`)
- `tests/Coffer.Infrastructure.Tests/Security/LoginServiceTests.cs`
- `tests/Coffer.Infrastructure.Tests/Security/LastActivityTrackerTests.cs`
- `tests/Coffer.Infrastructure.Tests/Security/AutoLockMonitorTests.cs`
- `tests/Coffer.Application.Tests/ViewModels/Login/LoginViewModelTests.cs`
- `tests/Coffer.Application.Tests/ViewModels/Main/MainViewModelTests.cs`

**Modified:**
- `src/Coffer.Desktop/Views/MainWindow.axaml`(`.cs`) — Sprint-1 placeholder upgraded to real content + activity tracking
- `src/Coffer.Desktop/App.axaml.cs` — login route + post-login MainWindow lifecycle + auto-lock wiring
- `src/Coffer.Infrastructure/DependencyInjection/...` — register `ILoginService`, `ILastActivityTracker`, `IAutoLockMonitor`, `AutoLockOptions`
- `src/Coffer.Application/DependencyInjection/...` — register `LoginViewModel`, `MainViewModel`
- `docs/architecture/10-roadmap.md` — Phase 0 checkboxes [x] (in closure PR)
- `.ai/sprints/sprint-6/sprint-6.md` — checkboxes, status
- `.ai/sprints/sprint-6/log.md` — progress
- `.ai/sprints/index.md` — status

## Open questions

1. **Where do `LastActivityTracker` / `AutoLockMonitor` live — `Coffer.Application` or `Coffer.Infrastructure`?**
   - Both are pure CLR logic with no platform dependencies. Could be in Application (closer to VM concerns) or Infrastructure (matches `DekHolder`, `LoginService`).
   - **Recommendation:** `Coffer.Infrastructure` — they are runtime services consumed by VMs and App, not VMs themselves. Same neighbourhood as `DekHolder`.

2. **`LoginService` constructor — `Func<IDbContextFactory<CofferDbContext>>` like `SetupService`, or direct factory?**
   - Sprint 5 needed the `Func` indirection because `IDekHolder` was empty at factory-resolution time. Sprint 6's `LoginService` publishes the DEK before any DB code touches the factory, but the order of operations within `LoginService` is `_dekHolder.Set(dek)` THEN potentially anything else. No DB operation happens during login itself.
   - **Recommendation:** `LoginService` does NOT need the factory at all in Sprint 6 — login publishes the DEK; first DB use is downstream (MainWindow does nothing with the DB yet; Phase 1 reads start using it). Drop the factory dependency entirely.

3. **`VaultCorruptedException.Reason` — enum or string?**
   - Enum gives the VM a switch-able set; string is open-ended for future failure modes.
   - **Recommendation:** enum (`DekFileFormat`, `MigrationDbState`, `Other`) — three known cases today, easy to extend.

4. **Where does the `AutoLockMonitor` event marshal to the UI thread — inside the monitor or in App?**
   - Doing it inside the monitor couples Infrastructure to `Avalonia.Threading.Dispatcher`, which violates hard rule #3 (Infrastructure should access platform APIs only behind interfaces).
   - **Recommendation:** monitor raises on the threadpool; App's subscriber marshals via `Dispatcher.UIThread.Post`. Keep Avalonia out of Infrastructure.

5. **`MainWindow` activity tracker — code-behind subscription, or VM event from App?**
   - Code-behind is the pragmatic Avalonia pattern (Sprint 5's `BipSeedDisplayStepView` already does it for the screen-capture blocker).
   - **Recommendation:** code-behind. Same flagged-follow-up [#31](https://github.com/bartoszclapinski/Coffer/issues/31) covers both views in one future refactor.

6. **DPAPI cache refresh policy — every successful password login, or only on cache-miss → password-login transition?**
   - Refreshing every time gives sliding-window UX (each successful login resets the 7-day clock). Skipping if the cache is already fresh saves one DPAPI call but breaks the "7 days from last login" mental model.
   - **Recommendation:** refresh every password login. Cache write is sub-millisecond; the UX clarity is worth it.

7. **`LoginWindow` "Forgot password?" link — disabled with tooltip, or hidden entirely?**
   - Disabled hint commits the UI to a position the Sprint-7 recovery flow may need to revise.
   - **Recommendation:** hidden in Sprint 6. Clean slate for Sprint 7.

8. **Manual logout vs auto-lock — same code path?**
   - Both invalidate DPAPI cache, clear `IDekHolder`, swap to `LoginWindow`. Difference: trigger only.
   - **Recommendation:** single `App.HandleLogoutAsync` method, called from both. Step 6.25 already documents this.

9. **`AutoLockMonitor` periodicity — every minute, every 30s, on-demand?**
   - Per `docs/architecture/09-security-key-management.md` §"Auto-lock": *"checked by a `Timer` every minute"*. Document already decided.
   - **Recommendation:** every 60 seconds. Tradeoff: max 1-minute slop on the 15-minute idle threshold (worst case the user locks at 15:59 not 15:00) — acceptable.

10. **Argon2 derivation happens on the UI thread or a background task?**
    - The `RelayCommand` is async; `LoginWithPasswordAsync` already runs on a thread pool thread by virtue of `await`. As long as the View doesn't synchronously call into it, no extra `Task.Run` is needed.
    - **Recommendation:** rely on `[RelayCommand] async Task LoginAsync` + the natural `await` continuation. No `ConfigureAwait(false)` mistakes — they're already in Sprint 3 / 5 code.

11. **What happens if user closes `LoginWindow` without logging in?**
    - Sprint 5's `SetupWizardWindow` close = exit (no state on disk yet). Sprint 6's `LoginWindow` close = exit too (no useful "in-between" state).
    - **Recommendation:** exit the app on `LoginWindow` close, matching Sprint 5's behaviour.

12. **`MainWindow.LogoutAsync` failure mode — what if `LoginService.LogoutAsync` throws?**
    - It shouldn't; the implementation wraps the two sub-operations in safe-rollback. But if it does, the VM should log and still raise `LoggedOut` — the holder being not-cleared is worse than the user seeing a confused logout button.
    - **Recommendation:** `MainViewModel.LogoutAsync` catches all, logs, and always raises `LoggedOut`. App handler is the final authority.

13. **Wire `ILastActivityTracker.RegisterActivity` to setup wizard too, or only post-login?**
    - The wizard has its own busy state; auto-lock during setup is a different design question (lock what — there is no DEK yet).
    - **Recommendation:** only post-login. The wizard cannot be auto-locked; an idle user in the middle of setup just sees the wizard sitting there.

14. **Polish error text for `VaultCorruptedException.Reason = DekFileFormat`?**
    - This is the case where `DekFile.ReadAsync` cannot parse the file (truncated, wrong version, etc).
    - **Recommendation:** *"Plik sejfu jest uszkodzony. W Sprincie 7 pojawi się odzyskiwanie z frazy BIP39."* — sets expectations honestly.

15. **`AutoLockMonitor` Singleton vs Transient?**
    - One monitor per process; reuse across logout / re-login. Singleton.
    - **Recommendation:** Singleton. App calls `Start`/`Stop` on the same instance.

## Notes

- Hard rule #3 (Core has zero UI dependencies) — same as Sprint 5. Login VMs in Application; views in Desktop.
- Hard rule #6 (master password / BIP39 / keys never logged) — `LoginService` logs only "Login completed", never the password or key bytes. The `InvalidMasterPasswordException` constructor takes no parameters precisely to avoid accidental "password was: ..." style messages.
- Hard rule #8 (every migration runs pre-migration-backup) — not in play for login (no migration runs).
- `Coffer.Application` gets a second VM family (`Login/` + `Main/`) alongside Sprint 5's `Setup/`. Folder convention stays the same.
- Sprint 6 closes Phase 0. The closure PR is also the moment to update `docs/architecture/10-roadmap.md` Phase 0 checkboxes — a documentation change owned by the closure PR rather than the implementation PR.
- Sprint 6 is smaller than Sprint 5: ~43 steps vs Sprint 5's 47, but with far less new UI surface (one window vs five views).

# Sprint 2 — IKeyVault + WindowsDpapiKeyVault + testy round-trip

**Faza:** 0 (Foundation)
**Status:** Planowany
**Zależności:** sprint-1

## Cel

Coffer ma w `Coffer.Core` abstrakcję `IKeyVault` dla wrażliwego storage (zakres Sprintu 2: tylko master-key-cache z 7-dniowym TTL). Implementacja `WindowsDpapiKeyVault` w `Coffer.Infrastructure` używa DPAPI (`DataProtectionScope.CurrentUser`) — zaszyfrowany cache jest czytelny tylko dla zalogowanego użytkownika Windows. `InMemoryKeyVault` jako cross-platform fallback dla dev/test na non-Windows. DI rejestruje implementację per OS. Testy pokrywają round-trip, TTL expiry, invalidation, missing/corrupted cache.

## Strategia

Sprint 2 implementuje **wyłącznie master-key-cache** część `IKeyVault`. Inne odpowiedzialności umbrellowego `IKeyVault` z `docs/architecture/01-stack-and-projects.md` (DEK loading/saving, OAuth refresh tokens, API keys) dochodzą w późniejszych sprintach (Sprint 3 DEK, Faza 3 OAuth refresh). YAGNI z [CLAUDE.md](../../../CLAUDE.md): nie dorzucamy metod do interface'u dopóki nie są realnie potrzebne.

DPAPI jest Windows-only. `WindowsDpapiKeyVault` używa `[SupportedOSPlatform("windows")]` żeby analizator wiedział. `InMemoryKeyVault` daje cross-platform fallback dla CI (Ubuntu) i ewentualnej pracy na Linux/macOS — nie nadaje się do produkcji, ale działa dla testów i dev.

Sprint idzie przez dwa PR-y zgodnie z naszą regułą:
1. **PR planu** (`chore/plan-sprint-2`) — sam ten plan + szkielet log.md + update index.md
2. **PR implementacji** (`feature/sprint-2-keyvault-dpapi`) — kod, testy, manualny run, zamknięcie sprintu (lub osobny closure PR analogicznie do Sprintu 1)

## Kroki

### A. Pakiety NuGet

- [ ] 2.1 `Coffer.Infrastructure` — dodać `System.Security.Cryptography.ProtectedData` (`9.*`). To pakiet potrzebny dla `ProtectedData.Protect/Unprotect` na .NET 9 (nie jest w core BCL).
- [ ] 2.2 `tests/Coffer.Infrastructure.Tests` — dodać `Xunit.SkippableFact` (`1.*`). Pozwala na `Skip.IfNot(OperatingSystem.IsWindows(), "...")` w testach DPAPI; xUnit raportuje skipped (nie passed) gdy warunek nie spełniony — czytelniejsza diagnostyka w CI.

### B. IKeyVault interface w Coffer.Core

- [ ] 2.3 `src/Coffer.Core/Security/IKeyVault.cs`:
  ```csharp
  public interface IKeyVault
  {
      Task<byte[]?> GetCachedMasterKeyAsync(CancellationToken ct);
      Task SetCachedMasterKeyAsync(byte[] masterKey, TimeSpan ttl, CancellationToken ct);
      Task InvalidateMasterKeyCacheAsync(CancellationToken ct);
  }
  ```
  - Async zgodnie z [conventions.md](../../../docs/conventions.md) "All I/O methods are async"
  - `CancellationToken ct` wymagany (nie default na library API)
  - Zwraca kopię master key (caller odpowiada za `Array.Clear` po użyciu)
- [ ] 2.4 `Coffer.Core/Security/` jako nowy folder — namespace `Coffer.Core.Security`

### C. WindowsDpapiKeyVault

- [ ] 2.5 `src/Coffer.Infrastructure/Security/CofferPaths.cs` — helper dla cross-platform paths:
  - `LocalAppDataFolder()` zwraca `Path.Combine(Environment.GetFolderPath(SpecialFolder.LocalApplicationData), "Coffer")`
  - `MasterKeyCacheFile()` zwraca `Path.Combine(LocalAppDataFolder(), "master-key.dpapi.cache")`
  - Dorzucamy teraz — w Sprintach 3-4 dojdą `DekFile()`, `DatabaseFile()`, ścieżki rosną organicznie
- [ ] 2.6 `src/Coffer.Infrastructure/Security/WindowsDpapiKeyVault.cs`:
  - Klasa oznaczona `[SupportedOSPlatform("windows")]`
  - Konstruktor parametryczny `WindowsDpapiKeyVault(string cacheFilePath)` + konstruktor bezparametrowy używający `CofferPaths.MasterKeyCacheFile()` — test isolation
  - Format pliku (przed DPAPI): `[expiresAtUtcTicks: 8 bajtów BinaryWriter][keyLength: 4 bajty][masterKey: N bajtów]`
  - `SetCachedMasterKeyAsync`:
    1. Serialize plain bytes (timestamp + length + key)
    2. `ProtectedData.Protect(plain, optionalEntropy: null, DataProtectionScope.CurrentUser)`
    3. `Directory.CreateDirectory` dla parent
    4. `File.WriteAllBytesAsync` z `ct`
    5. `Array.Clear(plain, 0, plain.Length)` w `try-finally`
  - `GetCachedMasterKeyAsync`:
    1. `File.Exists` — jeśli nie, return `null`
    2. `File.ReadAllBytesAsync` z `ct`
    3. `ProtectedData.Unprotect(...)` — jeśli rzuca, return `null` (corrupted lub innego usera)
    4. Deserialize, sprawdź `expiresAt > DateTime.UtcNow`
    5. Jeśli expired: usuń plik (best-effort, swallow exception), return `null`
    6. Inaczej: zwróć kopię master key bytes
    7. `Array.Clear(plain, 0, plain.Length)` w `try-finally`
  - `InvalidateMasterKeyCacheAsync`: `File.Delete` if exists (swallow `IOException` jeśli inny proces ma plik otwarty — rare)

### D. InMemoryKeyVault (cross-platform fallback)

- [ ] 2.7 `src/Coffer.Infrastructure/Security/InMemoryKeyVault.cs`:
  - Klasa cross-platform, fallback dla non-Windows
  - Pola: `byte[]? _key`, `DateTime _expiresAtUtc`
  - Wszystkie metody async, ale wewnętrznie `Task.CompletedTask` / `Task.FromResult(...)` — nie ma I/O
  - W komentarzu XML doc na klasie: "Cross-platform fallback used on non-Windows hosts. NOT suitable for production (state lost on process exit) — production on Windows uses `WindowsDpapiKeyVault`, mobile uses MAUI SecureStorage (Sprint TBD)."
  - Wprowadzony teraz żeby ServiceCollection w `AddCofferInfrastructure` mógł zarejestrować coś na każdej platformie i CI na Ubuntu nie rzucał `NotSupportedException`

### E. DI registration

- [ ] 2.8 Update `src/Coffer.Infrastructure/DependencyInjection/ServiceRegistration.cs`:
  - Nowa metoda `AddCofferKeyVault(this IServiceCollection)`:
    ```csharp
    services.AddSingleton<IKeyVault>(_ =>
        OperatingSystem.IsWindows()
            ? new WindowsDpapiKeyVault()
            : new InMemoryKeyVault());
    ```
  - `AddCofferInfrastructure` wywołuje `AddCofferKeyVault()` po `AddCofferLogging()`
  - `Log.Warning` lub `Log.Information` jeśli wybrana implementacja to InMemoryKeyVault — sygnał że to dev/test mode

### F. Testy

- [ ] 2.9 `tests/Coffer.Infrastructure.Tests/Security/InMemoryKeyVaultTests.cs` — kontraktowe testy (cross-platform, działają w CI Ubuntu):
  - 2.9.a `GetCachedMasterKeyAsync_WhenEmpty_ReturnsNull`
  - 2.9.b `SetThenGet_RoundTrip_ReturnsSameBytes`
  - 2.9.c `Get_AfterTtlExpired_ReturnsNull`
  - 2.9.d `Invalidate_ThenGet_ReturnsNull`
  - 2.9.e `Set_OverwritesPreviousKey`
- [ ] 2.10 `tests/Coffer.Infrastructure.Tests/Security/WindowsDpapiKeyVaultTests.cs` — DPAPI tests (skipped na non-Windows przez SkippableFact):
  - 2.10.a `SetThenGet_RoundTrip_ReturnsSameBytes` — używa temp folder przez konstruktor parametryczny
  - 2.10.b `Set_WritesEncryptedFile_NotPlaintext` — czyta surowe bajty pliku, weryfikuje że oryginalny key nie pojawia się jako substring
  - 2.10.c `Get_AfterTtlExpired_ReturnsNullAndDeletesFile`
  - 2.10.d `Get_WhenFileMissing_ReturnsNull`
  - 2.10.e `Get_WhenFileCorrupted_ReturnsNull` — zapisuje śmieci do pliku cache i sprawdza że `Get` nie rzuca, zwraca null
  - 2.10.f `Invalidate_DeletesCacheFile`
  - 2.10.g `Set_CreatesParentDirectory` — usuwa folder, set powinien go odtworzyć
  - W każdym teście: izolowany temp folder via `Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())`, cleanup w `IDisposable.Dispose()` na klasie testowej

### G. Walidacja i merge

- [ ] 2.11 `dotnet build` + `dotnet test` + `dotnet format --verify-no-changes` zielono lokalnie (na Windows — wszystkie testy run, w tym DPAPI)
- [ ] 2.12 Manualne sprawdzenie: zapisać cache (PowerShell repl albo small console app), zamknąć proces, odczytać z drugiego procesu — sprawdza że DPAPI persistencja działa cross-process dla tego samego usera
- [ ] 2.13 Commit na `feature/sprint-2-keyvault-dpapi`, push, PR
- [ ] 2.14 CI zielony — InMemoryKeyVaultTests run, WindowsDpapiKeyVaultTests skipped na Ubuntu CI (z czytelnym "Skipped: DPAPI only available on Windows")
- [ ] 2.15 Squash-merge, branch usunięty
- [ ] 2.16 Osobny closure PR `chore/close-sprint-2`: status w `sprint-2.md` → Zamknięty, finalne wpisy w `log.md`, update `index.md`

## Definition of Done

1. `IKeyVault` w `Coffer.Core/Security/IKeyVault.cs` — trzy async metody (Get/Set/Invalidate master key cache)
2. `WindowsDpapiKeyVault` w Coffer.Infrastructure — DPAPI-encrypted, 7-day TTL (właściwie dowolny TTL przez parametr — 7 dni to UI decision), `CurrentUser` scope
3. `InMemoryKeyVault` w Coffer.Infrastructure — cross-platform fallback
4. DI rejestracja w `AddCofferInfrastructure`: per OS picking
5. Testy: 5 dla InMemory (CI Ubuntu uruchamia), 7 dla DPAPI (lokalnie na Windows uruchamia, w CI Ubuntu skipped). Wszystkie pass lokalnie na Windows.
6. `dotnet build` + `dotnet test` + `dotnet format` zielono lokalnie i w CI
7. `Coffer.Core` zostaje czysty — żadnych referencji do `ProtectedData`, `System.Security.*` poza standardowymi BCL types użytych przez interfejs (CancellationToken, byte[], Task)

## Dotykane pliki

**Nowe:**
- `src/Coffer.Core/Security/IKeyVault.cs`
- `src/Coffer.Infrastructure/Security/CofferPaths.cs`
- `src/Coffer.Infrastructure/Security/WindowsDpapiKeyVault.cs`
- `src/Coffer.Infrastructure/Security/InMemoryKeyVault.cs`
- `tests/Coffer.Infrastructure.Tests/Security/InMemoryKeyVaultTests.cs`
- `tests/Coffer.Infrastructure.Tests/Security/WindowsDpapiKeyVaultTests.cs`

**Modyfikowane:**
- `src/Coffer.Infrastructure/Coffer.Infrastructure.csproj` — PackageReference `System.Security.Cryptography.ProtectedData`
- `src/Coffer.Infrastructure/DependencyInjection/ServiceRegistration.cs` — `AddCofferKeyVault` + wywołanie z `AddCofferInfrastructure`
- `tests/Coffer.Infrastructure.Tests/Coffer.Infrastructure.Tests.csproj` — PackageReference `Xunit.SkippableFact`
- `.ai/sprints/sprint-2/sprint-2.md` — checkboxy, status
- `.ai/sprints/sprint-2/log.md` — postęp
- `.ai/sprints/index.md` — status

## Otwarte pytania

1. **Async w `IKeyVault` mimo że DPAPI jest sync** — wewnątrz `WindowsDpapiKeyVault` `ProtectedData.Protect/Unprotect` to sync API. File I/O używamy `ReadAllBytesAsync/WriteAllBytesAsync` (prawdziwie async). Dla DPAPI calls: `await Task.Run(() => ProtectedData.Protect(...))` (osobny wątek) albo zostawić blocking call w async metodzie?
   - **Rekomendacja:** zostawić blocking — DPAPI jest szybkie (mikrosekundy), `Task.Run` overhead niewart. Async API utrzymujemy dla future-proofing (mobile SecureStorage będzie naprawdę async).

2. **`CofferPaths` w Sprint 2 czy poczekać aż urośnie?** Dodanie helpera teraz dla jednej ścieżki (master key cache) to mała inwestycja, ale dotyk YAGNI.
   - **Rekomendacja:** **dodaję teraz** — Sprint 3 doda `DekFile()`, Sprint 4 `DatabaseFile()`. Ścieżki rosną organicznie, helper ułatwia.

3. **`InMemoryKeyVault` w `Coffer.Infrastructure` czy w test project?** Jeśli ma być produkcyjny DI fallback dla non-Windows, musi być w Infrastructure. Jeśli to tylko fixture, w testach.
   - **Rekomendacja:** **w `Coffer.Infrastructure`** — production fallback dla dev na non-Windows. Z explicit warning w XML doc i logu na starcie ("InMemoryKeyVault selected — non-Windows environment, state lost on process exit").

4. **Master key length validation w interfejsie?** `IKeyVault.SetCachedMasterKeyAsync(byte[], TimeSpan, ct)` — czy `byte[]` ma constraint długości (np. 32 bajty)?
   - **Rekomendacja:** **bez constraintu** — IKeyVault jest agnostic. Walidacja długości to odpowiedzialność wyższej warstwy (Argon2KeyDerivation w Sprint 3).

5. **`Xunit.SkippableFact` czy `[Fact]` z early return?** Skip raportuje czytelnie w xUnit output. Early return jest passed (zielono mimo że nic nie zrobił).
   - **Rekomendacja:** **`Xunit.SkippableFact`** — wymaga jednego dodatkowego PackageReference w test project, ale daje czytelne raporty w CI ("Skipped: DPAPI only on Windows").

6. **Test isolation dla `WindowsDpapiKeyVault`** — konstruktor parametryczny przyjmujący ścieżkę cache vs hardcoded path.
   - **Rekomendacja:** **konstruktor parametryczny** (`WindowsDpapiKeyVault(string cacheFilePath)`) + konstruktor bezparametrowy z default path. Testy używają temp folder, prod konstruktor bezparametrowy.

7. **Memory hygiene** — `Array.Clear` po użyciu na buforach z master key plain bytes (zgodnie z `09-security-key-management.md` "Memory hygiene")
   - **Rekomendacja:** **tak**, w `try-finally` w `Set/Get`. Pole `_sensitivePropertyNames` z Sprintu 1 wzorzec już ustanowiony.

8. **Domyślny TTL 7 dni?** Plan z roadmapy mówi "7 dni" dla DPAPI cache. Czy to ma być wymuszone w `WindowsDpapiKeyVault` (`Set` ignoruje parametr `ttl` i zawsze 7 dni)? Czy `ttl` to wybór callera (zwykle wywołującego z `TimeSpan.FromDays(7)`)?
   - **Rekomendacja:** `ttl` to **parametr callera** (interface mówi "podajesz TTL"). Sprint 5 (setup wizard) i Sprint 6 (login) będą wywoływać z `TimeSpan.FromDays(7)` — w jednym miejscu, łatwo zmienić.

## Notatki

- Plan tego sprintu sam idzie przez PR (`chore/plan-sprint-2`) zgodnie z workflow rule
- Implementacja na osobnym branchu (`feature/sprint-2-keyvault-dpapi`) — kolejny PR po zatwierdzeniu planu
- Closure w osobnym PR-rze (`chore/close-sprint-2`) po merge implementacji — wzorzec ustanowiony w Sprincie 1
- Kod, komentarze i nazwy po angielsku; log sprintu po polsku
- Hard rule #3 trzymamy ściśle: `Coffer.Core` nie dostaje żadnych Windows-specific lub crypto deps. Tylko interface `IKeyVault` z czystym BCL contractem (byte[], TimeSpan, CancellationToken, Task)
- Wciąż otwarty follow-up z Sprintu 0: bump GitHub Actions z Node 20 do Node 24 (deadline 2026-09-16). Może iść jako osobny chore PR równolegle do tego sprintu lub po zamknięciu Sprintu 2.

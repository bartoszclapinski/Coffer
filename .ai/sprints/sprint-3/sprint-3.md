# Sprint 3 — Argon2 + BIP39 + dek.encrypted + AES-GCM

**Faza:** 0 (Foundation)
**Status:** W toku
**Zależności:** sprint-2

## Cel

Coffer ma w `Coffer.Core/Security` interfejsy `IMasterKeyDerivation` i `ISeedManager` z BCL-only sygnaturami. Implementacje `Argon2KeyDerivation` (Konscious, Argon2id 64MB/3/4) i `Bip39SeedManager` (NBitcoin) w `Coffer.Infrastructure/Security`. Pełny pipeline: master password → Argon2id → master key → AES-GCM encrypts DEK → file `dek.encrypted`. Recovery ścieżka: BIP39 mnemonic → PBKDF2-HMAC-SHA512 → recovery key → AES-GCM decrypts DEK. Testy z oficjalnymi BIP39 test vectors weryfikują kompatybilność.

## Strategia

Sprint 3 buduje **fundament crypto** ale nie używa go jeszcze — to Sprint 5 (setup wizard) doloży użycie w pierwszym uruchomieniu aplikacji.

Pattern z Sprintu 2 utrzymany: interfejsy w Core (BCL-only), implementacje w Infrastructure (z third-party deps). DI rejestruje implementacje jako Singletony — bezstanowe.

Async API dla wszystkich CPU-intensive metod (Argon2id, PBKDF2): UI wątek Avalonia w Sprincie 5 nie może być zablokowany ~1-2 sekundami na derivacji.

Format DEK file: `[version: 1B][argonParams: 20B][salt: 16B][iv: 12B][tag: 16B][ciphertext: N B]` — argon params w pliku żeby starsze DEK'i mogły być derived z tymi samymi parametrami nawet jeśli defaults się zmienią.

Trzy PR-y (z odpowiadającymi issues, zgodnie z workflow ustalonym w issue #10):
1. **Plan** (`chore/plan-sprint-3`, issue #12) — ten plan
2. **Implementacja** (`feature/sprint-3-crypto-core`, nowy issue) — kod + testy
3. **Closure** (`chore/close-sprint-3`, nowy issue) — bookkeeping po merge implementacji

## Kroki

### A. Pakiety NuGet

- [x] 3.1 `Coffer.Infrastructure` — dodać `Konscious.Security.Cryptography.Argon2` (`1.*`) dla Argon2id; `NBitcoin` (`7.*`) dla BIP39
- [x] 3.2 `tests/Coffer.Infrastructure.Tests` — bez nowych package'ów (FluentAssertions + xUnit + SkippableFact już są)

### B. Wartości i interfejsy w Coffer.Core

- [x] 3.3 `Coffer.Core/Security/Argon2Parameters.cs` — positional record `Argon2Parameters(int MemorySizeKb, int Iterations, int Parallelism, int OutputBytes, int SaltBytes)` z static `Default` (64MB / 3 / 4 / 32 / 16) zgodnie z [09-security-key-management.md](../../../docs/architecture/09-security-key-management.md)
- [x] 3.4 `Coffer.Core/Security/IMasterKeyDerivation.cs` — `Task<byte[]> DeriveMasterKeyAsync(string password, byte[] salt, Argon2Parameters parameters, CancellationToken ct)`
- [x] 3.5 `Coffer.Core/Security/ISeedManager.cs`:
  - `string GenerateMnemonic()` (sync, fast — generates random 12-word phrase)
  - `bool IsValid(string mnemonic)` (sync, fast — checksum + wordlist validation)
  - `Task<byte[]> DeriveRecoveryKeyAsync(string mnemonic, string passphrase, CancellationToken ct)` (async, PBKDF2 expensive; passphrase wymagany parametr — caller decyduje, `""` dla BIP39 standard)

### C. Implementacje w Coffer.Infrastructure

- [x] 3.6 `Coffer.Infrastructure/Security/Argon2KeyDerivation.cs`:
  - `Argon2KeyDerivation : IMasterKeyDerivation`
  - Używa `Konscious.Security.Cryptography.Argon2id`
  - `DeriveMasterKeyAsync` → `await Task.Run(() => { var bytes = Encoding.UTF8.GetBytes(password); try { ... argon2id.GetBytes(parameters.OutputBytes) ... } finally { Array.Clear(bytes, 0, bytes.Length); } }, ct)`
  - Memory hygiene: bajty hasła zerowane w try-finally po `GetBytes` callu
  - Cancellation: ct propagowane do Task.Run; Argon2 sam się nie anuluje, ale jeśli ct cancelled przed startem — `OperationCanceledException`
- [x] 3.7 `Coffer.Infrastructure/Security/Bip39SeedManager.cs`:
  - `Bip39SeedManager : ISeedManager`
  - `GenerateMnemonic` → `new Mnemonic(Wordlist.English, WordCount.Twelve).ToString()`
  - `IsValid` → `try { new Mnemonic(phrase, Wordlist.English); return true; } catch { return false; }` zgodnie z 09-security example
  - `DeriveRecoveryKeyAsync` → `await Task.Run(() => new Mnemonic(mnemonic, Wordlist.English).DeriveSeed(passphrase).AsSpan(0, 32).ToArray(), ct)` (pierwsze 32 bajty z 512-bit seed)
- [x] 3.8 `Coffer.Infrastructure/Security/AesGcmCrypto.cs` — static helpers:
  - `public sealed record AesGcmResult(byte[] Iv, byte[] Ciphertext, byte[] Tag)`
  - `AesGcmResult Encrypt(byte[] plaintext, byte[] key, byte[]? associatedData = null)`:
    - IV: 12 bajtów losowe (`RandomNumberGenerator.GetBytes(12)`)
    - Tag: 16 bajtów (max AES-GCM)
    - Używa `System.Security.Cryptography.AesGcm` (BCL)
  - `byte[] Decrypt(byte[] ciphertext, byte[] iv, byte[] tag, byte[] key, byte[]? associatedData = null)`:
    - Rzuca `CryptographicException` przy tamper detection (zlecone przez AesGcm.Decrypt)
- [x] 3.9 `Coffer.Infrastructure/Security/DekFile.cs`:
  - `public sealed record DekFile(byte Version, Argon2Parameters ArgonParameters, byte[] Salt, byte[] Iv, byte[] Tag, byte[] Ciphertext)`
  - `static async Task WriteAsync(DekFile file, string path, CancellationToken ct)` — binarny layout:
    - Version (1B) | MemKb (4B) | Iter (4B) | Par (4B) | OutBytes (4B) | SaltBytes (4B) | Salt (N B) | Iv (12B) | Tag (16B) | CipherLength (4B) | Ciphertext (N B)
    - Używa `BinaryWriter` na `MemoryStream` → `File.WriteAllBytesAsync`
  - `static async Task<DekFile> ReadAsync(string path, CancellationToken ct)` — parsuje, rzuca `InvalidDataException` przy malformed payload, `FileNotFoundException` jeśli plik nie istnieje
- [x] 3.10 `Coffer.Infrastructure/Security/CofferPaths.cs` — dorzucić `public static string DekFile() => Path.Combine(LocalAppDataFolder(), "dek.encrypted")` (uwaga: nazwa metody `DekFile` koliduje z typem `DekFile` — zmienić na `DekFilePath()` lub `EncryptedDekFile()`)

### D. DI registration

- [x] 3.11 Update `Coffer.Infrastructure/DependencyInjection/ServiceRegistration.cs`:
  - Nowa metoda `AddCofferCrypto(this IServiceCollection)`:
    ```csharp
    services.AddSingleton<IMasterKeyDerivation, Argon2KeyDerivation>();
    services.AddSingleton<ISeedManager, Bip39SeedManager>();
    return services;
    ```
  - `AddCofferInfrastructure` wywołuje `AddCofferCrypto` po `AddCofferKeyVault`
  - `AesGcmCrypto` jest static — nie potrzebuje DI; `DekFile` to record + static metody — też bez DI

### E. Testy

- [x] 3.12 `tests/Coffer.Infrastructure.Tests/Security/Argon2KeyDerivationTests.cs` (4 testy):
  - 3.12.a `Derive_WithSamePasswordAndSalt_ProducesSameKey` (deterministic check)
  - 3.12.b `Derive_WithDifferentSalts_ProducesDifferentKeys`
  - 3.12.c `Derive_OutputBytes_MatchesParametersOutputBytes`
  - 3.12.d `Derive_CanBeCancelled_BeforeStart_Throws` (CT cancelled before call → OperationCanceledException)
- [x] 3.13 `tests/Coffer.Infrastructure.Tests/Security/Bip39SeedManagerTests.cs` (6 testów):
  - 3.13.a `Generate_Produces12WordsAcceptedByIsValid` (round-trip)
  - 3.13.b `IsValid_OfficialBip39Vector_ReturnsTrue` (Theory z 3 oficjalnymi vectorami z https://github.com/trezor/python-mnemonic/blob/master/vectors.json)
  - 3.13.c `IsValid_InvalidChecksum_ReturnsFalse` (modify last word in valid mnemonic)
  - 3.13.d `IsValid_NonBip39Word_ReturnsFalse` (gibberish words)
  - 3.13.e `DeriveRecoveryKey_OfficialBip39Vector_ProducesExpectedSeed` (Theory z 3 vectorami: known `mnemonic` + `passphrase` → znany pierwszy 32B seed)
  - 3.13.f `DeriveRecoveryKey_DifferentPassphrase_ProducesDifferentSeed`
- [x] 3.14 `tests/Coffer.Infrastructure.Tests/Security/AesGcmCryptoTests.cs` (4 testy):
  - 3.14.a `Encrypt_ThenDecrypt_RoundTrips`
  - 3.14.b `Decrypt_WithTamperedCiphertext_ThrowsCryptographicException`
  - 3.14.c `Decrypt_WithWrongKey_ThrowsCryptographicException`
  - 3.14.d `Encrypt_ProducesUniqueIvForEachCall`
- [x] 3.15 `tests/Coffer.Infrastructure.Tests/Security/DekFileTests.cs` (3 testy):
  - 3.15.a `WriteThenRead_RoundTrip_ReturnsEquivalentFile` (używa temp folder + `IDisposable.Dispose` cleanup, wzorzec z Sprint 2 DPAPI tests)
  - 3.15.b `ReadAsync_FromMissingFile_ThrowsFileNotFoundException`
  - 3.15.c `ReadAsync_FromCorruptedFile_ThrowsInvalidDataException`

### F. Walidacja i merge

- [x] 3.16 `dotnet build` + `dotnet test` + `dotnet format --verify-no-changes` zielono lokalnie (Windows). Na Ubuntu CI też powinno przejść — Konscious i NBitcoin są cross-platform.
- [x] 3.17 `gh issue create` dla implementacji — title `feat(sprint-3): crypto core (Argon2, BIP39, AES-GCM, DEK file)`
- [ ] 3.18 Commit na `feature/sprint-3-crypto-core`, push, `gh pr create` z `Closes #<impl-issue>` w body, label `feat` + `sprint-3`
- [ ] 3.19 CI zielony, squash-merge, branch usunięty
- [ ] 3.20 `gh issue create` dla closure → osobny `chore/close-sprint-3` PR analogicznie do Sprintów 1-2

## Definition of Done

1. 3 nowe pliki w `Coffer.Core/Security/`: `Argon2Parameters.cs`, `IMasterKeyDerivation.cs`, `ISeedManager.cs`
2. 4 nowe pliki w `Coffer.Infrastructure/Security/`: `Argon2KeyDerivation.cs`, `Bip39SeedManager.cs`, `AesGcmCrypto.cs`, `DekFile.cs`
3. `CofferPaths` rozszerzony o metodę zwracającą ścieżkę `dek.encrypted`
4. DI: `AddCofferCrypto` chained w `AddCofferInfrastructure`; `IMasterKeyDerivation` i `ISeedManager` jako Singletony
5. **17 nowych testów pass** lokalnie i w CI Ubuntu: 4 Argon2 + 6 BIP39 (w tym 3 z oficjalnymi vectorami) + 4 AES-GCM + 3 DEK file
6. `dotnet build` + `dotnet test` + `dotnet format` zielono w CI i lokalnie
7. PR squash-merged do `main`; implementation issue auto-closed; closure PR też zmergowany ze swoim issue
8. `Coffer.Core` zostaje czysty — żadnych Konscious/NBitcoin/crypto-implementation deps; tylko `Microsoft.Extensions.DependencyInjection.Abstractions` z poprzednich sprintów

## Dotykane pliki

**Nowe:**
- `src/Coffer.Core/Security/Argon2Parameters.cs`
- `src/Coffer.Core/Security/IMasterKeyDerivation.cs`
- `src/Coffer.Core/Security/ISeedManager.cs`
- `src/Coffer.Infrastructure/Security/Argon2KeyDerivation.cs`
- `src/Coffer.Infrastructure/Security/Bip39SeedManager.cs`
- `src/Coffer.Infrastructure/Security/AesGcmCrypto.cs`
- `src/Coffer.Infrastructure/Security/DekFile.cs`
- `tests/Coffer.Infrastructure.Tests/Security/Argon2KeyDerivationTests.cs`
- `tests/Coffer.Infrastructure.Tests/Security/Bip39SeedManagerTests.cs`
- `tests/Coffer.Infrastructure.Tests/Security/AesGcmCryptoTests.cs`
- `tests/Coffer.Infrastructure.Tests/Security/DekFileTests.cs`

**Modyfikowane:**
- `src/Coffer.Infrastructure/Coffer.Infrastructure.csproj` — PackageReferences `Konscious.Security.Cryptography.Argon2`, `NBitcoin`
- `src/Coffer.Infrastructure/Security/CofferPaths.cs` — dorzucenie metody dla ścieżki DEK file
- `src/Coffer.Infrastructure/DependencyInjection/ServiceRegistration.cs` — `AddCofferCrypto` + wywołanie z `AddCofferInfrastructure`
- `.ai/sprints/sprint-3/sprint-3.md` — checkboxy, status
- `.ai/sprints/sprint-3/log.md` — postęp
- `.ai/sprints/index.md` — status

## Otwarte pytania

1. **`DeriveRecoveryKeyAsync` passphrase — wymagany parametr czy default `""`?**
   - 09-security przykład używa `mnemonic.DeriveSeed("Coffer")` (passphrase "Coffer"). BIP39 standard używa pustego (`""`). Test vectors używają pustego.
   - **Rekomendacja:** `passphrase` jako **wymagany parametr** w interfejsie. Caller decyduje. Sprint 5 setup wizard zdecyduje czy używać "Coffer" jako 25th word czy pusty BIP39 standard. Test vectors dostają `""`. 09-security warto zaktualizować w osobnym docs PR (jak ten po Sprincie 2).

2. **`CofferPaths.DekFile()` koliduje z typem `DekFile`** — nazwa metody musi być inna.
   - **Rekomendacja:** `EncryptedDekFilePath()` — jasne że to ścieżka pliku, nie kolizja.

3. **Argon2 cancellation** — Konscious nie wspiera ct natywnie. Task.Run z ct daje cancel-before-start ale nie cancel-mid-call.
   - **Rekomendacja:** **akceptujemy ograniczenie**. Test 3.12.d covers cancel-before-start. Mid-call cancel jest niemożliwe bez forka libki — out of scope.

4. **Argon2Parameters — positional record czy z initializerami?**
   - **Rekomendacja:** **positional record** — `sealed record Argon2Parameters(int MemorySizeKb, int Iterations, int Parallelism, int OutputBytes, int SaltBytes)`. Niezmienialne, kompaktowe.

5. **DEK file version byte — potrzebny?**
   - **Rekomendacja:** **tak**, 1 byte forward-compat. Wersja 1 hardcoded. Future readers mogą wybrać parser na version.

6. **Argon2Parameters w pliku DEK — binary czy JSON?**
   - **Rekomendacja:** **binary** — 5 fields × int32 = 20 bajtów. JSON overkill dla deterministycznej struktury.

7. **Memory hygiene dla master password** — zerujemy bajty po Argon2 call?
   - **Rekomendacja:** **tak**, w try-finally w `Argon2KeyDerivation`. String `password` GC away — limitacja znana (per 09-security "memory hygiene" sekcja).

8. **Cross-platform Argon2?**
   - Konscious to pure managed code — działa wszędzie. NBitcoin też. Żadnych platform attributes.
   - **Rekomendacja:** brak `[SupportedOSPlatform]` na crypto classes — działają na Windows/Linux/macOS.

9. **`Argon2Parameters` default — w pliku Core czy w `Argon2KeyDerivation`?**
   - **Rekomendacja:** **w `Argon2Parameters` jako static `Default`** — Core wie o "rekomendowanych defaults" via wartość, Infrastructure wie jak to wykonać. Caller (Sprint 5) używa `Argon2Parameters.Default`.

## Notatki

- Plan tego sprintu sam idzie przez PR (`chore/plan-sprint-3`, issue #12, ten dokument). Implementacja na osobnym branchu z osobnym issue. Closure też osobny.
- 09-security-key-management.md może wymagać drobnego update'u po Sprincie 3 (np. fix rekomendacji 1 powyżej — passphrase BIP39 jako `""` zamiast "Coffer"). Jeśli tak, osobny chore PR — analogicznie do PR #9 docs alignment po Sprincie 2.
- Hard rule #3: Coffer.Core nie dostaje Konscious/NBitcoin. Tylko interfejsy + `Argon2Parameters` value object.
- Sprint 3 nie używa jeszcze KeyVault ze Sprintu 2 — to Sprint 5 (setup wizard) połączy crypto + vault.
- Workflow z issue #10: każdy z 3 PR-ów Sprintu 3 ma swój issue, label `feat`/`docs` + `sprint-3`.

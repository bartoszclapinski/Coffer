# Log sprintu 3

## 2026-05-14

- issue #14 utworzony dla implementacji (`feat(sprint-3): crypto core (Argon2 + BIP39 + AES-GCM + DEK file)`), labels: `feat` + `sprint-3`. Workflow z issue #10 zastosowany.
- kroki 3.1-3.2 ukończone — dodane `Konscious.Security.Cryptography.Argon2` (`1.*`) i `NBitcoin` (`7.*`) do Infrastructure. Pakiety testowe bez zmian.
- kroki 3.3-3.5 ukończone — `Coffer.Core/Security/`: `Argon2Parameters` (positional record + static `Default`), `IMasterKeyDerivation`, `ISeedManager` (3 metody, ostatnia async z passphrase jako wymagany parametr per rekomendacja #1).
- krok 3.6 ukończony — `Argon2KeyDerivation`: Konscious Argon2id w `Task.Run` z `ct.ThrowIfCancellationRequested()`, memory hygiene przez `Array.Clear` na `passwordBytes` w try-finally.
- krok 3.7 ukończony — `Bip39SeedManager`: NBitcoin `Mnemonic`, `IsValid` catches `FormatException or ArgumentException or InvalidOperationException` (NBitcoin throws different types per failure mode); `DeriveRecoveryKey` zwraca pierwsze 32 bajty z 64-bajtowego seedu.
- problem: pierwszy run testu `IsValid_InvalidChecksum_ReturnsFalse` failed — NBitcoin's `new Mnemonic(string, Wordlist)` **nie weryfikuje checksumu w konstruktorze**, akceptuje strukturalnie poprawne ale checksum-broken mnemoniki. → rozwiązanie: dodano `bip39.IsValidChecksum` check po konstrukcji. Test invalid checksum użył `"abandon × 12"` (kanoniczne off-by-one od valid `"abandon × 11 + about"`).
- krok 3.8 ukończony — `AesGcmCrypto` static helpers + `AesGcmResult` record. Random 12-byte IV, 16-byte tag, BCL `AesGcm`. `CryptographicException` na tamper detection.
- krok 3.9 ukończony — `DekFile` record + binary serializer. Format: `[version: 1B][argonParams: 5×4B = 20B][salt + iv + tag + ciphertext: każdy length-prefixed]`. `ReadAsync` rzuca `FileNotFoundException` / `InvalidDataException` na malformed.
- krok 3.10 ukończony — `CofferPaths.EncryptedDekFilePath()` dodane (nazwa wybrana, żeby nie kolidowała z typem `DekFile`).
- krok 3.11 ukończony — `AddCofferCrypto` w `Coffer.Infrastructure/DependencyInjection/ServiceRegistration.cs`, chained w `AddCofferInfrastructure` po `AddCofferKeyVault`. Oba interfejsy jako Singletony.
- kroki 3.12-3.15 ukończone — 17 nowych testów: 4 Argon2 + 6 BIP39 (3 z oficjalnymi Trezor vectorami: `abandon×11 + about`, `legal winner...`, `letter advice...`) + 4 AES-GCM + 3 DEK file.
- problem: `dotnet format --verify-no-changes` zgłosił 11 błędów `IDE1006: Missing prefix "_"` dla `private const`/`private static readonly` fields w nowych plikach. `.editorconfig` ma `applicable_kinds = field` bez `required_modifiers` filtra — wszystkie private fields (const, readonly, instance) flagged. → rozwiązanie (Path A): rename na `_camelCase` we wszystkich miejscach (`_fastParameters`, `_key`, `_plaintext`, `_recoveryKeyBytes`, `_trezorVector1Mnemonic`, ...).
- decyzja: drift między `docs/conventions.md` ("Constants are PascalCase") a `.editorconfig` (wszystkie private fields → `_camelCase`) odłożony do osobnego follow-up chore PR. Aktualnie analyzer wygrywa.
- problem: post-checkout autocrlf w `tests/Coffer.Infrastructure.Tests/Security/WindowsDpapiKeyVaultTests.cs` ze Sprintu 2 (znany side-effect na Windows + autocrlf=true) → rozwiązanie: `dotnet format` (bez --verify) naprawił.
- krok 3.16 ukończony — `dotnet build` (0 warnings, 0 errors), `dotnet test` 39 pass total (1 Core + 3 Application + 35 Infrastructure: 5 InMemory + 7 DPAPI + 2 Serilog + 17 nowe Sprintu 3), `dotnet format --verify-no-changes` zielono.

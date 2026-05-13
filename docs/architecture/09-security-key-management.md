# 09 — Security and Key Management

## Threat model — who we're protecting against

We're explicit about which threats we address and which we don't. Knowing this helps avoid security theater and missing real protections.

### In scope

- **Lost or stolen device.** Attacker has the device but doesn't know the master password. Goal: data unreadable.
- **Cloud provider compromise.** Google Drive's storage is breached or scanned. Goal: backed-up data unreadable without keys.
- **OS reinstall.** User reinstalls Windows; DPAPI keys lost. Goal: backups + BIP39 seed allow full recovery.
- **Casual snooping.** Family member opens the app on the user's PC. Goal: basic auth wall (master password + auto-lock).

### Out of scope

- **State-level adversary with kernel access.** If they're in your kernel, they can keylog the master password. We don't try to defend against this.
- **Physical attacks while app is running and unlocked.** Memory contains the DEK; cold-boot attack feasibility ignored for this use case.
- **Compromised AI provider.** Anthropic/OpenAI could theoretically log prompts. We mitigate via prompt anonymization, but absolute privacy from API providers is impossible while using their APIs.
- **Insider threat at Google.** A rogue Google employee with infra access. Drive uses encryption-at-rest, but a determined insider could potentially decrypt; we add our own encryption layer specifically against this.

## Three-layer key hierarchy

```
              Master Password                  BIP39 Seed (12 words)
                    │                                  │
                    │ Argon2id KDF                     │ PBKDF2-HMAC-SHA512
                    │ (64 MB / 3 iter / 4 par)          │ (2048 iter)
                    ▼                                  ▼
              Master Key (256 bit)                Recovery Key (256 bit)
                    │                                  │
                    └────────┬─────────────────────────┘
                             │ AES-GCM decryption
                             ▼
              Database Encryption Key (DEK, 256 bit)
                             │ used as
                             ▼
                  SQLCipher PRAGMA key
```

### Why this design

- **DEK never derives from password directly.** Decoupling means: change master password → re-encrypt DEK only, not the entire DB.
- **Recovery seed is independent.** Forgot password → seed unlocks DEK → set new password.
- **Backups are useful without DPAPI.** DPAPI is just a cache for convenience; not the source of trust.
- **Re-keying is cheap.** Rotating master password is fast.

## Master password

### Requirements

- **Minimum 12 characters**
- **Multiple character classes** (lower + upper + digit + symbol — at least 3 of 4)
- **Not in zxcvbn's top common-passwords list**
- **Not the same as the BIP39 seed phrase**

### Validation

Use `zxcvbn-cs` library (or similar port). Reject passwords scoring below 3 on its 0–4 scale. Show a strength meter in setup UI.

### Argon2id parameters

```csharp
public static class Argon2Config
{
    public const int MemorySizeKb = 65536;       // 64 MB
    public const int Iterations = 3;
    public const int Parallelism = 4;
    public const int OutputBytes = 32;            // 256-bit key
    public const int SaltBytes = 16;
}
```

These produce ~1–2 second derivation on modern CPUs. Tunable later — store the parameters used in the encrypted DEK file so old DEKs can be re-derived correctly even if defaults change.

```csharp
public class MasterKeyDerivation
{
    public byte[] Derive(string password, byte[] salt)
    {
        using var argon = new Argon2id(Encoding.UTF8.GetBytes(password));
        argon.Salt = salt;
        argon.MemorySize = Argon2Config.MemorySizeKb;
        argon.Iterations = Argon2Config.Iterations;
        argon.DegreeOfParallelism = Argon2Config.Parallelism;
        return argon.GetBytes(Argon2Config.OutputBytes);
    }
}
```

## BIP39 seed

### Why BIP39

- 30+ years of cryptographic research and field deployment (Bitcoin, Ethereum, hardware wallets)
- Built-in checksum (12th word validates the first 11) catches transcription errors
- Wordlist mnemonic is more memorable and writable than random 32 hex characters
- Standardized recovery flow people may already be familiar with

### Library

`NBitcoin` (NuGet, mature, used in Bitcoin tooling).

```csharp
public class Bip39SeedManager
{
    public Mnemonic Generate() =>
        new Mnemonic(Wordlist.English, WordCount.Twelve);

    public byte[] DeriveRecoveryKey(string mnemonicPhrase)
    {
        var mnemonic = new Mnemonic(mnemonicPhrase, Wordlist.English);
        var seed = mnemonic.DeriveSeed("Coffer");        // optional passphrase salt
        return seed.AsSpan(0, 32).ToArray();
    }

    public bool IsValid(string mnemonicPhrase)
    {
        try
        {
            _ = new Mnemonic(mnemonicPhrase, Wordlist.English);
            return true;
        }
        catch { return false; }
    }
}
```

### Wordlist language

**English.** Recovery tooling and verifiers commonly assume English. Polish wordlist exists but reduces interoperability with anything outside this app.

### Setup flow

```
1. Generate 12 words
2. Display in a numbered grid with screenshot blocked (SetWindowDisplayAffinity on Windows;
   FLAG_SECURE on Android; iOS shows blank in screenshot but cannot prevent capture entirely)
3. Force a verification step: ask user to enter words #3 and #7
4. Block "Continue" until correct
5. Recommend: "Write these on paper. Don't store digitally. Keep safe."
```

This verification catches "I'll write it down later" failures, which are common.

### Display security

- **Windows:** `SetWindowDisplayAffinity(handle, WDA_EXCLUDEFROMCAPTURE)` on the seed display window. Prevents print screen, screen recording, screen sharing.
- **Android:** `Window.SetFlags(WindowManagerFlags.Secure, WindowManagerFlags.Secure)` on the seed activity.
- **iOS:** No equivalent prevention API. Show seed only after re-authentication and recommend the user not screenshot. Detect screenshot via `UIApplication.UserDidTakeScreenshotNotification` and warn user.

### No clipboard support

The seed display does not have a "copy" button. User must hand-write. This is annoying but necessary — clipboard contents are visible to other software and clipboard managers persist them.

## DEK (Database Encryption Key)

- **Generated:** at first app run, with `RandomNumberGenerator.Fill(...)` — 32 bytes
- **Storage:** encrypted with master key (AES-GCM); ciphertext written to `dek.encrypted` in app folder
- **Format:**

```
File: dek.encrypted
[salt: 16 bytes]
[iv: 12 bytes]
[ciphertext: 32 bytes]
[tag: 16 bytes]
[argon-params: variable]
```

Backups include this file so a foreign device with master password OR seed can decrypt.

## DPAPI cache (Windows desktop only)

User chose 7-day cache. Implementation:

- After successful master password unlock, derive master key, encrypt with DPAPI, write to `master-key.dpapi.cache`
- File timestamps embedded inside the cache to enforce 7-day expiry
- Cache is usable only by the same Windows user on the same machine
- Cache is invalidated:
  - On password change
  - On user-initiated logout
  - On schema migration
  - On detection of OS reinstall (DPAPI handles this automatically — the cache becomes unreadable)
- After 7 days expires, app prompts for password again

Sprint 2 built the production implementation as `WindowsDpapiKeyVault : IKeyVault` in `Coffer.Infrastructure/Security/`. `IKeyVault` is the umbrella contract in `Coffer.Core/Security/`; the master-key-cache methods (`GetCachedMasterKeyAsync`, `SetCachedMasterKeyAsync`, `InvalidateMasterKeyCacheAsync`) are the only members today. Other duties (DEK loading, OAuth refresh tokens) join the interface in their respective sprints.

```csharp
[SupportedOSPlatform("windows")]
public sealed class WindowsDpapiKeyVault : IKeyVault
{
    private readonly string _cacheFilePath;

    public WindowsDpapiKeyVault() : this(CofferPaths.MasterKeyCacheFile()) { }
    public WindowsDpapiKeyVault(string cacheFilePath) => _cacheFilePath = cacheFilePath;

    public async Task<byte[]?> GetCachedMasterKeyAsync(CancellationToken ct)
    {
        if (!File.Exists(_cacheFilePath)) return null;

        var protectedBytes = await File.ReadAllBytesAsync(_cacheFilePath, ct);
        byte[] plainBytes;
        try
        {
            plainBytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
        }
        catch (CryptographicException)
        {
            return null; // Different user, corrupted, or DPAPI key revoked.
        }

        try
        {
            // Payload: [expiresAtUtcTicks: long][keyLength: int][masterKey: N bytes]
            var (expiresAtUtcTicks, masterKey) = ParsePayload(plainBytes);
            if (DateTime.UtcNow.Ticks > expiresAtUtcTicks)
            {
                Array.Clear(masterKey, 0, masterKey.Length);
                TryDeleteCacheFile();
                return null;
            }
            return masterKey;
        }
        finally
        {
            Array.Clear(plainBytes, 0, plainBytes.Length);
        }
    }

    public async Task SetCachedMasterKeyAsync(byte[] masterKey, TimeSpan ttl, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(masterKey);
        Directory.CreateDirectory(Path.GetDirectoryName(_cacheFilePath)!);

        var plainBytes = BuildPayload(DateTime.UtcNow.Add(ttl).Ticks, masterKey);
        try
        {
            var protectedBytes = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);
            await File.WriteAllBytesAsync(_cacheFilePath, protectedBytes, ct);
        }
        finally
        {
            Array.Clear(plainBytes, 0, plainBytes.Length);
        }
    }

    public Task InvalidateMasterKeyCacheAsync(CancellationToken ct)
    {
        TryDeleteCacheFile();
        return Task.CompletedTask;
    }
}
```

`CofferPaths.MasterKeyCacheFile()` returns `%LocalAppData%/Coffer/master-key.dpapi.cache`. The DPAPI scope is `CurrentUser` — the cache is only readable by the same Windows account that wrote it, and becomes unreadable after an OS reinstall (the DPAPI master key is regenerated). Memory hygiene: all plaintext buffers are zeroed in `try-finally` per the rule earlier in this doc.

### Cross-platform fallback

For non-Windows hosts (CI on Ubuntu, future Linux/macOS dev), `Coffer.Infrastructure/Security/InMemoryKeyVault.cs` provides a process-local fallback that satisfies the same interface but persists nothing across restarts. `AddCofferInfrastructure` picks the implementation via `OperatingSystem.IsWindows()` at DI build time and logs the selection when the in-memory fallback is used.

## Mobile secure storage

DPAPI doesn't exist on Android/iOS. Use MAUI's `SecureStorage`:

```csharp
public class MauiSecureStorageKeyVault : IKeyVault
{
    public async Task<byte[]?> GetCachedMasterKeyAsync()
    {
        var stored = await SecureStorage.Default.GetAsync("coffer-master-key-cache");
        if (stored is null) return null;
        var (key, expiresAt) = Deserialize(stored);
        return DateTime.UtcNow > expiresAt ? null : key;
    }

    public async Task SetCachedMasterKeyAsync(byte[] key, TimeSpan ttl) =>
        await SecureStorage.Default.SetAsync("coffer-master-key-cache", Serialize(key, DateTime.UtcNow + ttl));
}
```

Under the hood: Android Keystore (with hardware-backed keys when available), iOS Keychain (with `kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly`).

## Auto-lock

After 15 minutes of inactivity (configurable in Settings), app locks. User must re-enter master password (or wait if cache valid).

Inactivity = no keyboard or mouse input on desktop; no foreground time on mobile.

Implementation: a `LastActivityTracker` updated on every UI input event, checked by a `Timer` every minute.

## Anonymization in AI prompts

Detailed in `04-ai-strategy.md`. Summary:

- Account numbers, IBAN, NIP, REGON, full physical addresses, full names → replaced with placeholders before any LLM call
- Merchant names and category names are kept (no privacy concern, useful signal)
- Even with anonymization, never send a complete user profile to an LLM. Send only what's needed for the specific question.

## Logging — what NOT to log

Serilog file sink captures application logs. Never log:

- Master password (obviously)
- BIP39 seed
- Master key, DEK, recovery key
- Account numbers, full IBAN, NIP, full names
- Full transaction descriptions if they may contain personal data
- API keys (Anthropic, OpenAI, Google)

Use Serilog's destructuring and filters:

```csharp
LoggerConfiguration
    .Destructure.ByTransforming<MasterCredentials>(_ => "[REDACTED]")
    .Filter.ByExcluding(le => le.Properties.ContainsKey("Password"));
```

## Memory hygiene

After use, zero out byte arrays containing sensitive data:

```csharp
try
{
    var key = derive.Derive(password, salt);
    return DecryptDek(key);
}
finally
{
    Array.Clear(key, 0, key.Length);
}
```

For strings (master password input), .NET strings are immutable and may be GC-collected at any time. Use `SecureString` where supported, or accept that password input strings may briefly exist in managed memory and minimize their lifetime.

## Code review checklist for security PRs

- [ ] No new place where DEK or master key is written to disk in plaintext
- [ ] No new logging of sensitive fields
- [ ] No new prompt to LLM containing un-anonymized data
- [ ] Argon2id parameters not weakened
- [ ] Password validation still applied
- [ ] Tests updated for new code paths

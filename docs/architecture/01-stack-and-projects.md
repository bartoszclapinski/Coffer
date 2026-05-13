# 01 — Stack and Projects

## Why this stack

| Concern | Choice | Reasoning |
|---|---|---|
| Desktop UI | Avalonia 11 | Modern, MVVM-first, hot reload, native look across Win/Linux/macOS, ~80% knowledge transferable to/from WPF |
| Mobile UI | .NET MAUI | Native bindings for camera, biometrics, push, background sync. Mature ecosystem in 2026 |
| Shared logic | .NET 9 + C# 13 | Single language across all UIs, strong typing, mature tooling, AI assistance is excellent |
| Persistence | SQLite + EF Core 9 | Local-first, zero-config, great EF Core support, single file = easy backup |
| Encryption | SQLCipher (SQLitePCLRaw.bundle_e_sqlcipher) | Industry standard, transparent to EF Core after setup, AES-256 |
| MVVM | CommunityToolkit.Mvvm | Source generators eliminate boilerplate, supported by Microsoft, works in both Avalonia and MAUI |
| PDF parsing | UglyToad.PdfPig | Apache 2.0, gives letters with X/Y coordinates, sufficient for table extraction |
| Charts | LiveChartsCore.SkiaSharpView | Same API for Avalonia and MAUI, GPU-accelerated, MVVM-friendly |
| Logging | Serilog | File sink for structured logs, rolling files, search-friendly |
| Validation | FluentValidation | Composable, testable, separates rules from models |
| Testing | xUnit + FluentAssertions + Bogus + FsCheck | Industry standard combo, property-based tests for parser edge cases |

## What was rejected and why

- **WPF** — Microsoft has put it in maintenance mode. Windows-only. XAML idioms feel dated.
- **WinUI 3** — Packaging and deployment story is still painful. Documentation gaps. Limited cross-platform.
- **Flutter** — Excellent framework, but exits the .NET ecosystem entirely. Owner's portfolio and skills are .NET. Receipt OCR and matching logic would need a separate .NET backend or full Dart rewrite.
- **iText7 for PDF** — AGPL/commercial license. PdfPig is sufficient and Apache 2.0.
- **PyMuPDF / Python sidecar** — Adds runtime dependency and deployment complexity. PdfPig provides equivalent functionality for our needs.
- **ML.NET for anomaly detection** — Underpowered vs scikit-learn, but we don't need scikit-learn. Hybrid statistics + LLM is sufficient.

## Projects in detail

### Coffer.Core

**Rules:** Zero dependencies on UI frameworks or infrastructure libraries. Allowed external dependencies are limited to:

- the BCL,
- `Microsoft.Extensions.DependencyInjection.Abstractions` (interface-only, for `IServiceCollection` extensions like `AddCofferCore`),
- small focused utility packages introduced when needed (e.g., NodaTime, BIP39 helpers).

No `using Avalonia.*`, no `using Microsoft.Maui.*`, no `using System.Windows.*`, no crypto-implementation or platform-specific deps.

**Contains:**
- Entities: `Transaction`, `Account`, `Category`, `Statement`, `ImportSession`, `Receipt`, `ReceiptItem`, `Goal`, `GoalContribution`, `GoalSnapshot`, `Rule`, `Alert`, `Tag`
- Value objects: `Money`, `Period`, `BankFingerprint`, `TransactionHash`
- Enums: `TransactionType`, `ImportStatus`, `GoalType`, `GoalStatus`, `AlertSeverity`
- Interfaces (abstractions): `IStatementParser`, `IBankDetector`, `IAiCategorizer`, `IReceiptOcr`, `IReceiptMatcher`, `IKeyVault`, `IBackupService`, `ISyncService`, `INotificationService`, `IFilePicker`, `ICamera`, `IGoalStrategy`
- Domain services: `DeduplicationService`, `MoneyMath`, `DateNormalization`

### Coffer.Infrastructure

**Rules:** Implements interfaces from Core. Talks to OS, file system, network, AI APIs, databases. Platform-specific code lives here behind abstractions.

**Folders:**
- `Persistence/` — `CofferDbContext`, EF Core configurations, migrations, repositories
- `Persistence/Encryption/` — SQLCipher key handling, PRAGMA setup, connection interceptor
- `Parsers/` — base classes, `PdfPigParser` helpers
- `Parsers/Pko/`, `Parsers/MBank/`, `Parsers/Ing/`, etc. — bank-specific parsers
- `Parsers/AiAssisted/` — fallback parser using LLM
- `AI/Providers/` — `ClaudeProvider`, `OpenAiProvider`, `ProviderFactory`
- `AI/Categorization/` — `HybridCategorizer`, `RuleEngine`, `BatchCategorizer`, `CategoryCache`
- `AI/Chat/` — tool calling host, tool implementations
- `AI/Anomalies/` — statistical detectors, LLM commentary
- `AI/Vision/` — `ClaudeVisionReceiptOcr`, `ReceiptItemCategorizer`
- `AI/Prompting/` — `PromptAnonymizer`, prompt templates
- `Receipts/` — `ReceiptMatcher` with fuzzy matching
- `Backup/` — local snapshot service, Google Drive uploader
- `Sync/` — event sourcing, Drive sync worker, conflict resolution
- `Security/` — `WindowsDpapiKeyVault` (desktop), `MauiSecureStorageKeyVault` (mobile), `Bip39SeedManager`, `Argon2KeyDerivation`
- `GoogleDrive/` — OAuth2 client, file operations

### Coffer.Application

**Rules:** ViewModels, use cases, mediators. UI-framework-agnostic (no `using Avalonia.*` or `using Microsoft.Maui.*`). Both Desktop and Mobile UI projects reference this.

**Folders:**
- `ViewModels/` — `DashboardViewModel`, `TransactionListViewModel`, `ImportViewModel`, `AdvisorViewModel`, `ReceiptCaptureViewModel`, etc.
- `UseCases/` — `ImportStatementUseCase`, `CategorizeTransactionsUseCase`, `EvaluateGoalUseCase`, `MatchReceiptUseCase`
- `Services/` — orchestration services that coordinate multiple infrastructure pieces
- `Mappings/` — DTO ↔ entity mappers (manual, no AutoMapper — too magic)

### Coffer.Desktop

Avalonia app. Windows is primary, Linux/macOS work but are not actively tested.

**Folders:**
- `Views/` — `.axaml` and code-behind
- `Controls/` — custom reusable controls (`KpiCard`, `TransactionRow`, `CategoryPill`, etc.)
- `Converters/` — value converters for bindings
- `Assets/` — images, fonts, icons
- `App.axaml`, `Program.cs`, `MainWindow.axaml`

### Coffer.Mobile

MAUI app. Android and iOS.

**Folders:**
- `Views/` — `.xaml` and code-behind
- `Controls/` — mobile-specific controls (`ReceiptCameraView`, `BottomNavBar`, etc.)
- `Platforms/Android/`, `Platforms/iOS/` — platform-specific implementations of `ICamera`, `INotificationService`, etc.
- `Resources/`
- `App.xaml`, `MauiProgram.cs`, `AppShell.xaml`

### Coffer.Shared

Tiny project. Holds DTOs used both at sync layer and at application layer. No logic.

## Dependency Injection

Use Microsoft.Extensions.DependencyInjection in both desktop and mobile. Each UI project has its own `ServiceCollection` configured at startup, but they call shared registration extensions:

```csharp
// In Application or Infrastructure
public static class ServiceRegistration
{
    public static IServiceCollection AddCofferCore(this IServiceCollection services) { ... }
    public static IServiceCollection AddCofferInfrastructure(this IServiceCollection services, CofferConfig cfg) { ... }
    public static IServiceCollection AddCofferApplication(this IServiceCollection services) { ... }
}

// In Desktop
services
    .AddCofferCore()
    .AddCofferInfrastructure(config)
    .AddCofferApplication()
    .AddSingleton<IKeyVault, WindowsDpapiKeyVault>()
    .AddSingleton<IFilePicker, AvaloniaFilePicker>();

// In Mobile
services
    .AddCofferCore()
    .AddCofferInfrastructure(config)
    .AddCofferApplication()
    .AddSingleton<IKeyVault, MauiSecureStorageKeyVault>()
    .AddSingleton<ICamera, MauiCamera>();
```

## Lifetimes

- `DbContext` — Scoped (per-operation factory pattern, not Transient — EF Core best practice)
- `IStatementParser` — Singleton (stateless)
- `IAiCategorizer` — Singleton (holds cache)
- ViewModels — Transient
- HTTP clients — Singleton via `IHttpClientFactory`

## Configuration

Use `appsettings.json` only for non-secret defaults. Secrets (API keys, refresh tokens) go through `IKeyVault`. Environment variables override appsettings.

```
appsettings.json           — defaults shipped with the app
appsettings.Development.json — dev-only overrides, in .gitignore
secrets.encrypted          — managed by IKeyVault, in .gitignore
```

## NuGet packages — exact list to start

```
<PackageReference Include="Microsoft.EntityFrameworkCore" Version="9.*" />
<PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="9.*" />
<PackageReference Include="SQLitePCLRaw.bundle_e_sqlcipher" Version="2.*" />
<PackageReference Include="CommunityToolkit.Mvvm" Version="8.*" />
<PackageReference Include="UglyToad.PdfPig" Version="0.1.*" />
<PackageReference Include="Anthropic.SDK" Version="*" />
<PackageReference Include="OpenAI" Version="*" />
<PackageReference Include="Microsoft.Extensions.AI" Version="*" />
<PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="9.*" />
<PackageReference Include="Microsoft.Extensions.Hosting" Version="9.*" />
<PackageReference Include="Microsoft.Extensions.Http" Version="9.*" />
<PackageReference Include="Serilog.Extensions.Logging" Version="*" />
<PackageReference Include="Serilog.Sinks.File" Version="*" />
<PackageReference Include="FluentValidation" Version="11.*" />
<PackageReference Include="LiveChartsCore.SkiaSharpView.Avalonia" Version="2.*" />
<PackageReference Include="LiveChartsCore.SkiaSharpView.Maui" Version="2.*" />
<PackageReference Include="NBitcoin" Version="7.*" />        <!-- BIP39 -->
<PackageReference Include="Konscious.Security.Cryptography.Argon2" Version="1.*" />
<PackageReference Include="Google.Apis.Drive.v3" Version="*" />

<!-- Test projects -->
<PackageReference Include="xunit" Version="*" />
<PackageReference Include="FluentAssertions" Version="*" />
<PackageReference Include="Bogus" Version="*" />
<PackageReference Include="FsCheck.Xunit" Version="*" />
<PackageReference Include="Microsoft.NET.Test.Sdk" Version="*" />
```

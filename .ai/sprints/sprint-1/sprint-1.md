# Sprint 1 — DI + Serilog + Avalonia DI bootstrap

**Faza:** 0 (Foundation)
**Status:** Planowany
**Zależności:** sprint-0

## Cel

Avalonia desktop uruchamia się przez `Microsoft.Extensions.DependencyInjection` (zamiast hardcoded `new MainWindow()` z templateu), wszystkie warstwy rejestrują się przez `AddCofferCore`/`AddCofferInfrastructure`/`AddCofferApplication` extension methods. Serilog skonfigurowany z dwoma sinks (konsola dla dev, plik rolling dla prod), `MainWindow` ma wstrzyknięty `ILogger<MainWindow>` i loguje swoje utworzenie. Wszystko zgodne z [01-stack-and-projects.md](../../../docs/architecture/01-stack-and-projects.md).

## Strategia

Trzymamy się prostoty: nie ciągniemy `IHostBuilder`/`Microsoft.Extensions.Hosting` w Sprint 1 — wystarczy `ServiceCollection.BuildServiceProvider()` zbudowany w `Program.cs`. Migracja na `IHostBuilder` (kiedy będą `IHostedService` workery — sync, daily backup) odłożona do Fazy 3+. Decyzja świadoma, motywacja: mniejsza powierzchnia teraz, łatwa do rozbudowy potem.

Sprint idzie przez dwa PR-y zgodnie z naszą regułą:
1. **PR planu** (`chore/plan-sprint-1`) — sam ten plan + szkielet log.md + update index.md
2. **PR implementacji** (`feature/sprint-1-di-serilog-bootstrap`) — kod, testy, manualny run, zamknięcie sprintu

## Kroki

### A. Pakiety NuGet

- [ ] 1.1 PackageReferences w odpowiednich csproj:
  - 1.1.a `Coffer.Core` — bez zmian (brak deps na MS Extensions zgodnie z hard-rule #3)
  - 1.1.b `Coffer.Application` — `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Logging.Abstractions`
  - 1.1.c `Coffer.Infrastructure` — `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Logging`, `Serilog`, `Serilog.Extensions.Logging`, `Serilog.Sinks.Console`, `Serilog.Sinks.File`
  - 1.1.d `Coffer.Desktop` — `Microsoft.Extensions.DependencyInjection` (concrete)

### B. ServiceRegistration extensions per warstwa

- [ ] 1.2 `Coffer.Core/DependencyInjection/ServiceRegistration.cs` z `AddCofferCore(this IServiceCollection)` — początkowo prawie pusta (return services), wzorzec ustanowiony
- [ ] 1.3 `Coffer.Application/DependencyInjection/ServiceRegistration.cs` z `AddCofferApplication(this IServiceCollection)` — j.w.
- [ ] 1.4 `Coffer.Infrastructure/DependencyInjection/ServiceRegistration.cs` z `AddCofferInfrastructure(this IServiceCollection)` — wywołuje wewnątrz `AddCofferLogging`

### C. Serilog

- [ ] 1.5 `Coffer.Infrastructure/Logging/SerilogConfiguration.cs` — extension `AddCofferLogging(this IServiceCollection)`:
  - 1.5.a `Log.Logger = new LoggerConfiguration()` z minimum level Information, Debug w `#if DEBUG`
  - 1.5.b Console sink
  - 1.5.c File sink rolling, ścieżka: `Path.Combine(Environment.GetFolderPath(SpecialFolder.LocalApplicationData), "Coffer", "logs", "coffer-.log")` (cross-platform — Windows `%LocalAppData%`, Linux/macOS odpowiedniki); `RollingInterval.Day`, `fileSizeLimitBytes: 10_000_000`, `rollOnFileSizeLimit: true`, `retainedFileCountLimit: 30`
  - 1.5.d Property excluding filter dla pól o nazwach `Password`, `MasterKey`, `Dek`, `Mnemonic`, `Seed`, `ApiKey`, `RefreshToken` — zgodnie z [09-security-key-management.md](../../../docs/architecture/09-security-key-management.md) "Logowanie — czego NIE logować"
  - 1.5.e `services.AddLogging(b => b.AddSerilog(dispose: true))` — most ILogger<T> ↔ Serilog
  - 1.5.f Full sensitive-types destructuring filter (np. `MasterCredentials`) zostaje na Sprint 3 gdy te typy faktycznie powstaną — tu tylko property-name filtering

### D. Avalonia DI bootstrap

- [ ] 1.6 `Coffer.Desktop/Program.cs` — przepisz:
  - 1.6.a `var services = new ServiceCollection().AddCofferCore().AddCofferInfrastructure().AddCofferApplication().AddCofferDesktopUi();`
  - 1.6.b `App.Services = services.BuildServiceProvider();`
  - 1.6.c `Log.Information("Coffer starting, version {Version}, runtime {Runtime}", ...)` przed `BuildAvaloniaApp().StartWithClassicDesktopLifetime(args)`
  - 1.6.d `Log.CloseAndFlush()` w `finally` (lub `try-catch-finally` wokół całości)
- [ ] 1.7 `Coffer.Desktop/DependencyInjection/DesktopServiceRegistration.cs` z `AddCofferDesktopUi(this IServiceCollection)`:
  - 1.7.a Rejestruje `MainWindow` jako Singleton (decyzja: single-window app, refactor do Transient w Sprint 5 gdy doszłucze drugie okno setup wizard)
- [ ] 1.8 `Coffer.Desktop/App.axaml.cs`:
  - 1.8.a `public static IServiceProvider Services { get; set; } = null!;`
  - 1.8.b W `OnFrameworkInitializationCompleted`: `desktop.MainWindow = Services.GetRequiredService<MainWindow>();`
- [ ] 1.9 `Coffer.Desktop/MainWindow.axaml.cs`:
  - 1.9.a Konstruktor przyjmuje `ILogger<MainWindow> logger`
  - 1.9.b Loguje `logger.LogInformation("MainWindow created")` po `InitializeComponent()`
  - 1.9.c Pole `_logger` zachowane (przyda się dla późniejszych zdarzeń UI)
- [ ] 1.10 `Coffer.Desktop/MainWindow.axaml` — opcjonalnie dorzucić tekstowy placeholder (np. `<TextBlock>Coffer</TextBlock>`) zamiast "Welcome to Avalonia!"

### E. Testy

- [ ] 1.11 `tests/Coffer.Application.Tests/DependencyInjection/ServiceRegistrationTests.cs` (zastępuje `SmokeTest.cs`):
  - 1.11.a Test: `AddCofferCore() + AddCofferInfrastructure() + AddCofferApplication()` builds without throwing
  - 1.11.b Test: `BuildServiceProvider()` returns non-null
- [ ] 1.12 `tests/Coffer.Infrastructure.Tests/Logging/SerilogConfigurationTests.cs` (zastępuje `SmokeTest.cs`):
  - 1.12.a Test: `AddCofferLogging()` registers `ILoggerFactory`
  - 1.12.b Test: można pozyskać `ILogger<SerilogConfigurationTests>` z DI
  - 1.12.c Test: property filter ukrywa wartość pola `Password` w wyjściu loga (capture sink, weryfikacja string'a)
- [ ] 1.13 `tests/Coffer.Core.Tests/SmokeTest.cs` — zostawić obecny (FluentAssertions wired up) lub przepisać na test pustej `AddCofferCore()` — decyzja w trakcie

### F. Walidacja i merge

- [ ] 1.14 Manualny run: `dotnet run --project src/Coffer.Desktop` — pokazuje się okno; plik `%LocalAppData%\Coffer\logs\coffer-<data>.log` powstaje i zawiera co najmniej dwie linijki ("Coffer starting", "MainWindow created")
- [ ] 1.15 `dotnet build` + `dotnet test` + `dotnet format --verify-no-changes` zielono lokalnie
- [ ] 1.16 Commit na `feature/sprint-1-di-serilog-bootstrap`, push, PR
- [ ] 1.17 CI zielony (build-and-test + format-check)
- [ ] 1.18 Squash-merge, branch usunięty
- [ ] 1.19 Update [log.md](log.md) — finalny wpis "sprint zamknięty"; update [index.md](../index.md) — status Sprint 1 na "Zamknięty"

## Definition of Done

1. `dotnet run --project src/Coffer.Desktop` uruchamia aplikację i pokazuje `MainWindow` z DI container (nie `new MainWindow()`)
2. Plik logu w `%LocalAppData%\Coffer\logs\coffer-<data>.log` powstaje i zawiera linijki o starcie aplikacji i utworzeniu MainWindow
3. `dotnet build` + `dotnet test` + `dotnet format --verify-no-changes` zielono lokalnie i w CI
4. PR squash-merged do `main`, branch usunięty
5. `Coffer.Core` nie ma żadnej reference do `Microsoft.Extensions.*` ani `Serilog` (hard rule #3)

## Dotykane pliki

**Nowe:**
- `src/Coffer.Core/DependencyInjection/ServiceRegistration.cs`
- `src/Coffer.Application/DependencyInjection/ServiceRegistration.cs`
- `src/Coffer.Infrastructure/DependencyInjection/ServiceRegistration.cs`
- `src/Coffer.Infrastructure/Logging/SerilogConfiguration.cs`
- `src/Coffer.Desktop/DependencyInjection/DesktopServiceRegistration.cs`
- `tests/Coffer.Application.Tests/DependencyInjection/ServiceRegistrationTests.cs`
- `tests/Coffer.Infrastructure.Tests/Logging/SerilogConfigurationTests.cs`

**Modyfikowane:**
- `src/Coffer.Application/Coffer.Application.csproj` — PackageReferences
- `src/Coffer.Infrastructure/Coffer.Infrastructure.csproj` — PackageReferences (Serilog stack)
- `src/Coffer.Desktop/Coffer.Desktop.csproj` — PackageReferences (MS DI)
- `src/Coffer.Desktop/Program.cs` — DI bootstrap + Serilog config
- `src/Coffer.Desktop/App.axaml.cs` — resolve MainWindow z DI
- `src/Coffer.Desktop/MainWindow.axaml.cs` — konstruktor z `ILogger<MainWindow>`
- `src/Coffer.Desktop/MainWindow.axaml` — opcjonalnie tekstowy placeholder
- `tests/Coffer.Application.Tests/SmokeTest.cs` — usunięty (zastąpiony)
- `tests/Coffer.Infrastructure.Tests/SmokeTest.cs` — usunięty (zastąpiony)
- `.ai/sprints/sprint-1/sprint-1.md` — checkboxy, status
- `.ai/sprints/sprint-1/log.md` — postęp
- `.ai/sprints/index.md` — status

## Otwarte pytania

- **`Microsoft.Extensions.Hosting`** — odkładamy do Fazy 3+ (gdy będą `IHostedService` workery). Rekomendacja zatwierdzona w "Strategia". Zgoda?
- **MainWindow lifecycle** — Singleton (single-window app) czy Transient (MVVM-friendly)? Rekomendacja: Singleton teraz, refactor w Sprint 5. Zgoda?
- **MainViewModel** — w Sprint 1 nie tworzymy ViewModel (brak stanu do bindowania). Dorzucamy w Sprint 6 gdy doszłucze "logged in as" placeholder. Zgoda?
- **Wersje pakietów** — `Serilog.*` najnowsze stable (4.x); `Microsoft.Extensions.*` 9.* (zgodne z .NET 9 SDK). Pinujemy major (`4.*`, `9.*`) czy konkretne minor? Rekomendacja: major wildcard (`4.*`).
- **Konsola w produkcyjnym buildzie** — Avalonia desktop app nie ma stdout w prod (OutputType=WinExe). Console sink trzymamy tylko w `#if DEBUG`?
  - Rekomendacja: tak, `#if DEBUG` dla Console sink. W release tylko File sink.

## Notatki

- Plan tego sprintu sam idzie przez PR (`chore/plan-sprint-1`) zgodnie z workflow rule
- Implementacja na osobnym branchu (`feature/sprint-1-di-serilog-bootstrap`) — kolejny PR po zatwierdzeniu planu
- Wszystkie nazwy/komentarze w kodzie po angielsku (conventions.md); logi sprintu po polsku
- Hard rule #3 (Core bez deps na UI/framework) trzymamy się ściśle: Core nie dostaje `Microsoft.Extensions.*`

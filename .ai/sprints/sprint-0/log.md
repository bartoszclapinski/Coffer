# Log sprintu 0

## 2026-05-12

- decyzja: repo nie istnieje na GitHubie — tworzymy nowe przez `gh repo create Coffer --public --source=. --remote=origin --push` (zamiast tworzenia ręcznego przez przeglądarkę)
- decyzja: `TreatWarningsAsErrors=true` od początku dla dyscypliny; jeśli przeszkadza w XAML, relaksujemy selektywnie per warning code w `Coffer.Desktop`
- decyzja: `Coffer.Desktop` w Sprint 0 dostaje minimalny Avalonia bootstrap (template `avalonia.app` bez modyfikacji) — żeby build weryfikował też pakiety Avalonia. UI z funkcjonalnością i DI bootstrap trafiają do Sprint 1
- decyzja: lokalna maszyna ma SDK 9.0.312 i 10.0.201 — pinujemy SDK 9 przez `global.json` w korzeniu (`"version": "9.0.0"`, `"rollForward": "latestFeature"`), żeby `dotnet` używał tego samego SDK co CI (workflow ma `dotnet-version: '9.0.x'`)
- decyzja: Avalonia templates wymagają one-time install `dotnet new install Avalonia.Templates` na maszynie — odnotowane w sprint-0.md jako krok 0.6, ale to operacja machine-level a nie repo-level
- decyzja: strategia "single-push" — wszystko konfigurujemy lokalnie, walidujemy `dotnet build/test/format` zielono, dopiero potem `gh repo create --push`, żeby CI na publicznym repo był zielony od pierwszego commita
- decyzja: status Sprint 0 → "W toku" (plan finalny, zaczynamy realizację)
- krok 0.1 ukończony — `git init` z `main` jako domyślną gałęzią
- krok 0.2 ukończony — commit `ecc26db` `chore: initial commit with planning docs and ci skeleton` (30 plików, 13472 wstawienia)
- decyzja: `.claude/settings.local.json` dodane do `.gitignore` — to lokalne uprawnienia Claude Code per maszyna, nie commitowane; team-shared `settings.json` (jeśli powstanie) zostaje śledzony
- krok 0.3 ukończony — usunięto `Install MAUI workload` z `.github/workflows/build.yml`
- krok 0.4 ukończony — `global.json` pinuje SDK 9.0.0 z `rollForward: latestFeature`; `dotnet --version` na maszynie zmienia z 10.0.201 na 9.0.312
- krok 0.5 ukończony — `Directory.Build.props` dodany, single source of truth dla TargetFramework/Nullable/LangVersion/TreatWarningsAsErrors/ImplicitUsings/EnforceCodeStyleInBuild
- krok 0.6 ukończony — `dotnet new install Avalonia.Templates` — UWAGA: to operacja machine-level, nie repo-level; każda maszyna gdzie chce się tworzyć nowe projekty Avalonia musi to zrobić raz
- decyzja: Avalonia.Templates najnowsze (template `avalonia.app`) wygenerowały csproj z `TargetFramework=net10.0` i Avalonia 12.0.3 — niezgodne z naszym SDK 9 i z docs (Avalonia 11). Pinujemy `Avalonia.*` PackageReferences do `11.*` w Coffer.Desktop.csproj
- decyzja: wszystkie csprojy oczyszczone z `TargetFramework`/`Nullable`/`ImplicitUsings` — single source of truth w `Directory.Build.props`. csprojy projektów produkcyjnych dostają tylko `ProjectReference`-y, csprojy testowe dodatkowo `IsPackable=false` i package references xUnit/FluentAssertions/coverlet/Microsoft.NET.Test.Sdk
- problem: kolizja namespace `Coffer.Application` (project reference) vs `Avalonia.Application` (base class) w `Coffer.Desktop/App.axaml.cs` (CS0118) → rozwiązanie: fully qualified `public partial class App : Avalonia.Application`
- problem: Avalonia template generowała placeholdery `Class1.cs` w projektach classlib i `UnitTest1.cs` w testowych → rozwiązanie: usunięto Class1.cs (czyste, puste projekty), wymieniono UnitTest1.cs na SmokeTest.cs weryfikujący wire-up FluentAssertions
- decyzja: FluentAssertions pinowane do `6.12.*` — od wersji 8 license komercyjna, 6.x darmowa
- kroki 0.7-0.11 ukończone — `Coffer.sln`, 5 projektów produkcyjnych + 3 testowe, referencje zgodnie z plan, wszystko w sln
- krok 0.12 ukończony — `dotnet build Coffer.sln --configuration Release` zielono, 0 warnings, 0 errors, ~2.5s
- krok 0.13 ukończony — `dotnet test Coffer.sln --no-build` zielono, 3 testy (po jednym smoke teście w każdym projekcie), wszystkie pass
- problem: `Coffer.Infrastructure.Tests.csproj` mimo zgłoszonego "updated successfully" w batch Write zachował template content (możliwy bug narzędzia lub race condition) → rozwiązanie: ponowny Write naprawił, kolejne builds OK
- krok 0.14 ukończony — `dotnet format Coffer.sln` naprawił CRLF/BOM/imports w wygenerowanych przez template plikach Avalonia (App.axaml.cs, MainWindow.axaml.cs, Program.cs); `dotnet format --verify-no-changes --severity warn` przeszło zielono
- krok 0.15 ukończony — commit `076f530` `chore(build): bootstrap Coffer.sln with 5 projects and 3 test projects` (23 pliki, 439 wstawień)
- krok 0.16 ukończony — `gh repo create Coffer --public --source=. --remote=origin --push` utworzyło publiczne repo na https://github.com/bartoszclapinski/Coffer i wypchnęło branch `main`
- krok 0.17 ukończony — CI run `25750058425` zielony (`success`, 40s) na commicie `076f530`
- krok 0.18 ukończony — badge `[![build](...)]` dodany pod nagłówkiem README, redundantna linia "Build status: see badge at top" usunięta z sekcji Status
- krok 0.19 ukończony — commit `ac9d540` `docs: add CI badge to README` + push; CI run `25750226446` również zielony (`success`)
- obserwacja: CI annotations ostrzegają o deprecation `actions/checkout@v4`, `actions/setup-dotnet@v4`, `actions/upload-artifact@v4` (Node.js 20 deprecation; Node.js 24 default od 2026-06-02, Node.js 20 usuwane 2026-09-16). To warning, nie failure — TODO do osobnego sprintu (chore CI bump actions versions). Nie blokuje Sprintu 0.
- krok 0.20 — sprint zamknięty: wszystkie 20 kroków done, DoD spełnione (świeży `git clone` + `dotnet build` + `dotnet test` zielono lokalnie i w CI; badge zielony w README)
- sprint zamknięty 2026-05-12

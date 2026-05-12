# Sprint 0 — Repo + solution + CI zielono

**Faza:** 0 (Foundation)
**Status:** W toku
**Zależności:** brak

## Cel

Repo zainicjowane w git, na publicznym GitHubie, `Coffer.sln` z pięcioma projektami produkcyjnymi i trzema testowymi, `dotnet build` + `dotnet test` + `dotnet format --verify-no-changes` zielono lokalnie i w CI od pierwszego pusha.

## Strategia

Wszystkie kroki konfiguracji wykonujemy lokalnie, walidujemy `dotnet build/test/format` zielono, dopiero potem tworzymy publiczne repo i pushujemy — żeby CI był zielony od razu, bez "czerwonych" intermediate commitów na publicznym repo.

## Kroki

- [x] 0.1 `git init` (bez commitów)
- [x] 0.2 Pierwszy commit obecnej zawartości repo (`chore: initial commit with planning docs and ci skeleton`)
- [x] 0.3 Usunąć krok `Install MAUI workload` z [.github/workflows/build.yml](../../../.github/workflows/build.yml) — wróci gdy doczepimy `Coffer.Mobile` (Faza 5)
- [x] 0.4 Dodać `global.json` w korzeniu z `"version": "9.0.0"` i `"rollForward": "latestFeature"` — zapewnia powtarzalny SDK (lokalnie mamy też .NET 10, CI używa 9.0.x)
- [x] 0.5 Dodać `Directory.Build.props` w korzeniu ze wspólnymi propertiesami: `TargetFramework=net9.0`, `Nullable=enable`, `LangVersion=13`, `TreatWarningsAsErrors=true`, `ImplicitUsings=enable`
- [x] 0.6 Zainstalować templates Avalonia lokalnie: `dotnet new install Avalonia.Templates` (one-time per machine, odnotowane w `log.md` żeby kolejny LLM/dev wiedział)
- [x] 0.7 Stworzyć `Coffer.sln` w korzeniu repo
- [x] 0.8 Utworzyć 5 projektów w `src/`:
  - 0.8.a `Coffer.Core` — classlib
  - 0.8.b `Coffer.Shared` — classlib
  - 0.8.c `Coffer.Infrastructure` — classlib
  - 0.8.d `Coffer.Application` — classlib
  - 0.8.e `Coffer.Desktop` — Avalonia 11 app (template `avalonia.app`), template-default `App.axaml` + `MainWindow.axaml` (UI bez funkcjonalności)
- [x] 0.9 Utworzyć 3 projekty testowe w `tests/` (template `xunit`):
  - 0.9.a `Coffer.Core.Tests` → Coffer.Core
  - 0.9.b `Coffer.Infrastructure.Tests` → Coffer.Infrastructure
  - 0.9.c `Coffer.Application.Tests` → Coffer.Application
  - + dodać `FluentAssertions` jako PackageReference do każdego
- [x] 0.10 Referencje między projektami (zgodnie z [01-stack-and-projects.md](../../../docs/architecture/01-stack-and-projects.md)):
  - `Coffer.Application` → Core, Shared
  - `Coffer.Infrastructure` → Core, Shared
  - `Coffer.Desktop` → Application, Infrastructure
  - `Coffer.Core` → (nic)
  - `Coffer.Shared` → (nic)
- [x] 0.11 Wszystkie projekty (prod + testy) dodać do `Coffer.sln`
- [x] 0.12 `dotnet build` lokalnie zielono
- [x] 0.13 `dotnet test` lokalnie zielono (puste projekty testowe — OK, ale dodać po jednym smoke teście na każdy projekt żeby `dotnet test` miał co znaleźć)
- [x] 0.14 `dotnet format --verify-no-changes --severity warn` zielono
- [ ] 0.15 Drugi commit (`chore(build): bootstrap Coffer.sln with 5 projects and 3 test projects`)
- [ ] 0.16 `gh repo create Coffer --public --source=. --remote=origin --push` — tworzy publiczne repo i pushuje branch `main`
- [ ] 0.17 Weryfikacja: CI workflow na GitHubie kończy się zielono (lokalnie już sprawdzone, ale CI używa innego SDK + Linux — warto upewnić się)
- [ ] 0.18 Dodać badge CI do [README.md](../../../README.md) (zastąpić frazę "see badge at top of repo" linkiem do workflow)
- [ ] 0.19 Trzeci commit + push (`docs: add CI badge to README`)
- [ ] 0.20 Zaktualizować [index.md](../index.md) — status Sprint 0 na "Zamknięty", finalny wpis do [log.md](log.md)

## Definition of Done

Świeży `git clone` + `dotnet build` + `dotnet test` przechodzi zielono bez żadnych dodatkowych kroków. Aktualny CI workflow na GitHubie pokazuje zielony status. Badge w README renderuje się jako zielony.

## Dotykane pliki

**Nowe:**
- `global.json`
- `Directory.Build.props`
- `Coffer.sln`
- `src/Coffer.Core/Coffer.Core.csproj`
- `src/Coffer.Shared/Coffer.Shared.csproj`
- `src/Coffer.Infrastructure/Coffer.Infrastructure.csproj`
- `src/Coffer.Application/Coffer.Application.csproj`
- `src/Coffer.Desktop/Coffer.Desktop.csproj` + pliki Avalonia template
- `tests/Coffer.Core.Tests/Coffer.Core.Tests.csproj` + smoke test
- `tests/Coffer.Infrastructure.Tests/Coffer.Infrastructure.Tests.csproj` + smoke test
- `tests/Coffer.Application.Tests/Coffer.Application.Tests.csproj` + smoke test

**Modyfikowane:**
- `.github/workflows/build.yml` — usunięcie kroku `Install MAUI workload`
- `README.md` — badge CI
- `.ai/sprints/index.md` — status
- `.ai/sprints/sprint-0/log.md` — postęp + decyzje

## Otwarte pytania

(Zamknięte — patrz `log.md`.)

## Notatki

- Nazewnictwo plików/folderów w `src/` i `tests/` — PascalCase, English (zgodnie z [conventions.md](../../../docs/conventions.md))
- `.ai/` zostaje w repo (śledzone w git) — to dokumentacja procesu dla kolejnych LLM-ów
- Wersje pakietów testowych: xUnit i FluentAssertions najnowsze stable. `Bogus`/`FsCheck` dorzucamy dopiero gdy potrzebne (najwcześniej Sprint 3)
- Strategia "single-push z gotową konfiguracją" zamiast "iteracyjne pushe z czerwonymi pośrednimi" — wybrana dla czystej historii CI na publicznym repo

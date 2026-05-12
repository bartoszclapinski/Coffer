# Log sprintu 0

## 2026-05-12

- decyzja: repo nie istnieje na GitHubie — tworzymy nowe przez `gh repo create Coffer --public --source=. --remote=origin --push` (zamiast tworzenia ręcznego przez przeglądarkę)
- decyzja: `TreatWarningsAsErrors=true` od początku dla dyscypliny; jeśli przeszkadza w XAML, relaksujemy selektywnie per warning code w `Coffer.Desktop`
- decyzja: `Coffer.Desktop` w Sprint 0 dostaje minimalny Avalonia bootstrap (template `avalonia.app` bez modyfikacji) — żeby build weryfikował też pakiety Avalonia. UI z funkcjonalnością i DI bootstrap trafiają do Sprint 1
- decyzja: lokalna maszyna ma SDK 9.0.312 i 10.0.201 — pinujemy SDK 9 przez `global.json` w korzeniu (`"version": "9.0.0"`, `"rollForward": "latestFeature"`), żeby `dotnet` używał tego samego SDK co CI (workflow ma `dotnet-version: '9.0.x'`)
- decyzja: Avalonia templates wymagają one-time install `dotnet new install Avalonia.Templates` na maszynie — odnotowane w sprint-0.md jako krok 0.6, ale to operacja machine-level a nie repo-level
- decyzja: strategia "single-push" — wszystko konfigurujemy lokalnie, walidujemy `dotnet build/test/format` zielono, dopiero potem `gh repo create --push`, żeby CI na publicznym repo był zielony od pierwszego commita
- decyzja: status Sprint 0 → "W toku" (plan finalny, zaczynamy realizację)

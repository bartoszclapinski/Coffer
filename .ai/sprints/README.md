# Sprinty

Lekka organizacja pracy nad Cofferem. Każdy sprint = logiczny kawałek pracy kończący się commitem i działającym/testowalnym stanem. Sprinty nie mają sztywnego budżetu czasowego — kończą się gdy DoD jest spełnione.

Kolejny LLM (lub ten sam po przerwie) zaczyna od:

1. Przeczytać [CLAUDE.md](../../CLAUDE.md)
2. Przeczytać [index.md](index.md) — który sprint jest w toku
3. Przeczytać `sprint-N/sprint-N.md` (plan) i `sprint-N/log.md` (co już zrobione)
4. Kontynuować pracę zgodnie z planem

## Struktura sprintu

Każdy sprint to katalog `sprint-N/` z dwoma plikami:

- `sprint-N.md` — plan sprintu. W trakcie modyfikujemy tylko checkboxy i sekcję "Otwarte pytania" (zamknięte pytania przenosimy do `log.md` jako decyzje).
- `log.md` — append-only chronologiczny dziennik.

## Format `sprint-N.md`

```markdown
# Sprint N — <krótki tytuł>

**Faza:** <numer z docs/architecture/10-roadmap.md>
**Status:** Planowany | W toku | Zamknięty
**Zależności:** sprint-X, sprint-Y (lub: brak)

## Cel

Jedno zdanie — co po sprincie ma działać.

## Kroki

- [ ] N.1 ...
- [ ] N.2 ...

(Można dzielić na podkroki N.1.a, N.1.b.)

## Definition of Done

Konkretny test ręczny lub automatyczny do odhaczenia.

## Dotykane pliki

Lista plików/katalogów których spodziewamy się tknąć.

## Otwarte pytania

- pytanie 1
- pytanie 2
```

## Format `log.md`

Append-only, najnowsze na dole:

```markdown
# Log sprintu N

## YYYY-MM-DD

- `HH:MM` krok N.X ukończony — commit `abc1234` — krótka notatka
- `HH:MM` decyzja: <co> bo <dlaczego>
- `HH:MM` problem: <opis> → rozwiązanie: <co zrobiliśmy>
```

Hash commita opcjonalny ale przydatny dla nawigacji.

## Status sprintu

- **Planowany** — plan napisany, ani jeden krok jeszcze nie zaczęty
- **W toku** — przynajmniej jeden krok ukończony, nie wszystkie
- **Zamknięty** — wszystkie kroki done, DoD spełnione, ostatni wpis w logu to "sprint zamknięty"

## Aktualizacja `index.md`

Po każdej zmianie statusu sprintu — zaktualizuj [index.md](index.md). `index.md` to tylko tablica statusów; treści sprintów tam nie kopiujemy.

## Język

Logi i plany sprintów piszemy po polsku (dokumentacja procesu). Kod, komentarze w kodzie, dokumentacja architektoniczna w `docs/` — po angielsku, zgodnie z [docs/conventions.md](../../docs/conventions.md).

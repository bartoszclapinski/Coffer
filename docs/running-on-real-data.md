# Running Coffer on real data

How to go from "it builds" to "my real bank history lives in it" without losing data or leaking it. Written for the owner's own machine (Windows, primary target). Read it once end to end before the first real import.

## 1. Build a stable copy — don't run real data from the IDE

Real data should be opened only by a build made from **merged `main`**, never by a feature branch running from the IDE (a feature branch can carry an unfinished migration).

```powershell
git switch main; git pull
dotnet publish src/Coffer.Desktop -c Release -r win-x64 --self-contained `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -o C:\Apps\Coffer
```

- Publish **outside the repo** (here `C:\Apps\Coffer`). Pin `C:\Apps\Coffer\Coffer.Desktop.exe` to Start or the taskbar.
- Republish only from `main` after a PR is merged (each new version runs its migrations against your vault — after taking a pre-migration backup).
- Never set the `COFFER_STYLE_GALLERY`, `COFFER_OVERVIEW_PREVIEW` or `COFFER_TX_PREVIEW` environment variables on this machine: they open developer preview windows instead of the app.

## 2. Know where your data lives

| Build | Data folder |
|---|---|
| Release (the published copy) | `%LocalAppData%\Coffer\` |
| Debug (IDE / `dotnet run`) | `%LocalAppData%\Coffer.Dev\` |
| Either, with `COFFER_DATA_DIR` set | that folder |

Inside it: `coffer.db` (the SQLCipher-encrypted database), `dek.encrypted` (the data key, wrapped by your password and by your recovery seed), `backups\` (daily and manual snapshots, pre-migration backups), `logs\`, `secrets\` (API key, DPAPI-encrypted), `theme.json` / `language.json` (non-sensitive preferences).

**An existing development vault:** if `%LocalAppData%\Coffer\` already holds a vault you created while developing, either rename that folder to `Coffer.Dev` (it becomes the Debug builds' vault) or delete it — then let the Release build create a fresh one. Don't reuse a vault created before Sprint 25: its paper seed cannot unlock it.

## 3. First run — create the vault properly

1. Start the published app; the setup wizard runs.
2. Choose a strong master password (the strength meter must be happy).
3. **Write the full recovery seed on paper.** It is shown once and never stored. Keep it offline.
4. After setup, sanity-check encryption: the first bytes of `coffer.db` must **not** be the text `SQLite format 3`. (The app also refuses to open the vault if the SQLCipher engine is missing.) In Windows PowerShell 5.1:
   ```powershell
   [Text.Encoding]::ASCII.GetString((Get-Content "$env:LOCALAPPDATA\Coffer\coffer.db" -Encoding Byte -TotalCount 15))
   ```
   (In PowerShell 7 use `-AsByteStream` instead of `-Encoding Byte`.)
5. **Drill the seed once:** close and restart the app, use "Forgot password? → Restore from seed" with your paper, set a password, and confirm you get back in. Better to find a mis-copied word now.

## 4. Settings before the first import

- **Account holder name** (Settings → *AI fallback parsing* → *Account holder name*, then **Save** at the bottom): every form of your name that appears on statements, comma-separated — with a middle name, a double surname, an inflected form ("Jana Kowalskiego"). Matching already ignores case, Polish characters and word order. It is replaced with `[OWNER]` in everything sent to an AI provider; other people's names, addresses, phone numbers, PESEL, e-mails and account numbers are redacted automatically.
- **Monthly AI cap** (PLN). The app warns at 80% and pauses AI at 100%.
- **AI API key:** optional, and needed only for a role that runs on Claude. Without any AI, import, rules, dashboards and every deterministic feature still work; only AI categorization, chat and AI commentary are off. If you want to be cautious, run the first imports without a key and add it later — or use a local model (below), which needs no key at all.

### Using a local Ollama model

Coffer can run its AI on [Ollama](https://ollama.com) on this computer: nothing leaves the machine, nothing is billed (local calls count as 0 PLN against the cap), and prompts are still redacted exactly as for Claude.

1. Install Ollama and pull the models you want, e.g.:
   ```powershell
   ollama pull qwen3.5
   ollama pull qwen2.5:3b
   ```
2. Settings → *Ollama server*: leave the endpoint at `http://localhost:11434` (Ollama's default) and press **Test connection** — it shows the Ollama version, or why it cannot connect (is `ollama serve` running?). A successful test also loads the model list; **Refresh models** reloads it after an `ollama pull`.
3. Settings → *AI providers and models*: for each role pick **Ollama (local)** and a model, then **Save** at the bottom.
   - **Analysis & chat** needs a model with tool calling — chat answers by calling Coffer's data tools. Use a capable one such as `qwen3.5` (9.7B). A 3B model can do it but is weak at chat and reports; Settings warns when the chosen model does not report tool support.
   - **Categorization** works with a small model (`qwen2.5:3b`); try a larger one if the categories look off.
   - The roles are independent: e.g. categorization local, analysis on Claude.
4. Check it: import a statement and look at the categories, then ask the chat a question. If Ollama is down, AI features degrade (uncategorised transactions, an "AI unavailable" chat banner) — nothing crashes and nothing falls back to Claude.

Notes: local models are slower than Claude (seconds to tens of seconds per call, depending on your GPU/CPU). Coffer sends up to a 16K-token context; a very long statement for the AI fallback parser can exceed it, and the import then says so instead of silently dropping transactions. If you point the endpoint at another machine, Settings warns that data leaves this computer.

## 5. Backups — before you trust it

1. Settings → Backup & Recovery → **Backup now**. A new `coffer-YYYY-MM-DD-manual-HHMMSS.db` appears in `backups\` and the panel shows its age. If a backup ever fails, the panel shows a red banner — don't ignore it.
2. **Export full archive** to an external drive or another disk. The archive holds the encrypted database and the wrapped key — it's useless without your password or seed, and enough to rebuild the vault with either.
3. Daily snapshots are taken automatically (a few minutes after launch, once per day); the newest 7 are always kept. Check over the next days that new files appear.

**Restoring on a new machine (manual, until the in-app archive import ships):** install/publish the app, do *not* run setup; create `%LocalAppData%\Coffer\` and unzip the archive's `coffer.db` and `dek.encrypted` into it; start the app and log in with your password (or "Restore from seed").

## 6. Importing PKO statements

Export from PKO iPKO: account history → "Historia rachunku" → **CSV**. Import the file exactly as downloaded — opening and re-saving it in Excel can change its encoding and columns.

Keep statement files **outside the repository** (e.g. `Documents\Bank\`). The repo is public; `.gitignore` covers typical export names and `*.real.*`, but don't rely on it.

### Optional dry run (no vault involved)

From the repo, copy one export to `tests/.local-fixtures/` (gitignored) under the name the manual harness expects (`Zestawienie operacji za 01.01.2026 - 31.01.2026.csv`) and run:

```powershell
dotnet test tests/Coffer.Infrastructure.Tests --filter PkoHistoriaCsvRealStatementManualHarness --logger "console;verbosity=detailed"
```

It prints only counts, the period and warnings — enough to see the file parses before it goes into the vault. Delete the copy afterwards.

### Verify the first import

- Added equals the number of transactions in the CSV; nothing unexpectedly skipped.
- Import the **same file again** → it adds 0.
- Import an **overlapping** period → only the new days are added.
- Two genuinely identical payments on the same day (e.g. two coffees) both appear.
- Polish characters in descriptions and merchants look right (ą, ę, ł, ó, ś, ż, ź, ć, ń).
- Spot-check a card payment, a transfer, a BLIK payment and, if present, a foreign-currency row.
- Opening balance + the sum of amounts matches the last row's "Saldo po transakcji" (set the account's anchor balance in Settings so balances are real, not relative).

## 7. When something looks wrong

- Logs are in the data folder's `logs\` (no amounts, descriptions, names or secrets are logged).
- To return to a known-good state: Settings → Backup & Recovery → **Restore from snapshot…** (applied on next start; the current database is first copied to `backups\pre-restore\`).
- A bug in a parser or importer: reproduce it with a hand-anonymized copy (fake names, account numbers and amounts; keep it under the gitignored `tests/.local-fixtures/`) — never commit a real statement — then open an issue.

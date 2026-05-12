# 08 — Backup and Recovery

## Goal

Surviving any of:
- Disk failure (SSD dies, no warning)
- OS reinstall or new machine
- Forgotten master password
- Database corruption from a power loss
- Migration that goes sideways
- Accidental delete by the user

Without losing financial history.

## Three layers of backup

### Layer 1 — daily local snapshot

A hosted background service makes a copy of the encrypted database file once per day.

- **Location:** `%AppData%\Coffer\backups\` (desktop), `FileSystem.AppDataDirectory/backups/` (mobile, though mobile rarely originates new data)
- **Filename:** `coffer-{YYYY-MM-DD}.db`
- **Retention:** 30 days, rolling. Older files auto-deleted at the start of each new daily run.
- **Trigger:** runs ~5 minutes after app startup if no snapshot exists for today
- **Cost:** ~10 MB per snapshot × 30 = 300 MB. Negligible.

### Layer 2 — monthly backup to Google Drive

On the first day of each month the app uploads a full snapshot to Drive.

- **Location:** `Coffer/backups/monthly/coffer-{YYYY-MM}.db.enc`
- **Retention:** 12 monthly backups (12 months of history)
- **Note:** the file is already SQLCipher-encrypted; uploading as-is to Drive is safe. We do not double-encrypt.
- **Auth:** Google Drive API using the same OAuth scope as sync (`drive.file`)

### Layer 3 — manual archive export

A button in Settings labeled "Export full archive". Produces a `.zip` containing:

- Encrypted database file
- Encrypted DEK file (`dek.encrypted`)
- App settings (excluding cached cleartext credentials)
- All saved receipt images (already encrypted)
- Versions: app version, schema version, schema info history

User decides where it goes — saved as `Coffer-archive-{YYYY-MM-DD}.zip`. They might keep it on a USB drive in a safe.

## Pre-migration backup — separate, mandatory

Before any database migration, an additional snapshot is created:

- **Location:** `%AppData%\Coffer\backups\migrations\pre-{schema-version}.db`
- **Retention:** 90 days
- **Always created**, regardless of daily/monthly cycle
- **Purpose:** an explicit "rollback point" tied to schema versions

If a migration corrupts data, the user has a guaranteed clean rollback.

## What's in a backup

A backup is more than the database. The database is encrypted; without the DEK it's noise.

The DEK itself is stored encrypted with a key derived from either:
1. The master password (Argon2id-derived), or
2. The BIP39 seed (PBKDF2-derived)

So a complete recovery package contains:
- Encrypted database (`coffer.db`)
- Encrypted DEK (`dek.encrypted`) — only useful with master password OR BIP39 seed
- Schema version metadata (`schema-info.json`)
- App version metadata (`version.json`)

The user never has to backup the master password separately — they should remember it. The seed they wrote on paper is the disaster recovery channel.

## Restore flow

```
User has lost data → opens app
                     ↓
            Detects fresh install (no DB)
                     ↓
              Setup wizard offers:
              ┌──────────────────────┐
              │ 1. Start fresh        │
              │ 2. Restore from backup│
              │ 3. Restore from seed  │
              └──────────────────────┘
                     ↓
            "Restore from backup" path:
              ┌──────────────────────┐
              │ User picks file:     │
              │ - daily snapshot     │
              │ - monthly Drive bkp  │
              │ - manual archive ZIP │
              └──────────────────────┘
                     ↓
              Verify file integrity
              (parse headers, schema)
                     ↓
              User enters master password
                     ↓
              Decrypt DEK with derived key
                     ↓
              Open DB with DEK, verify
                     ↓
              Migrate to current schema if needed
              (with another pre-migration snapshot)
                     ↓
                 Restart app
```

## Restore from BIP39 seed

If master password is forgotten:

```
User opens app → Setup wizard → "Restore from seed"
            ↓
Enter 12 words (validated against BIP39 wordlist + checksum word)
            ↓
Derive recovery key (PBKDF2-HMAC-SHA512, 2048 iter, BIP39 standard)
            ↓
Decrypt DEK using recovery key
            ↓
Prompt user for new master password
            ↓
Re-encrypt DEK with new master password
            ↓
Continue with normal restore
```

## Auto-test restore

Once a month, the app runs a silent self-test:

1. Pick yesterday's daily snapshot
2. Open it in a temp directory with the in-memory DEK
3. Run a SELECT COUNT against each table
4. Verify counts match expected (no integrity errors)
5. Delete temp copy
6. If failed: log error, show in-app warning the next time the app opens

This catches silently corrupted backups before the user needs them.

## Google Drive integration specifics

### OAuth scope

`drive.file` only. Cannot read other Drive files. Cannot list user's other Drive content.

### Folder structure

```
Coffer/                          (created by app on first run)
├── backups/
│   └── monthly/
│       ├── coffer-2026-01.db.enc
│       ├── coffer-2026-02.db.enc
│       └── coffer-2026-03.db.enc
├── sync/
│   └── (see 06-sync-and-mobile.md)
└── receipts/
    └── (encrypted receipt images)
```

### Refresh token

Stored encrypted by `IKeyVault`. Never in plaintext on disk. If revoked from Google's side, app shows a settings prompt to re-authorize.

### Rate limits

Drive API has generous quotas for individual users. Monthly backup = one upload per month, well within free tier. Sync polling (every 60s) involves listing and small reads, also within tier.

### Resumable uploads

For DB files > 5 MB, use Drive's resumable upload. Avoids restarting if connection drops.

## When backup fails

| Failure | Handling |
|---|---|
| No internet for monthly Drive backup | Retry on next app open. After 3 missed monthly attempts, show user a warning. |
| Drive quota exceeded | Show user warning, don't auto-delete old monthly backups (user might be on a strict plan) |
| Local disk full | Refuse to write daily snapshot, show warning toast |
| Backup file write interrupted | Daily snapshot uses `coffer-{date}.db.tmp` first, atomic rename only on full write success |
| Decrypt fails on restore | Likely wrong password OR corrupted file. Distinguish in error message. |

## What the user sees in Settings

A "Backup & Recovery" panel:

```
Backup status:
  Last daily snapshot:    2026-05-09 (today)
  Last monthly backup:    2026-05-01  ✓ on Drive
  Last self-test restore: 2026-04-15  ✓ passed

Actions:
  [Backup now]              ← runs Drive upload immediately
  [Export full archive]     ← Layer 3 manual export
  [Restore from backup]     ← opens restore wizard
  [Test restore]            ← runs self-test on demand
  [Show recovery seed]      ← password-gated, shows BIP39 with screenshot blocked
```

## Privacy guarantees

- Daily backups are **local only**, never leave the device
- Monthly backups go to user's own Drive folder (`drive.file` scope, can't see other content)
- Manual archives go wherever the user picks
- All files are SQLCipher-encrypted
- No telemetry about backup status leaves the device

## Testing

- Unit tests for backup file naming and rotation
- Integration tests with a fake Drive backend
- Restoration tests: build a DB, snapshot, modify, restore from snapshot, verify modification is undone
- Disaster recovery drill (manual, documented): once a release, do a full reinstall and restore from a real backup to verify the user-facing flow works end-to-end

# 06 — Sync and Mobile

## Goal

Owner uses both desktop and mobile. Operations on either device propagate to the other within ~60 seconds. No data is held by a third party in plaintext.

## Topology

Two devices (desktop, mobile) sync through a shared Google Drive folder owned by the user. Each device has its own local SQLite + SQLCipher database. The Drive folder holds an event log; devices apply each other's events to converge.

```
[ DESKTOP ]                                [ MOBILE ]
local SQLite                              local SQLite
   ▲                                          ▲
   │ apply events                             │ apply events
   │                                          │
   └────── Google Drive folder ───────────────┘
              "Coffer/sync/events/"
              encrypted .json files
```

This is **not** a shared database. Each device is the source of truth for its own write-side history; the merge happens client-side using event sourcing.

## Why event sourcing, not "diff the db"

Naive sync ("push my db, pull theirs") fails because:
- Both devices may have made offline edits → no canonical state to compare
- No way to merge conflicting changes
- Whole-DB transfer is expensive and rate-limited by Drive

Event sourcing solves this:
- Each operation is an immutable event with a unique ID and timestamp
- Devices append events; never modify or delete past events
- Conflicts resolve deterministically by timestamp (last-write-wins per field)
- Initial sync = replay all events; incremental sync = replay events since last cursor

## Event model

```csharp
public class SyncEvent
{
    public Guid Id { get; set; }                        // unique event ID
    public Guid DeviceId { get; set; }                  // which device produced it
    public long LamportClock { get; set; }              // monotonic per device
    public DateTime UtcTimestamp { get; set; }
    public SyncEventKind Kind { get; set; }             // Create | Update | Delete | Tag
    public string EntityType { get; set; } = "";       // "Transaction" | "Category" | "Goal" | ...
    public Guid EntityId { get; set; }
    public string Payload { get; set; } = "";          // JSON of the entity (for Create/Update) or field diffs
    public bool Applied { get; set; }                   // for inbound events
}
```

Stored locally in `SyncEvents` table. Indexed on `(LamportClock, DeviceId)`.

### Local-first writes

When a use case modifies data, two things happen in the same DB transaction:
1. The entity is written to its primary table (e.g., `Transactions`)
2. A `SyncEvent` row is inserted

This guarantees we never have an inconsistency between local state and outbound events.

### Event payload format

For `Create`/`Update`, the payload is the full entity JSON. For `Update`, it includes only changed fields plus a version vector. For `Delete`, it's an empty payload with the entity ID.

```json
{
  "kind": "Update",
  "entityType": "Transaction",
  "entityId": "f4d1...",
  "fields": {
    "CategoryId": { "old": "...", "new": "..." }
  },
  "lamport": 12345,
  "device": "desktop-abc"
}
```

## Encryption of synced events

Each event file uploaded to Drive is encrypted with the same DEK that encrypts the local DB. AES-GCM with a random 96-bit IV per file.

```
Drive folder structure:
Coffer/
├── sync/
│   ├── events/
│   │   ├── desktop-abc/
│   │   │   ├── 0000000001.event.enc
│   │   │   ├── 0000000002.event.enc
│   │   │   └── ...
│   │   └── mobile-xyz/
│   │       ├── 0000000001.event.enc
│   │       └── ...
│   └── checkpoints/
│       ├── desktop-abc.cursor
│       └── mobile-xyz.cursor
└── receipts/
    └── {receipt-id}.enc
```

Drive sees only opaque blobs. The OAuth scope used is `drive.file` — the app sees only files it created.

## Sync worker

Background `BackgroundService` in both desktop and mobile.

```csharp
public class SyncWorker : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var interval = TimeSpan.FromSeconds(60);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await PushOutboundEventsAsync(ct);
                await PullInboundEventsAsync(ct);
                await ApplyPendingInboundAsync(ct);
            }
            catch (Exception ex) when (IsTransient(ex))
            {
                _logger.LogWarning(ex, "Sync transient failure, retrying");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Sync failed");
            }

            await Task.Delay(interval, ct);
        }
    }
}
```

### Push (outbound)

1. Read `SyncEvents` where `Applied = true` and `Pushed = false`
2. Encrypt each event's payload with DEK
3. Upload to Drive: `Coffer/sync/events/{thisDeviceId}/{lamportPadded}.event.enc`
4. Mark `Pushed = true` in local DB

### Pull (inbound)

1. List Drive `Coffer/sync/events/` subfolders (one per device, excluding self)
2. For each device, list files newer than local cursor (filename > last cursor)
3. Download new files, decrypt
4. Insert into local `SyncEvents` with `Applied = false`
5. Update local cursor in `SyncCheckpoints` table
6. Update Drive checkpoint file (so other devices know what we've consumed)

### Apply

1. Read `SyncEvents` where `Applied = false`, ordered by `(UtcTimestamp, LamportClock)`
2. For each event:
   - **Create:** insert entity, no-op if entity already exists
   - **Update:** apply field-level last-write-wins (compare `LamportClock` of update event vs entity's last-modified clock)
   - **Delete:** mark entity deleted (soft delete; see "Tombstones" below)
3. Mark event as `Applied = true`
4. Commit in a single DB transaction

## Conflict resolution

### Field-level last-write-wins

For an `Update`, each field has its own clock. When two devices update the same entity at different times, the later-clock value wins per field. This is finer-grained than entity-level LWW and avoids losing concurrent edits to different fields.

```csharp
public class FieldClockTracker
{
    public Dictionary<string, long> FieldClocks { get; set; } = [];
}

// On Apply:
foreach (var (field, value) in updateEvent.Fields)
{
    if (event.LamportClock > entity.FieldClocks.GetValueOrDefault(field))
    {
        SetField(entity, field, value);
        entity.FieldClocks[field] = event.LamportClock;
    }
    // else: incoming is older, ignore
}
```

This avoids a common bug: desktop and mobile both update transaction's category at different times — without field clocks, one device's change is lost. With them, the later one wins per field.

### Tombstones for deletes

A delete is itself an event with `Kind = Delete`. We don't physically remove the row; we set `IsDeleted = true` and write the deletion timestamp/clock. Future `Update` events with older clocks are then ignored.

Garbage collection: rows with `IsDeleted = true` for more than 90 days are physically removed during a compaction job.

## Initial setup on a new device

On first launch:
1. User enters master password (or restores from BIP39 seed)
2. App connects to Google Drive (re-auth OAuth scope)
3. App lists existing devices in `sync/events/`
4. App downloads ALL events from all other devices
5. App replays events in chronological order to build local state
6. App generates own `DeviceId` (random GUID) and starts producing events

For 5+ years of usage with ~100 events/month per device, this is ~6,000 events. Replay takes seconds.

## Polling vs push

Why not real-time push (Drive Push Notifications)?

- Setup overhead: requires webhook-capable URL, not viable for desktop/mobile apps directly
- Polling at 60s intervals is sufficient for this use case (financial data is not chat)
- Saves complexity for v1

If real-time becomes desirable later, switch to Drive's `changes.watch` API.

## Mobile-specific concerns

### Background sync on iOS

iOS aggressively suspends background tasks. Strategies:
- `BGAppRefreshTask` registered for periodic 30-min refreshes (best-effort)
- Sync also runs on app foreground
- Push notification when statement-related changes happen on desktop (so mobile pulls fresh data when user opens it)

### Battery on Android

Don't drain battery polling every 60s when on cellular. Mobile worker:
- Polls every 60s on Wi-Fi + charging
- Polls every 5 min on Wi-Fi alone
- Polls every 15 min on cellular
- Pauses when battery < 20% unless user explicitly triggers

### Network failures

- Sync must be resilient to dropped connections mid-upload
- Use resumable uploads via `Google.Apis.Drive.v3` resumable session
- For pulls, atomic file naming: rename `*.tmp` to `*.event.enc` only after full download + decrypt verification

## Storage size

Per event ≈ 1–4 KB encrypted. 5 years × 2 devices × 100 events/month ≈ 12,000 events ≈ 30 MB on Drive. Negligible for Drive's free tier.

Receipts dominate storage. Compressed JPEG ~200 KB encrypted; 100 receipts/month × 60 months = 6,000 receipts × 200 KB = 1.2 GB. Watch this number; consider receipt cleanup policies (archive originals to local-only after 2 years).

## Privacy guarantees

- Drive holds only opaque blobs; no plaintext
- OAuth scope `drive.file` — Google APIs cannot read user's other Drive files
- Refresh token encrypted at rest via `IKeyVault`
- DEK never sent over network — same DEK derived from master password / BIP39 seed on each device

If user revokes Google Drive access in their account settings, sync stops gracefully and shows a settings prompt.

## Schema migration interaction

When desktop ships a new schema version, mobile may still be on the old version. Strategies:

- Events are versioned (event has `SchemaVersion` field)
- Newer-version events on Drive are not applied by older clients; they back off and prompt user to update
- This forces coordinated updates but prevents data corruption

For a solo user with two of their own devices, it's acceptable to update both within a few days.

## Mobile companion scope

The mobile app is **not** a port of desktop. It's a focused tool for what makes sense on the go.

### Mobile DOES

- Show current balance and recent transactions
- Capture receipt photos and run vision OCR
- Add manual transactions (edge case: cash purchases)
- Receive push notifications for alerts
- Show goal progress
- Quick search of transactions

### Mobile DOES NOT

- Import PDF statements (that's a desktop workflow)
- Manage categories or rules (text-heavy editing)
- Run the AI chat (long conversations don't fit mobile UX)
- Build or edit financial goals (needs the simulator slider on a real screen)
- Show full analytics dashboard (too dense for small screens)

A simple home + receipts + alerts + goals overview = ~5 screens. Bottom-tab navigation. Native MAUI controls.

## Testing sync

- Unit tests for event apply logic with crafted event sequences
- Integration tests with two `CofferDbContext` instances and a fake Drive backend
- Property-based tests: generate random event interleavings, assert convergence (both DBs end up identical regardless of event order)

# Technical architecture

## Stack and boundaries

- **OS/UI:** Windows 11, WPF on .NET 10, MVVM (CommunityToolkit.Mvvm recommended).
- **Persistence:** SQLite via **Microsoft.Data.Sqlite** (chosen in Phase 0; not EF Core), schema migrations, transactions and a repository/service boundary. Database is single-user and local.
- **DI/logging:** Microsoft.Extensions.DependencyInjection, Microsoft.Extensions.Hosting if useful; structured local logging with sensitive values redacted.
- **Tests:** xUnit or NUnit; isolated in-memory/temp-file SQLite tests for persistence and domain rules.
- **Deployment:** self-contained win-x64 build; later MSIX or signed conventional installer if useful. Install/run without admin privileges where possible.

Do not introduce a browser-hosted web UI, cloud services, an Electron shell or microservices for v1.

## Proposed solution structure

```text
TaskPane.sln
src/
  TaskPane.App/                # WPF windows, views, resources, DI bootstrap
  TaskPane.Core/               # Entities, enums, rules, service interfaces
  TaskPane.Infrastructure/     # SQLite, backups, import/export, Windows adapters
  TaskPane.Application/        # Use cases, validation, transaction orchestration
  TaskPane.Branding/           # Optional later; v1 may keep settings in App

tests/
  TaskPane.Core.Tests/
  TaskPane.Infrastructure.Tests/
  TaskPane.Application.Tests/
  TaskPane.App.Tests/          # Optional UI-oriented tests later

docs/
.cursor/rules/
AGENTS.md
```

Keep references one-directional: `Core` depends on nothing; `Application` depends on Core; `Infrastructure` implements interfaces; `App` composes them. Exact boundaries may be simplified if tests remain easy.

## App lifetime and window behavior

- Single instance per Windows user session (mutex plus activation IPC if needed). Re-launch focuses overlay or manager, does not open a competing database writer.
- One long-lived app process and tray icon. Two windows: compact overlay (transparent/borderless) and conventional main manager.
- Use shared injected services and observable view models, not duplicate in-memory task lists.
- Overlay drag area must not swallow checkbox/timer clicks. Save window placement on move/resize with debounce; restore to a visible area if a monitor disappears. Handle mixed DPI and work-area boundaries.
- Explicit Quit in tray terminates. Closing/Windows-minimizing full manager should restore overlay, except OS shutdown or explicit Quit.
- Optional click-through behavior is **deferred until accessible escape/hotkey mechanics are tested**; never enable an inescapable overlay.

## Core application services

- `TaskService`: create/update/complete/reopen/soft-delete/restore/reorder/move children.
- `TaskHierarchyValidator`: one-level nesting, prevention of cycles via parent changes.
- `DependencyService`: link/unlink, self-edge and graph cycle detection, ready/blocked queries.
- `FocusService`: Inbox/Today/Next/Someday, filter/sort/sleep/wake.
- `TimeTrackingService`: start/switch/stop, idle/lock handling, crash recovery, totals.
- `RecurrenceService`: rule validation, deterministic instance generation and deduplication.
- `ReminderService`: scheduling while tray app runs, quiet hours, notification adapter.
- `HistoryService`: atomic audit log and targeted undo actions.
- `BackupService`: consistent DB snapshots and retention, redirected Documents discovery/retries.
- `TransferService`: JSON round-trip import/export and readable Markdown export.

## Persistence strategy

Path: `Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)` + `TaskPane` + `tasks.db` (validate returned path; implemented by `AppPaths`). Use schema migrations and `PRAGMA foreign_keys=ON`, a reasonable busy timeout, and SQLite journaling mode suited to local storage (WAL is acceptable with correct checkpoint/backup practice). Never attach the live database to network Documents.

All domain edits that touch several records occur in **one DB transaction** (e.g. timer switch, delete parent subtree, recurrence creation and audit entry). Use timestamps in UTC; use user-local time to display and resolve recurrence schedules. Record timezone ID where needed for a recurrence schedule. Store original session start/end, not an accumulating timer tick.

## Backups and redirected Documents

- Query the Windows Documents known folder via appropriate platform API (e.g. `Environment.GetFolderPath(SpecialFolder.MyDocuments)` and validate folder redirection). Documents could resolve to UNC.
- Primary database remains local; back up by SQLite online backup API / consistent snapshot to a local temp file first, verify it, then publish safely to `Documents\TaskPane\Backups` when reachable.
- Never block task writes waiting for network storage. Use background bounded retries and last-success status.
- Maintain local fallback backups, retention and recovery procedure. If backup is unavailable, display a status indicator and retry; don't claim data was backed up until verified.
- Avoid arbitrary overwrites of user export files; sanitize filenames.
- Automatic backup is not multi-PC synchronization. **Do not read a backup on another PC and silently overwrite the current DB.**

## Timekeeping and power events

- Keep at most one open `TimeSession` per process / local database; enforce this transactionally. For `switch`, stop prior session and start new session in one transaction.
- Use `DateTimeOffset.UtcNow` for persisted time endpoints; use `Stopwatch` for a responsive in-session elapsed display if desired. On restart, detect open sessions and ask how to resolve; never simply count downtime.
- Subscribe to Windows session lock/unlock and power suspend/resume notifications through an adapter. On suspend/lock, end the session with reason and offer resumption as specified.
- Idle detection must allow the user to keep or discard idle time; decisions should be auditable.

## Recurrence considerations

- Store local calendar recurrence plus `TimeZoneId` (Windows timezone ID), not just a fixed UTC interval.
- Define a stable `OccurrenceKey` under a unique constraint to prevent duplicate generation after restart.
- Calendar arithmetic and DST transitions require explicit tests. Handle non-existent local times deterministically (e.g. roll forward to first valid time) and ambiguous times consistently. Record chosen policy in DECISIONS.md.
- Avoid infinite catch-up after an extended absence; cap generated instances and expose skipped/missed occurrences.
- Dependent task cloning must not create dangling or unexpected links. Define whether template dependencies are remapped to corresponding occurrences during Phase 5 design.

## Import/export contract

- JSON wrapper `schemaVersion`, `exportedAtUtc`, `application`, arrays of canonical entities; no opaque serialized ORM objects.
- Export should be consistent transactional snapshot; import uses preview and full validation of IDs, parent depth, dependency cycles, foreign references, time ranges, recurrence rules and duplicates.
- Default import **merge by ID only with explicit conflict behavior** or *import as new IDs* as a separate option. Always create safety backup before mutating an existing DB.
- Markdown is one-way human-readable export, not an exact restore format.

## Security and privacy

- No network access unless explicitly required for future features; no analytics.
- Treat task titles/notes as private user data: avoid default logging of content, hide note body in notifications, and never send to AI services.
- File permissions are Windows user permissions; encrypted database is **not** assumed. If encryption is required later, scope it separately (e.g. DPAPI for app secrets, not a false claim of DB encryption).
- Protect against malformed import files, oversized input, recursive references and CSV/Markdown injection-like surprise output where relevant.

## Performance and accessibility

- Async I/O where relevant; no network file operations on UI thread.
- Debounced filtering and list virtualization when counts demand it.
- Keyboard equivalents for mouse controls, focus management when expanding/collapsing, high-DPI scaling and screen reader names.

## Candidate libraries (not mandatory)

CommunityToolkit.Mvvm; Microsoft.Data.Sqlite or EF Core Sqlite; Microsoft.Extensions.DependencyInjection; a Windows tray/notification adapter compatible with current Windows 11 desktop apps. Validate API/package support and licenses before selecting implementations.

# Decisions and open questions

Use this log to distinguish **approved product decisions** from implementation assumptions. Add date, reason and implications when changing anything material.

## Approved by user

| Decision | Status |
|---|---|
| Public app / repository name: **TaskPane** | Confirmed |
| Simple movable translucent task overlay, expanding into richer manager and returning back | Confirmed |
| Add/complete tasks and view/edit notes on demand | Confirmed |
| Single-click per-task timers and task switching | Confirmed |
| Optional deadlines; no automatic low ranking for undated tasks | Confirmed |
| Task sleeping/snoozing | Confirmed |
| Subtasks limited to **one level** | Confirmed |
| Task links/dependencies | Requested |
| Quick-capture inbox via global hotkey | Confirmed |
| Recurring tasks | Confirmed |
| Today / Next / Someday | Confirmed |
| Tags and optional projects | Confirmed |
| Undo and activity history | Confirmed |
| Lightweight configurable reminders | Confirmed |
| **Soft delete** | Confirmed |
| **Data import/export** | Confirmed |
| Windows Documents (redirected) for user data/backup/export; ability to work offline | Requested |
| Multi-PC synchronization | **Not important yet; deferred** |

## Implementation decisions (Phase 0–1)

| Decision | Status | Notes |
|---|---|---|
| SQLite access via **Microsoft.Data.Sqlite** (not EF Core) | Chosen | Lightweight parameterized SQL, explicit migrations, easy temp-DB tests |
| Live DB under `%LOCALAPPDATA%\TaskPane\tasks.db` | Chosen | Never on Documents/network paths |
| Schema versioning via `schema_version` + numbered migrations | Chosen | Migration 001 creates Tasks + Settings |
| Single-instance via named mutex + named pipe activation | Chosen | Second launch activates existing instance |
| Tray Quit is the only normal process exit | Chosen | Manager close/minimize returns to overlay |

## Proposed defaults (reasonable v1 assumptions, can be refined)

- WPF/C#/.NET 10/MVVM, SQLite local live DB + backup to known Documents folder.
- Default view: compact always-on-top overlay; manual order; open tasks only; blocked visually marked, not hidden.
- Task creation defaults to Inbox/Normal/no deadline.
- Two window approach, not a single window that radically re-templates itself.
- Closing or minimizing manager returns to overlay; explicit tray Quit exits.
- Parent with unfinished children prompts before completion.
- Lock/suspend pauses timer; default idle threshold 10 minutes; offer resolution on return.
- JSON is lossless import/export format, Markdown is presentation-only export.
- Windows x64 self-contained build initially; later installer/signing.
- Recurrence generation must avoid duplication and infinite backlog.

## Open decisions to resolve when their phase begins

1. **Public packaging/brand polish:** icon and installer naming details (Phase 7).
2. **Recurrence policy:** generation horizon, whether catch-up instances accumulate, month-end rule, instance/template edit semantics and dependent occurrence remapping (Phase 5).
3. **Reminder presentation:** Windows toast integration compatibility vs in-app fallback, exact default quiet hours (Phase 5).
4. **Quick-capture hotkey:** default key combination after collision/UX testing (Phase 2).
5. **Import merge semantics:** import as new vs merge by ID, collisions and orphan references (Phase 6).
6. **Backup cadence and retention:** set conservative defaults after reliability tests (Phase 6).
7. **Branding:** icon, color scheme and public edition packaging (Phase 7).
8. **Soft-delete permanent purge:** explicit safety policy; no auto purge in early versions.
9. **Auto-start:** opt-in vs default, expose toggle and document behavior (Phase 7).
10. **Date/time semantics:** exact default timezone and preferred representation for date-only vs timed deadlines, including DST (Phase 2/5).

## Risks

- Redirected Documents may be UNC and unavailable during login, offline use or shutdown; never stall the UI.
- A network folder is not a safe primary SQLite store. Backup is not sync.
- WPF transparent windows have hit-testing, accessibility, blur and DPI edge cases; test real Windows devices.
- Native Windows notifications can impose packaging/identity details; abstract the adapter.
- Overly complex recurrence and dependency behavior can create surprises; validate before Phase 5.

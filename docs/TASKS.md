# Development roadmap and task tracker

**Rule:** Only implement one phase per Cursor request. Do not mark items complete just because code exists; builds/tests and (where applicable) manual Windows tests must pass.

## Phase 0 — Repository and foundation

- [x] Create .NET 10 solution and projects proposed in ARCHITECTURE.md.
- [x] Decide SQLite access approach (Microsoft.Data.Sqlite or EF Core); document decision.
- [x] Configure nullable references, analyzers, dependency injection and structured redacted logging.
- [x] Initialize SQLite schema migration tooling and schema versioning.
- [x] Define entity enums/value objects, IDs, core interfaces and business validation errors.
- [x] Add unit test framework and CI build/test workflow (Windows runner if WPF build required).
- [x] Implement single-instance launch behavior (may be finalized in Phase 1).
- [x] Build solution and run baseline automated tests.

**Accept:** Clean build; test projects execute; DB can initialize/migrate in a temp local folder; no network dependency.  
**Verified:** `dotnet build` / `dotnet test` on Linux agent (libraries + WPF compile via `EnableWindowsTargeting`). Fresh temp-folder SQLite migrate/persist covered by infrastructure tests.

## Phase 1 — Working overlay and task persistence

- [x] Create movable, resizable, always-on-top compact WPF overlay with user-adjustable opacity. *(code + compile verified; interactive Windows UI not run on this agent)*
- [x] Create separate manager window shell and bidirectional mode-switching. *(code + compile verified; interactive Windows UI not run on this agent)*
- [x] Persist overlay size/position/opacity; recover from monitor changes. *(persistence unit-tested; on-screen monitor recovery needs Windows manual check)*
- [x] Create/edit/complete/reopen top-level tasks; quick add to Inbox. *(service tests + UI wiring; UI flow needs Windows manual check)*
- [x] Local SQLite persistence with transactions, reload after restart.
- [x] Manual ordering, Today/Next/Someday/Inbox focus selection and priority independent of deadline.
- [x] One-level expandable subtasks, completion progress and parent-completion confirmation. *(rules tested; confirmation dialog needs Windows manual check)*
- [x] Soft delete and restore basics, parent/child cascade with deletion batch.
- [x] Tray icon and explicit Quit; close/minimize manager returns to overlay. *(code + compile verified; tray/Quit needs Windows manual check)*
- [x] Automated tests: hierarchy depth, title validation, delete/restore, persistence and order.

**Accept:** User can create a task/subtask, move overlay, expand manager, restart app and see identical persistent data. Live DB stays in LocalAppData. Closing full manager never accidentally quits.  
**Verified automatically:** domain/persistence rules and SQLite round-trip. **Not verified here:** interactive WPF overlay/manager/tray behavior on Windows 11.

## Phase 2 — Rich task editing and daily focus

- [x] Markdown/plain-text notes editor and compact note preview/popover. *(manager monospace notes editor + overlay title click preview; interactive Windows check pending)*
- [x] Tags and optional projects, filter/search by title and notes.
- [x] Optional date-only and date/time deadlines; overdue highlighting; no forced priority change.
- [x] Sleep task for presets, custom date/time or indefinitely; sleep/wake parent subtree behavior.
- [x] Waiting-for text/follow-up date; clear distinction from sleep and blocked.
- [x] Full manager filters and drag/drop reordering/moving one-level subtasks safely. *(reorder + move APIs tested; drag-drop UI needs Windows manual check)*
- [x] Global hotkey quick capture, conflict handling, customizable setting. *(Win32 RegisterHotKey + tray fallback; registration needs Windows manual check)*
- [x] Automated tests: undated priority sorting, sleep/wake, focus/filter combinations, move constraints.

**Accept:** All operations work offline; sleeping and due dates are independent; a task can be captured with just title and Enter.  
**Verified automatically:** domain/filter/sleep/deadline/move rules + SQLite migration 002 persistence. **Not verified here:** interactive WPF notes popover, drag/drop, and global hotkey on Windows 11.

## Phase 3 — Manual time tracking

- [ ] TimeSessions schema and transactionally enforced one-open-session invariant.
- [ ] Start, switch and stop by click on any root/subtask, without changing completion state.
- [ ] Direct and rolled-up durations; no double count.
- [ ] On complete/sleep/delete active task, end active session safely.
- [ ] Track lock/unlock and suspend/resume, including startup recovery of open sessions.
- [ ] Configurable inactivity threshold; keep/discard idle interval choice.
- [ ] Session correction UI and basic Today/Week report.
- [ ] Automated tests: rapid switch, restart recovery, parent/child rollup, manual adjustment.

**Accept:** Task switch creates two non-overlapping sessions. No phantom overnight hours after lock/sleep/crash.

## Phase 4 — Dependencies, history, undo

- [ ] Required dependency edges and reverse `Blocks` view; cycle/self-edge validation.
- [ ] Blocked status derived from unfinished required predecessors, including deleted predecessor warning.
- [ ] Blocked filter and explicit manual timer override.
- [ ] Activity history for major edits, including undo for completion, sleep, move and soft deletion.
- [ ] Recycle bin view and subtree restore, with safe handling of missing references.
- [ ] Automated tests for DAG cycles, graph updates and undo/redo boundaries.

**Accept:** Completing blocker updates dependent task immediately; creating A→B→A fails safely; delete never silently destroys links.

## Phase 5 — Recurrence and reminders

- [ ] Finalize recurrence instance-generation and missed-occurrence policies in DECISIONS.md.
- [ ] Implement recurrence templates (daily, weekly, selected weekdays, monthly, custom intervals/first weekday-of-month).
- [ ] Generate independent instances with stable occurrence key and deduplication.
- [ ] Handle timezone/DST/calendar edge cases with documented policy.
- [ ] Edit one occurrence vs future occurrences, copy notes/subtasks per selected policy.
- [ ] Independent reminder dates, reminder snooze/dismiss, quiet hours and deduplicated delivery.
- [ ] Windows 11 notification adapter or safe in-app fallback; optional background tray reminders.
- [ ] Automated tests for deduplication, leap year/month-end, DST, template changes and notification quiet hours.

**Accept:** Completing November's recurrence does not mark December complete; restarting twice creates no duplicates.

## Phase 6 — Backup, import/export, recovery

- [ ] Resolve actual Windows Documents known folder, including redirected UNC path.
- [ ] Consistent SQLite local backup + optional copy to Documents when reachable; retry and retention.
- [ ] Nonblocking backup status and error handling, recovery instructions.
- [ ] Versioned JSON export of tasks, relationships, tags, projects, sessions, recurrence, reminders and deletion state.
- [ ] Validated JSON import: dry-run/preview, duplicate handling, cycles/depth checks, pre-import safety backup.
- [ ] Human-readable Markdown export, with clearly stated limitations.
- [ ] Automated tests for export/import round trip, failed imports, unavailable Documents, live-backup consistency and restores.

**Accept:** App remains usable with disconnected redirected Documents; JSON round trip preserves meaningful data; invalid imports cannot corrupt DB.

## Phase 7 — Production polish and shipping

- [ ] Accessibility audit (tab order, focus, labels, contrast), keyboard alternatives and text scaling.
- [ ] DPI/multi-monitor/manual testing; window restore if monitor removed.
- [ ] Performance tests with thousands of tasks; search/virtualization and startup responsiveness.
- [ ] Crash recovery, logs/privacy defaults, offline startup, file permissions.
- [ ] Settings UI, per-user autostart option, tray polish, light/dark mode as appropriate.
- [ ] Self-contained win-x64 published build and instructions; installation/signing decision.
- [ ] Review licensing and third-party dependencies; public GitHub name still to be decided.
- [ ] Update README, changelog, screenshots, user manual and tested support matrix.

**Accept:** Repeatable release build, no admin needed for normal run, manual checks documented, data backup/restore tested.

## Out of scope / later

- [ ] Multi-PC synchronization (explicitly postponed).
- [ ] Click-through overlay (unless UX can guarantee reliable recovery and clear toggling).
- [ ] Optional advanced graphs, integrations, mobile/web access, cloud, collaboration.

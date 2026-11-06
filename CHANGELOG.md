# Changelog

All notable TaskPane releases are recorded here.

## 0.7.5 — Assign project and block from task UI

- Overlay task overflow: Project submenu (assign / clear) and Depends on… predecessor picker.
- Manager: clearer project assign + clear (Save applies); project filter clear; predecessor picker excludes self.
- Project name shown in task meta when assigned. DigiFokus via `AppDisplayName` only.

## 0.7.4 — Overlay lists all non-slept tasks

- Compact overlay no longer filters by Inbox/Today/Next/Someday; it shows every awake (non-slept) task. Focus buckets remain manager-only.
- Waiting and blocked roots sort below ready work; sleeping stays in the footer sleeping panel.
- Sleep, Waiting, and Blocked stay distinct: sleep hides + can stop timers; waiting is a free-text annotation; blocked is dependency-derived. DigiFokus via `AppDisplayName` only.

## 0.7.3 — Remove overlay opacity controls

- Removed overlay opacity menu/slider, ViewModel state, and RootSurface brush-alpha path; chrome stays solid at design alpha.
- Legacy `opacity` inside `overlay.placement` JSON is ignored on load and dropped on the next save (no Settings-row delete / schema wipe). DigiFokus via `AppDisplayName` only.

## 0.7.2 — Compact overlay while timer runs

- While a time session is active and the overlay is unfocused, chrome collapses to a smaller always-on-top mini strip (active title + elapsed + pause) — not tray minimize.
- Activate / focus (or click the mini strip) expands back to the full compact overlay; stopping the timer expands even if unfocused.
- `SetWindowRgn` refreshed on mini ↔ full size changes; rounded corners, Topmost, and dark overlay design preserved. DigiFokus via `AppDisplayName` only.

## 0.7.1 — Reminder toast improvements

- Win11 toast delivery where supported (unpackaged self-contained WPF); tray + in-app Open/Snooze/Dismiss fallback.
- Click opens the related task; configurable snooze minutes; quiet hours preserved; delivery-key dedup across restarts.
- Notification text uses `AppDisplayName` + task title only (never notes). DigiFokus via AppDisplayName only.

## 0.7.1 — Phase 7 testing and stabilization

- Regression coverage for soft-delete ending descendant timers, idle keep&resume, orphan restore promotion, recurrence not copying dependencies, and import open-session / ImportAsNew ID remap.
- Expanded Windows 11 manual verification guidance in `SUPPORT.md` (data-safety for restore/import drills).

## 0.7.0 — Phase 7 production polish

- Accessibility: visible focus style, automation names, tab cycling, text scale setting (100–150%).
- Multi-monitor placement restore when a saved monitor is missing.
- App settings: opt-in per-user autostart, theme preference (system/light/dark), privacy-filtered file logs.
- Crash/unclean-exit marker with startup recovery notice; LocalAppData writability probe (no admin).
- Debounced manager search; automated performance coverage for thousands of tasks.
- Self-contained `win-x64` publish script and shipping docs (`THIRD_PARTY_NOTICES.md`, `SUPPORT.md`, `LICENSE`).

## 0.6.0 — Phase 6 backup and transfer

- Local + Documents SQLite backups, JSON import/export, Markdown export, recovery help.

## 0.5.0 — Phase 5 recurrence and reminders

- Recurrence templates/instances, quiet hours, notification adapter.

## 0.4.0 — Phase 4 dependencies, history, undo

- Required dependency DAG, recycle bin, activity history/undo.

## 0.3.0 — Phase 3 manual time tracking

- Single open session, idle/lock recovery, reports.

## 0.2.0 — Phase 2 rich editing and daily focus

- Notes, tags/projects, deadlines, sleep, hotkey capture.

## 0.1.0 — Phase 0–1 foundation

- Overlay/manager shell, SQLite persistence, soft delete, tray quit.

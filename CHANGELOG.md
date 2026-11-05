# Changelog

All notable TaskPane releases are recorded here.

## 0.7.1 — Reminder toast improvements

- Win11 toast delivery where supported (unpackaged self-contained WPF); tray + in-app Open/Snooze/Dismiss fallback.
- Click opens the related task; configurable snooze minutes; quiet hours preserved; delivery-key dedup across restarts.
- Notification text uses `AppDisplayName` + task title only (never notes). DigiFokus via AppDisplayName only.

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

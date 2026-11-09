# Support matrix and manual checks

## Tested support matrix

| Item | Status |
|---|---|
| OS | Windows 11 x64 (target). Linux agents build libraries/tests with `EnableWindowsTargeting`. |
| Runtime | .NET 10 self-contained `win-x64` publish (no machine-wide runtime install required). |
| Privileges | Normal user. No admin for run, LocalAppData DB, or HKCU autostart. |
| Live database | `%LOCALAPPDATA%\TaskPane\tasks.db` only — never Documents/UNC. |
| Documents backups | Optional; deferred when offline/unavailable. |
| Installer / signing | Not in v1. Folder publish only. |
| Telemetry / network | None by design. |

## Automated (run on every change)

```bash
dotnet test TaskPane.sln -c Release
```

## Manual Windows 11 checklist (Phase 7)

Mark each **Pass** only after executing on a real Windows 11 machine. Otherwise leave **Not tested**. Prefer a throwaway Windows profile or a renamed `%LOCALAPPDATA%\TaskPane` folder before restore/import drills — the live DB is only `%LOCALAPPDATA%\TaskPane\tasks.db`.

| Check | Result |
|---|---|
| Rounded overlay corners over contrasting wallpaper (no white triangles / DWM halo) | Not tested |
| Overlay drag/resize (edges + corners); DPI 100/125/150; multi-monitor restore + missing-monitor clamp | Not tested |
| Mini overlay: unfocused → mini strip (idle: brand + expand; active: title + elapsed + pause); focus/click/expand restores full; corners/Topmost OK | Not tested |
| Tasks/subtasks/sleep/notes/undo/recurrence/reminders end-to-end (overlay + manager) | Not tested |
| Time tracking: sleep stops descendant timers; lock pause/resume; idle discard vs keep&resume; crash recovery | Not tested |
| Persistence across tray quit/relaunch (tasks, sessions, placement) | Not tested |
| Backup now + restore + JSON/Markdown export/import (incl. ImportAsNew remap; open sessions → Recovered) | Not tested |
| Overlay ↔ manager parity; tray; theme; text scale; autostart HKCU | Not tested |
| Keyboard-only create/edit/navigate; focus rectangle visible | Not tested |
| Offline / Documents unavailable; app starts and edits tasks | Not tested |
| Publish folder runs without admin from a user-writable path | Not tested |
| Large list (~3k tasks) remains scrollable with virtualization | Not tested |
| Public default wordmark remains TaskPane (`AppDisplayName` override only if intentional) | Not tested |

## User manual (short)

1. Publish or copy the `win-x64` folder to a user-writable location and run `TaskPane.exe`.
2. Use the overlay for quick triage; open the manager for editing, reports, backup, and **App / accessibility** settings.
3. Quit only from the tray **Quit TaskPane** item (or overlay quit). Closing the manager returns to the overlay.
4. Data lives under `%LOCALAPPDATA%\TaskPane\`. Use Manager backup/export before moving PCs (no sync in v1).
5. Logs (if enabled) are under `%LOCALAPPDATA%\TaskPane\logs\` and must not contain task notes by default.

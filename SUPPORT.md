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

Mark each **Pass** only after executing on a real Windows 11 machine. Otherwise leave **Not tested**.

| Check | Result |
|---|---|
| Overlay drag across monitors; restart restores placement | Not tested |
| Unplug/disable monitor that held the overlay; window returns to a visible work area | Not tested |
| DPI 100% / 125% / 150% and mixed-DPI if available | Not tested |
| Keyboard-only create/edit/navigate; focus rectangle visible | Not tested |
| Text scale 125% and 150% via Manager → App settings | Not tested |
| Theme system/light/dark applies without restart | Not tested |
| Autostart opt-in appears in HKCU Run; disable removes it | Not tested |
| Kill process mid-session; next launch shows recovery notice; no phantom timer hours | Not tested |
| Offline / Documents unavailable; app starts and edits tasks | Not tested |
| Tray menu: overlay, manager/settings, quick capture, quit | Not tested |
| Publish folder runs without admin from a user-writable path | Not tested |
| Backup now + restore + JSON export/import still works after polish | Not tested |
| Large list (~3k tasks) remains scrollable with virtualization | Not tested |

## User manual (short)

1. Publish or copy the `win-x64` folder to a user-writable location and run `TaskPane.exe`.
2. Use the overlay for quick triage; open the manager for editing, reports, backup, and **App / accessibility** settings.
3. Quit only from the tray **Quit TaskPane** item (or overlay quit). Closing the manager returns to the overlay.
4. Data lives under `%LOCALAPPDATA%\TaskPane\`. Use Manager backup/export before moving PCs (no sync in v1).
5. Logs (if enabled) are under `%LOCALAPPDATA%\TaskPane\logs\` and must not contain task notes by default.

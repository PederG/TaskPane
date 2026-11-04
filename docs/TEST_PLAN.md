# Acceptance and regression test plan

## Automated business-rule matrix

| Area | Must verify |
|---|---|
| Task creation | Title required, notes optional, defaults Inbox/Normal/no deadline |
| Independent dimensions | Undated High priority can rank ahead of dated Low; manual order preserved |
| Subtasks | One child level accepted; grandchild rejected; parent progress derived accurately |
| Parent completion | Incomplete children never silently completed; cancel leaves all unchanged |
| Soft deletion | Parent and children marked together; restore preserves IDs and related data |
| Dependencies | Self-edge/cycle rejected, blocked changes after predecessor complete/reopen, deleted predecessor surfaced |
| Sleep | Individual sleep, parent hides descendants, scheduled wake, indefinite sleep, no priority/deadline mutations |
| Timer | Zero/one open session, switching atomic, no overlap, correct parent aggregate |
| Timer interruptions | Lock/suspend/idle/crash recovery; no offline elapsed auto-charge |
| Recurrence | Deterministic generation, repeated restart dedupe, month-end/leap-year/DST behavior |
| Reminders | Quiet hours, dedup delivery, snooze, deadline independence |
| Imports | Round-trip IDs/relationships, invalid depth/cycles rejected, duplicate strategy safe |
| Backups | Consistent backup, retention, local fallback, delayed Documents path, recover after corruption |
| Undo/history | Restore previous state without breaking references or orphaning subtasks |

## Manual Windows 11 cases

- [ ] Drag overlay on same screen and between monitors; restart; placement retained and recoverable.
- [ ] Work over a maximized application; overlay remains legible and actionable.
- [ ] Resize to minimum/maximum; adjust opacity; controls remain reachable.
- [ ] Test at 100%, 125%, 150% scaling; mixed-DPI monitor combination if available.
- [ ] Click task title, checkbox, chevron, timer independently; no accidental window drag.
- [ ] Switch compact → manager → compact; previous position and list state maintained.
- [ ] Minimize and close manager return to overlay; explicit Quit exits fully.
- [ ] Launch second instance; only one app data writer and timer manager.
- [ ] Hotkey opens quick capture over other apps; conflict/fallback behavior clear.
- [ ] Keyboard-only create/edit/task navigation and visible focus.
- [ ] Lock/unlock session and sleep/resume; timer behavior matches policy.
- [ ] Simulate redirected Documents missing, unavailable UNC or slow network; task updates never freeze.
- [ ] Export JSON, import on clean profile, verify tasks/subtasks/dependencies/time/history.
- [ ] Restore latest valid backup after deliberately corrupting disposable test database.
- [ ] Reminder quiet hours, dismiss/snooze and unavailable notification permission fallback.
- [ ] Large dataset (e.g. 3,000 tasks, 1,000 children, 5,000 sessions) remains usable.

## Quality gates

1. `dotnet build` clean for targeted projects and release configuration.
2. `dotnet test` passes; fail fast on new regression.
3. Migrations tested against both fresh DB and an older schema version.
4. No normal path writes the live DB into redirected Documents.
5. No task titles/notes in logs or external requests by default.
6. Manual Windows items are marked **Not tested** unless actually executed.
7. Every new feature must demonstrate behavior after app restart and, if applicable, offline state.

## Known failure/edge conditions

- UNC path inaccessible or redirected Documents points to a temporarily offline share.
- Windows user changes resolution, monitor order or DPI while overlay is open.
- Local database locked/corrupt/full disk; backups unavailable.
- Another process briefly opens an exported file during import.
- Daylight-saving transition, leap day and monthly recurrence on the 31st.
- Circular graph via indirect predecessor; deletion/reopen of prerequisite.
- App killed during timer switch, subtree delete or JSON import.

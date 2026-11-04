# UI/UX contract

## Mental model

The overlay answers: **"What can I work on now?"** The manager answers: **"How are my tasks organized, scheduled and progressing?"**

## Compact overlay sketch

```text
┌─ ⠿  TaskPane · Today ─────────────── +  ⤢ ┐
│ ▾ □ SCCM migration                ▶   ⋯ │
│      2/4 subtasks · 1h 42m             │
│   ✓ Prepare migration script           │
│   □ Verify pilot clients           ▶   │
│ ▸ □ NPS certificate problem        ▶   │
│      No deadline · blocked             │
│ ◷ 3 sleeping tasks                  ▸  │
│ + Quick add task…                       │
└────────────────────────────────────────┘
```

- Header drag region moves window; task rows don't start dragging accidentally.
- `+` quick add; `⤢` opens manager; `▶` starts/switches time; click running timer to stop.
- Parent chevron expands/collapses direct subtasks; state persists.
- Checkboxes complete/reopen tasks. More menu: edit, sleep, move focus, copy link, soft delete.
- Task title click opens popover/side detail, with notes and simple edits; complex editing opens manager.
- Show minimal metadata; hide empty details. No deadline is normal, not a negative state.
- Default ordering is manual. A high-priority undated task can remain at top.

## Full manager sketch

```text
┌─ TaskPane — Manager ─────────────── Return to overlay ┐
│ Inbox  Today  Next  Someday  Projects  Sleeping  History│
├─ Search / Filter ───────────────────────────────────────┤
│ Task tree / list          │ Selected task details       │
│ □ SCCM migration          │ Title / notes / priority     │
│   □ Verify pilot          │ Deadline / reminders         │
│ □ NPS certificates        │ Dependencies / recurrence    │
│                           │ Sessions / activity history  │
└─────────────────────────────────────────────────────────┘
```

- Task tree limited to roots + one child level. Keyboard and mouse reordering.
- Detail editor can edit notes, focus, project, tags, deadline, reminder, recurrence, waiting, sleep and dependencies.
- Dedicated history and recycle bin views; time reports; import/export; settings.
- Minimize/close returns to overlay; explicit tray Quit exits.

## Task interaction defaults

- Creating a task needs only title; assigned to Inbox, Normal priority, no deadline.
- Clicking a task's timer stops previous session and starts this task's timer. Clicking active timer stops it.
- Editing notes does **not** start a timer automatically.
- A parent with incomplete subtasks prompts before completion.
- Sleeping a task does not change its deadline/priority. Sleeping a parent hides descendants.
- Blocked tasks visible by default with lock icon, filterable; manually starting timer is allowed.
- Prominent overdue indicators but no forced priority promotion.
- Full manager can optionally show completed/deleted; overlay normally shows open non-sleeping tasks.

## Keyboard and mouse

- Configurable global hotkey for Quick Capture; provide fallback tray menu when unavailable.
- Quick capture: input focus, title + Enter creates in Inbox, Escape dismisses. No mandatory form.
- Escape closes transient popovers; Tab navigates actionable controls; Enter/Space activate focused controls.
- Drag via header only. Optional position lock. Overlay remains recoverable if moved off-monitor.
- Preserve usable scaling at 125%, 150% and mixed DPI setups; minimum control hit targets and readable opacity.

## Reminders, idle and safety

- Reminders: quiet hours, dismiss/snooze, no repeated alerts for same occurrence, note text hidden by default.
- Timer activity is distinct from whether selected task is expanded. Pause on workstation lock or sleep; ask about idle time.
- Soft delete via More menu with undo toast. Confirmation for deleting a parent with children; restore from Recycle Bin.
- Show last-successful backup and a subtle warning when redirected Documents is offline; never block list interaction.

## Open UI choices (not implementation blockers)

- Exact colors/brand icon and whether compact mode displays elapsed time on every row or only active task.
- Default quick-capture shortcut (suggest `Ctrl+Alt+Space`, after conflict checks; configurable).
- Click-through mode: optional later after proven keyboard escape mechanism.

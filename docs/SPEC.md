# Product requirements — TaskPane

**Version:** 0.1 specification (pre-implementation)  
**Platform:** Windows 11 desktop  
**Audience:** Individual users, for personal desktop use.  
**Primary goal:** Reduce cognitive load by keeping the next useful tasks visible without forcing the user to maintain a complex project-management interface.

## 1. Product principles

1. **Capture first; organize later.** A new task requires only a title.
2. **Overlay first.** Essential actions must work without opening the full manager.
3. **Dates are not priorities.** A task without a deadline can be first in the list.
4. **Local and resilient.** Fully usable offline; no account/login.
5. **Progressively reveal complexity.** Notes, recurrence, dependencies and history are available on demand.
6. **No destructive surprises.** Soft delete, undo and explain potentially cascading changes.

## 2. Windows and mode behavior

### Compact overlay (must-have)

- Frameless or minimally framed WPF window with a draggable header; movable across monitors, resizable with sensible minimum/maximum dimensions.
- Adjustable translucency. Keep text legible; avoid making essential text unreadable at low opacity. Save position, size, monitor and opacity per Windows profile.
- Optional always-on-top (enabled by default); optional lock-position toggle.
- Remains usable above maximized applications. Has a task list, task completion control, expand/collapse chevron for a parent, timer start/stop, quick add, and expand-to-manager action.
- Notes and detailed editing open on demand, not permanently filling the overlay.
- Minimal task rows: title; optional indicators for due/overdue, blocked/waiting, timer running, subtask progress, reminder or sleep.
- User-controlled filters for focus bucket, project/tag and blocked tasks; separate access to sleeping tasks.
- Scrollable task list, keyboard navigation, normal DPI/multi-monitor support.

### Full management window (must-have)

- Separate standard WPF window for search, task editing, notes, deadlines, priorities, tags, projects, reminders, recurrence, dependencies, sleeping, history, recycle bin, import/export, settings and time reporting.
- Expand action hides the overlay. Return/minimize/close (not Quit) restores the overlay to its previous geometry; explicitly quitting from tray ends the application.
- A system tray icon provides restore/expand, pause timer and quit; one-instance app behavior avoids duplicate competing local timers.
- Quick-capture global hotkey. Detect/report shortcut conflicts and allow reassignment. Press Enter to capture title into Inbox.

## 3. Task data and organization

- Task: ID, title (required), optional rich-enough plain text/Markdown notes, status, manual order, priority (High/Normal/Low; Normal default), focus bucket (Inbox/Today/Next/Someday), optional deadline, optional reminder(s), tags, optional project, created/updated/completed timestamps, soft-delete metadata.
- One level of subtasks: parent task -> direct children only. Subtasks may have their own deadline, priority, notes, tags, sleep, status, time sessions and dependencies. No grandchildren.
- Parent progress shows `completed/total` children; parent completion does not silently complete children. If incomplete children exist, ask to complete all / keep parent open / cancel; never orphan records.
- Manual drag-and-drop order by default. Optional sorts by deadline/priority/created date. Overdue is a visual signal, not forced reordering.
- Search across tasks, subtasks and notes. Filter by status, focus, tags, project, deadline, priority, sleeping, blocked, completed and deleted (as applicable).
- Tasks and subtasks can be re-ordered. Moving a subtask between parents is permitted if the one-level constraint remains satisfied. Promoting a subtask to root is permitted.

## 4. Focus and visibility

- Focus buckets are **Inbox, Today, Next, Someday**; these are not deadlines and never automatically demote undated tasks.
- Tasks can sleep until a date/time, until tomorrow, in one hour, next week or indefinitely. Sleeping removes them from the normal overlay but not search/manager. Wake restores normal visibility.
- Sleeping a parent hides its subtree in the compact overlay. Child sleep is independent. Sleep does not complete, delete or change priority/deadline. If sleeping a running task, close/pause that session.
- Waiting for external action is an optional explicit status/flag with free-text "waiting for" and optional follow-up/reminder date. Waiting differs from sleep and dependency blocked.
- Blocked means at least one required predecessor is unresolved. Blocked tasks are visibly marked; optionally filter them out. Blocking does not auto-adjust priority or deadline and does not permanently prevent manual time tracking.

## 5. Dependencies

- Required relationship: **Finish-to-start** (`Task B depends on completed Task A`). Optional nonblocking **related to** link if straightforward.
- Dependencies may link roots and subtasks across projects. Do not allow self-dependencies or cycles.
- Completing the last blocker automatically updates readiness. Reopening a predecessor marks dependent tasks blocked again.
- Deleting a predecessor does not delete dependencies silently: retain a recoverable reference and surface a warning in manager.
- Show `Depends on` and `Blocks` in full manager. The compact overlay only needs a blocked indicator and on-demand details.

## 6. Time tracking

- One active session at a time in the process; clicking Start on any root or subtask stops the old session and starts the new one atomically.
- Stop tracking without completing the task. Completing or sleeping an actively tracked task ends its session.
- Store actual start/end instants; do not increment durations solely in UI timers. Allow edit/add/delete of sessions with audit history. A task's reported direct total is sum of its own valid sessions; a parent's roll-up is parent direct time plus each child's direct time, with **no double-counting**.
- On PC lock/sleep, auto-pause tracking; on unlock/resume offer to resume. On prolonged inactivity (default proposed 10 min, configurable), prompt whether to count or exclude idle time.
- On crash/restart, reconcile dangling sessions conservatively: do not automatically charge the whole offline interval. Surface a recovery decision.
- Reports for Today, week, custom range and task/project, with local-time date boundaries.

## 7. Recurrence

- Recurrence applies to tasks via a template/rule: daily, weekdays, weekly, monthly, custom intervals and chosen weekdays; monthly options must support meaningful calendar behavior (e.g. first Monday).
- Each generated occurrence has its own ID, completion state, notes snapshot, subtasks snapshot, dependencies where applicable and time sessions. Completing one does not complete future occurrences.
- No duplicated instances on restart. Define a deterministic occurrence key (`templateId` + local scheduled date/time + sequence/key if needed).
- Default suggested policy: generate the next instance when the current occurrence is completed or when its scheduled start arrives; avoid creating unlimited backlogs from extended absence. Finalize policy before implementing Phase 5.
- Display the originating recurrence rule and allow edit *this occurrence only* vs *future occurrences*.
- Recurring items may be snoozed individually without changing their underlying recurrence rule.

## 8. Reminders and notifications

- Reminder is independent of deadline; allow date/time reminders and, later, relative reminders.
- Reminders should be discreet, dismissible/snoozable and honor configurable quiet hours. Never spam repeated missed notifications.
- Notify when the application is running; background behavior depends on tray app. No service/admin requirement.
- Do not show full private notes in notification text by default.
- Detect/report notification permission or capability problems without losing task data.

## 9. History, deletion and recovery

- Soft-delete tasks into a recycle bin. Restorable with ID, notes, links, subtasks and history intact. Parent deletion soft-deletes child subtree. Restore should avoid unexpected overwrites.
- Permanent delete only through clearly warned explicit action (can be deferred to later). Never routinely purge audit entries without policy.
- Undo recent changes where feasible, including completion, move, priority/focus, sleep and deletion. Activity timeline records meaningful mutations with timestamp and before/after values as needed.
- Completed tasks remain searchable and can be reopened. Completion is distinct from deletion.

## 10. Import/export, backup and storage

- Authoritative live SQLite DB in `%LOCALAPPDATA%\TaskPane\`, not in a redirected or network path.
- Backups and default export destination in the **actual Windows Documents known folder** under `TaskPane` (may be redirected). Do not assume `%USERPROFILE%\Documents` is valid.
- If Documents is unavailable, store changes locally and queue/retry backup; show nonblocking status. There is **no multi-PC sync in v1**.
- JSON export/import: schema-versioned, stable IDs, hierarchy, recurrence, dependencies, time sessions, notes, tags, projects and deletion flags. Validate, preview counts, handle collisions, and offer safe import modes without silent overwrites.
- Markdown export is human-readable and need not be lossless; JSON is the round-trip format. Never imply Markdown can fully restore timers/links.
- Consistent automatic backups via SQLite online backup or equivalent, with retention, on schedule and optionally at shutdown. Backup must work while DB is in use.

## 11. Security, reliability and performance

- No cloud calls, account, analytics, telemetry or automatic notes upload. Store logs locally with task content redacted by default.
- No elevated permissions during normal use. Validate input and imports. Use database migrations and safe error handling.
- Preserve data across crashes and power loss (transactional writes); no UI hangs when network Documents folder is slow or missing.
- Responsive at realistically large personal task counts (target example: thousands of tasks) and on multi-monitor mixed-DPI setups.
- User-visible feedback for failed backups, read-only storage, corrupted exports and unavailable reminders.

## 12. Not in v1

- Multi-PC sync, conflict merge, central server or web UI.
- Deep nested subtasks or hierarchy graph view.
- Team collaboration, task assignment, permissions or shared projects.
- AI summaries/automatic prioritization, app integrations or automatic time capture from foreground applications.

## 13. Definition of done

A feature is done only when business rules are tested, the WPF flow is usable by keyboard and mouse, data survives restart, errors are handled, the specification reflects actual behavior, and manual Windows checks are recorded where required.

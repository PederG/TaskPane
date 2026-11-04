# Data model and invariants

This is the conceptual persistence model; concrete SQL and migrations should be designed during Phase 0. Stable UUID identifiers make future sync possible without implementing it in v1.

## Core tables

### Tasks

- `Id` UUID PK; `ParentTaskId` nullable FK to Tasks; **at most one child level**.
- `Title` required; `Notes` nullable Markdown/plain text.
- `Priority` enum High/Normal/Low (Normal default); `FocusBucket` enum Inbox/Today/Next/Someday.
- `Status` enum Open/Completed (blocked, waiting, sleeping and deleted are tracked separately rather than conflated).
- `ManualSortKey` stable sortable value (choose a collision-safe ordering implementation).
- `DeadlineLocalDate` nullable; `DeadlineLocalTime` nullable; `DeadlineTimeZoneId` nullable. Date-only deadlines must remain date-only, never silently treated as midnight UTC.
- `SleepUntilUtc` nullable; `SleepIndefinitely` boolean; `WaitingFor` nullable; `WaitingFollowUpUtc` nullable.
- `ProjectId` nullable.
- `RecurrenceTemplateId` nullable; `OccurrenceKey` nullable (unique with template when present).
- `CreatedUtc`, `UpdatedUtc`, `CompletedUtc` nullable; `DeletedUtc` nullable; `DeletionBatchId` nullable (to restore a parent subtree together).
- `RowVersion` or monotonic `Revision` for optimistic local concurrency.

### Dependencies

- `Id`; `PredecessorTaskId`; `SuccessorTaskId`; `Kind` (Required or Related); `CreatedUtc`; optional soft-delete marker.
- Unique edge per pair/kind; no self-edge; required-edge DAG (cycle check before commit).
- Define how an edge to deleted predecessor is shown and repaired; no cascading hard-delete in normal use.

### TimeSessions

- `Id`; `TaskId`; `StartedUtc`; `EndedUtc` nullable; `EndReason` (Stopped/Switched/Completed/Slept/Locked/Suspended/Idle/Recovered/ManualEdit); `CreatedUtc`; `UpdatedUtc`; `IsDiscarded` optional.
- A zero/one-open-session invariant for local profile; nonnegative duration; no double counting of open + closed sessions.
- Session edit history preserved in AuditEvents.

### Projects / Tags / TaskTags

- `Projects(Id, Name, ArchivedUtc?, Color?)`.
- `Tags(Id, Name, Color?)` unique case-insensitive name within app.
- `TaskTags(TaskId, TagId)` composite unique key.

### RecurrenceTemplates

- `Id`; `RuleJson` (validated, versioned recurring schedule model); `TimeZoneId`; `NextEvaluationUtc`; `IsEnabled`; `CreatedUtc`; `UpdatedUtc`.
- Add unique `(RecurrenceTemplateId, OccurrenceKey)` for Tasks; record whether template is attached to a root or independent recurring subtask. **Do not allow generation to violate one-level nesting.**
- Persist a snapshot/revision of task template content for a newly generated occurrence. Time sessions always belong to occurrences, not templates.

### Reminders

- `Id`; `TaskId`; `DueUtc`; `DeliveredUtc?`; `DismissedUtc?`; `SnoozedUntilUtc?`; `Kind`; `CreatedUtc`.
- Unique delivery/dedup key if reminders repeat across process restarts; quiet-hours policy stored in Settings.

### AuditEvents / Settings / Backups

- `AuditEvents(Id, EntityType, EntityId, Action, TimestampUtc, BeforeJson?, AfterJson?, CorrelationId)` with redaction controls; used for history/undo.
- `Settings(Key, JsonValue)` for user options, overlay placement, reminder quiet hours, backup retention, hotkeys and idle threshold.
- `BackupHistory(Id, CreatedUtc, LocalPath, DocumentsPath?, Result, ErrorCode?)` (do not log sensitive share details unnecessarily).

## Derived values (do not persist redundantly without a clear cache policy)

- `IsBlocked`: true when a required predecessor is not completed or was deleted/invalid.
- `IsVisibleInOverlay`: false if task deleted, completed (unless history toggle), sleeping, or parent sleeping; then apply chosen view/filter.
- `SubtaskProgress`: completed direct children / total direct children.
- `DirectTrackedSeconds`: sum of valid sessions belonging to task.
- `RolledUpTrackedSeconds`: direct parent seconds + sum of direct child seconds.
- `IsOverdue`: deadline passed in its calendar semantics and task still open.

## Required validation

1. Title cannot be empty or whitespace.
2. `ParentTaskId` cannot reference self, a missing/deleted disallowed parent, or any task already itself a child.
3. A parent with children cannot be moved under another parent without explicit safe flattening; default is reject.
4. A required dependency must not create a directed cycle, including across parents and projects.
5. Soft-delete parent and children with a single deletion batch; restore them consistently.
6. No two open time sessions in same local DB; switching sessions uses a transaction.
7. Import cannot introduce duplicate IDs, invalid date/times, negative session durations or hierarchy/dependency cycles.
8. Multiple occurrences cannot have same template + occurrence key.
9. Priorities, focus buckets, deadlines, sleep states and waiting statuses are independent concepts.
10. Completing a task with unfinished children requires an explicit decision; never silently auto-complete them.

## Suggested JSON export structure (illustrative, not a frozen API)

```json
{
  "schemaVersion": 1,
  "application": "TaskPane",
  "exportedAtUtc": "2026-11-04T10:00:00Z",
  "tasks": [],
  "dependencies": [],
  "timeSessions": [],
  "projects": [],
  "tags": [],
  "taskTags": [],
  "recurrenceTemplates": [],
  "reminders": [],
  "auditEvents": []
}
```

Do not blindly serialize all settings or machine-specific overlay coordinates into a portable task export; expose a separate settings export option if necessary.

# Agent instructions — TaskPane

You are implementing TaskPane, an offline-first Windows desktop task overlay. **Read README.md and all docs/*.md before substantive changes.** Follow user-approved requirements; do not invent a new product direction.

## Non-negotiable product rules

- The overlay is the **primary UX**: small, movable, resizable, translucent, always-on-top, quickly actionable. Advanced controls belong in the expanded manager.
- Opening the expanded manager hides the overlay; returning restores exact prior overlay geometry, monitor, opacity and topmost state where possible.
- Single-level subtasks only. A subtask cannot become a parent.
- Deadline is optional and does not set priority or position. Sorting defaults to manual order.
- Task sleep/snooze, waiting, blocking, completion and deletion are distinct states/concepts.
- Soft delete is the default; retain a restorable record. Do not hard-delete without explicit irreversible confirmation.
- Exactly one active timer per running app instance. Explicit start/switch/stop. No double-counting parent/subtask totals.
- Recurring tasks use a recurrence template and independent occurrences, with deterministic deduplication.
- v1 is single-PC local-first. No sync engine. Backup and export to redirected Documents when available; gracefully defer when offline.
- The local SQLite database is authoritative. **Never use a redirected/network folder as the live database location.**
- No admin rights required for normal operation. No telemetry or external data transfer.

## Engineering practices

- Prefer a small, testable architecture over speculative abstractions. C# / WPF / .NET 10 / MVVM / SQLite.
- Place business rules and services in projects that can be tested without starting WPF.
- Use migrations for schema changes; UTC instants in persistence; local timezone for presentation and recurrence. Be explicit about timezones and DST.
- Use parameterized SQL/EF Core safely; no untrusted dynamic SQL. Handle cancellation, validation and error paths.
- Back up SQLite using a consistent SQLite backup mechanism, not copying an open DB file.
- Use unique stable IDs, per-task audit entries and soft-delete tombstones. Use transactions for multi-record changes.
- Avoid treating timers as accuracy-critical background tick counters: persist session starts/ends and compute elapsed from clocks. Reconcile crashes/restarts.
- Use notifications sparingly and never disclose task notes in system notifications by default.
- Do not silently rewrite user content or resolve ambiguous recovery cases by dropping data.
- Accessibility: keyboard navigation, visible focus indicators, sensible contrast, scalable text, screen reader labels where practical.
- Use user-safe error messages; do not log task content or notes by default.

## Workflow

1. Work on **one phase** in docs/TASKS.md; explain any necessary deviation before implementing it.
2. Before edits: report files to change and the acceptance checks.
3. Implement incrementally, use compilation and automated tests where possible.
4. Add or update tests for business rules and regressions.
5. Record design changes in docs/DECISIONS.md; update docs when implementation changes contract.
6. After changes: report what was actually built, what was verified, and what still needs Windows manual testing.
7. Do not mark untested behavior as complete.

## Product naming

Public repository and product name: **TaskPane**. Keep storage namespace, assembly metadata and user-visible strings aligned with TaskPane unless a future branding decision is recorded in DECISIONS.md.

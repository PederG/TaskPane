# TaskPane

TaskPane is a Windows 11, offline-first personal task and focus manager. Its primary interaction is a **small, movable, resizable, translucent, always-on-top overlay**. A separate **full management window** provides detailed planning, notes, reporting and configuration. Both windows use the same local data.

**Status:** Phase 0–2 implemented (foundation, overlay/manager, rich editing, focus filters, sleep/deadlines/tags, quick capture). Later phases are planned in `docs/TASKS.md`.

## Start here

1. Read [Product specification](docs/SPEC.md).
2. Read [Architecture](docs/ARCHITECTURE.md) and [Data model](docs/DATA_MODEL.md).
3. Read [UX and interaction rules](docs/UX.md).
4. Follow [Implementation plan](docs/TASKS.md), **one phase at a time**.
5. Keep [Acceptance and test plan](docs/TEST_PLAN.md) current.
6. Record material design changes in [Decision log](docs/DECISIONS.md).

Cursor project guidance is in [`AGENTS.md`](AGENTS.md) and [`.cursor/rules/taskpane.mdc`](.cursor/rules/taskpane.mdc).

## Tech stack

- Windows 11 x64, C#, WPF, .NET 10, MVVM.
- SQLite as the *sole authoritative live database*, located in `%LOCALAPPDATA%\TaskPane\`.
- Offline-first, no cloud account, server, telemetry or network dependency.
- Portable, validated exports and consistent backups in the **resolved Windows Documents known folder**, which may be redirected to a network path (Phase 6). Work continues even when that path is unavailable.
- Unit/integration tests for business rules; manual Windows UI verification.

## Solution layout

```text
TaskPane.sln
src/
  TaskPane.App/             # WPF overlay, manager, tray, DI bootstrap
  TaskPane.Core/            # Entities, enums, validation, interfaces
  TaskPane.Application/     # Task, focus, organization, hotkey settings services
  TaskPane.Infrastructure/  # SQLite, migrations, local paths
tests/
  TaskPane.Core.Tests/
  TaskPane.Application.Tests/
  TaskPane.Infrastructure.Tests/
docs/
```

## Build and test

```bash
dotnet restore TaskPane.sln
dotnet build TaskPane.sln -c Release
dotnet test TaskPane.sln -c Release
```

The WPF app targets `net10.0-windows`. On non-Windows hosts, library and test projects still build/test with `EnableWindowsTargeting` set in `Directory.Build.props`; full WPF UI verification requires Windows 11.

## Explicitly out of scope for v1

- Multi-PC or simultaneous-user synchronization and conflict resolution.
- Server components, SaaS login, cloud services, mobile apps or web UI.
- Multi-level subtask nesting: exactly one child level is supported.
- Automatic AI processing of private task notes.
- Automatic project planning or a Gantt chart.

See [Decision log](docs/DECISIONS.md) for current assumptions to validate later.

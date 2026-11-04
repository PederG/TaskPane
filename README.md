# TaskPane

TaskPane is a Windows 11, offline-first personal task and focus manager. Its primary interaction is a **small, movable, resizable, translucent, always-on-top overlay**. A separate **full management window** provides detailed planning, notes, reporting and configuration. Both windows use the same local data.

**Status:** Phase 0–7 implemented (foundation through production polish/shipping). See [CHANGELOG.md](CHANGELOG.md) and [SUPPORT.md](SUPPORT.md).

## Start here

1. Read [Product specification](docs/SPEC.md).
2. Read [Architecture](docs/ARCHITECTURE.md) and [Data model](docs/DATA_MODEL.md).
3. Read [UX and interaction rules](docs/UX.md).
4. Follow [Implementation plan](docs/TASKS.md), **one phase at a time**.
5. Keep [Acceptance and test plan](docs/TEST_PLAN.md) current.
6. Record material design changes in [Decision log](docs/DECISIONS.md).

Cursor project guidance is in [`AGENTS.md`](AGENTS.md) and [`.cursor/rules/taskpane.mdc`](.cursor/rules/taskpane.mdc).

## Tech stack

- Windows 11 x64, C#, WPF (+ WPF-UI Fluent controls), .NET 10, MVVM.
- SQLite as the *sole authoritative live database*, located in `%LOCALAPPDATA%\TaskPane\`.
- Offline-first, no cloud account, server, telemetry or network dependency.
- Portable, validated exports and consistent backups in the **resolved Windows Documents known folder**, which may be redirected to a network path. Work continues even when that path is unavailable.
- Unit/integration tests for business rules; manual Windows UI verification ([SUPPORT.md](SUPPORT.md)).

## Solution layout

```text
TaskPane.sln
src/
  TaskPane.App/             # WPF overlay, manager, tray, DI bootstrap
  TaskPane.Core/            # Entities, enums, validation, interfaces
  TaskPane.Application/     # Task, focus, organization, settings services
  TaskPane.Infrastructure/  # SQLite, migrations, local paths, logging
tests/
  TaskPane.Core.Tests/
  TaskPane.Application.Tests/
  TaskPane.Infrastructure.Tests/
scripts/
  publish-win-x64.sh
docs/
```

## Build and test

```bash
dotnet restore TaskPane.sln
dotnet build TaskPane.sln -c Release
dotnet test TaskPane.sln -c Release
```

The WPF app targets `net10.0-windows`. On non-Windows hosts, library and test projects still build/test with `EnableWindowsTargeting` set in `Directory.Build.props`; full WPF UI verification requires Windows 11.

## Release publish (self-contained win-x64)

```bash
./scripts/publish-win-x64.sh
# or:
dotnet publish src/TaskPane.App/TaskPane.App.csproj -c Release -r win-x64 --self-contained true -o artifacts/publish/win-x64
```

Copy the output folder to a user-writable location and run `TaskPane.exe`. **No admin rights** are required for normal use. v1 does **not** ship an installer or code signing; see [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) and [LICENSE](LICENSE).

## Shipping notes (Phase 7)

- Opt-in per-user autostart (HKCU Run), theme preference, text scale, privacy-filtered file logs.
- Crash/unclean-exit recovery notice; LocalAppData writability probe.
- Multi-monitor placement restore when a saved monitor disappears.
- Public repository name / wordmark remains **TaskPane**. Workplace builds can show a different name without committing it here (see Branding below).

## Branding (`AppDisplayName`)

The overlay wordmark is driven by `AppSettings.AppDisplayName`, which defaults to the compile-time `AppBranding.DefaultAppDisplayName` (**TaskPane** in this repo).

| Layer | How | Notes |
| --- | --- | --- |
| Public default | Committed `AppDisplayName=TaskPane` | Do not change the public default to a private workplace name. |
| Build-time override | `dotnet build -p:AppDisplayName=YourName` or gitignored `Directory.Build.local.props` | Rewrites the generated `AppBranding` constant for that build. |
| Runtime override | Persist `appDisplayName` in local app settings (SQLite) | Survives restarts for that Windows profile; no secrets belong in the repo. |

Example workplace-only props file (gitignored as `Directory.Build.local.props`):

```xml
<Project>
  <PropertyGroup>
    <AppDisplayName>YourWorkplaceName</AppDisplayName>
  </PropertyGroup>
</Project>
```

## Explicitly out of scope for v1

- Multi-PC or simultaneous-user synchronization and conflict resolution.
- Server components, SaaS login, cloud services, mobile apps or web UI.
- Multi-level subtask nesting: exactly one child level is supported.
- Automatic AI processing of private task notes.
- Automatic project planning or a Gantt chart.
- Click-through overlay; signed MSIX installer (deferred).

See [Decision log](docs/DECISIONS.md) for current assumptions to validate later.

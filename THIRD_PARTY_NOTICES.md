# Third-party notices

TaskPane redistributes or links the following open-source components. Licenses were reviewed for Phase 7 shipping; all are permissive and compatible with the MIT-licensed TaskPane sources.

| Package | Version (pinned) | License |
|---|---|---|
| CommunityToolkit.Mvvm | 8.4.0 | MIT |
| Microsoft.Data.Sqlite (+ Core) | 10.0.14 | MIT |
| Microsoft.Extensions.* (DI, Hosting, Logging, Configuration, Options, …) | 10.0.14 | MIT |
| SQLitePCLRaw.bundle_e_sqlite3 (+ core/provider/lib) | 2.1.13 | Apache-2.0 |
| System.Diagnostics.EventLog | 10.0.14 | MIT |

## Test-only dependencies (not shipped in the app publish output)

| Package | License |
|---|---|
| xunit / xunit.runner.visualstudio | Apache-2.0 |
| Microsoft.NET.Test.Sdk / Microsoft.CodeCoverage / Microsoft.TestPlatform.* | MIT / proprietary test platform terms as published by Microsoft |
| coverlet.collector | MIT |
| Newtonsoft.Json (transitive via test SDK) | MIT |

## Decisions

- **App license:** MIT (`LICENSE`).
- **Public GitHub name:** repository currently uses `TaskPane`; final public branding remains open and may change.
- **Installer / code signing:** deferred. v1 ships as a self-contained `win-x64` folder publish (`scripts/publish-win-x64.sh`). No admin elevation is required for normal install-to-user-folder or run. Signing and MSIX remain optional later work.
- **Native SQLite:** provided via SQLitePCLRaw e_sqlite3 (Apache-2.0).

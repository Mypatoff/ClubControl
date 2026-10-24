# ClubControl

Computer club PC lock + billing system for Windows 11. Full spec: SPEC.md (read only the sections a task needs).

## Stack
C# .NET 8. WPF for Server (admin panel + hosted ASP.NET Core/SignalR) and Client. SQLite + EF Core. xUnit tests.

## Projects
- ClubControl.Shared: DTOs, enums, contracts, constants
- ClubControl.Server: server + admin panel (tray app)
- ClubControl.Client: lock screen + timer on gaming PCs
- ClubControl.Tests: billing/session tests

## Rules
- Work only on the current phase. Do not start the next phase.
- Keep code simple and readable; the owner is learning.
- Billing and session logic lives on the server and must have tests.
- Never disable the firewall or change Windows policies/registry shell.
- If the same error happens twice, stop and explain instead of retrying.
- At the end of a phase: build, run tests, give a short summary (max 10 lines) and how to test manually.

## Progress
(Update this list at the end of each phase.)
- [x] Phase 1: skeleton, database, server connection, discovery
- [x] Phase 2: client lock screen and timer
- [x] Phase 3: settings, tariffs, billing logic
- [x] Phase 4: admin panel UI
- [x] Phase 5: power control, reports, backup, languages, install scripts
- [x] Phase 6: manual/non-networked resources (offline PCs, billiards), free-form tariffs, context menus, split reports

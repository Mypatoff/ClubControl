# ClubControl

Computer club PC lock + billing system for Windows 11. Full spec: SPEC.md (read only the sections a task needs).

## Stack
C# .NET 8. WPF for Server (admin panel + hosted ASP.NET Core/SignalR) and Client. SQLite + EF Core. xUnit tests.

## Projects
- ClubControl.Shared: DTOs, enums, contracts, constants
- ClubControl.Server: server + admin panel (tray app)
- ClubControl.Client: lock screen + timer on gaming PCs
- ClubControl.Tests: billing maths and other cross-platform tests
- ClubControl.Server.Tests: SessionService tests on a real in-memory SQLite database (Windows only, references Server)

## Rules
- Work only on the current phase. Do not start the next phase.
- Keep code simple and readable; the owner is learning.
- Billing and session logic lives on the server and must have tests.
- Never disable the firewall or change Windows policies/registry shell.
- If the same error happens twice, stop and explain instead of retrying.
- At the end of a phase: build, run tests, give a short summary (max 10 lines) and how to test manually.
- EF model changes (new/changed columns, relationships) must ship with their migration in the same
  change - never push a schema-affecting edit without one (caused a prod crash loop once already).

## Dev environment
- ClubControl.Shared and ClubControl.Tests are plain .NET - they build and run anywhere (Linux, Windows, mac).
- ClubControl.Server and ClubControl.Client target net8.0-windows (UseWPF/UseWindowsForms) and can
  only be built, run, or EF-migrated **on Windows**. There is no cross-platform workload for this -
  WPF/WinForms require the actual Windows SDK installer, not an addable `dotnet workload`. On a
  non-Windows box, changes to these two projects can only be reviewed by reading, not compiled or
  run - always build + manually click-test them on Windows (or this repo's GH Actions) before
  trusting them, especially anything touching XAML, EF migrations, or the login/session flow.

## Known follow-ups
- `Pc.TariffId` has no EF foreign-key/migration tying it to `Tariff` (unlike every other relation in
  `ClubDbContext`). The app-level guard in `SettingsWindow.OnDeleteTariff` (blocks deleting a tariff
  that's currently assigned) closes the practical risk, but the schema itself still doesn't enforce
  it. Add the FK + migration once this can be built and the migration verified on Windows.
- No restore-from-backup flow exists (by design, matches SPEC - restore is a manual file-copy while
  the server is stopped), and session reports export to CSV only (`ReportsWindow.OnExport`), no print.

## Progress
(Update this list at the end of each phase.)
- [x] Phase 1: skeleton, database, server connection, discovery
- [x] Phase 2: client lock screen and timer
- [x] Phase 3: settings, tariffs, billing logic
- [x] Phase 4: admin panel UI
- [x] Phase 5: power control, reports, backup, languages, install scripts
- [x] Phase 6: manual/non-networked resources (offline PCs, billiards), free-form tariffs, context menus, split reports
- [x] Post-phase-6 fixes: tariff delete + tariff/resource-kind mismatch guard, report CSV export,
      duplicate-MainWindow-on-login bug (stray `StartupUri` racing the manual `MainWindow` creation),
      relaxed admin password policy (no more min-length/letter+digit rule), disabled PCs/billiards
      now actually disappear from the main panel (previously only excluded from bulk "all" actions)
- [x] Post-phase-6 change: automatic offline unlock replaced by the Ctrl+Shift+F12 staff-unlock password
      prompt on locked PCs (works with or without the server; new `Setting.ClientUnlockPasswordHash` +
      migration; `Setting.OfflineUnlockWaitSeconds` column is now unused but kept). Clicking empty space in
      the main panel now deselects. Debug-build client exit hook moved to Ctrl+Shift+F11.
- [x] Post-phase-6 fixes: client single-instance guard (`App.xaml.cs`), discovery retry loop now
      re-enumerates interfaces per try and survives exceptions instead of dying silently, and both
      app-wide unhandled-exception handlers plus that retry loop now log to
      `%ProgramData%\ClubControl\client-log.txt` (`ClientLog`) so unattended failures leave a trace.
- [x] Post-phase-6 feature: admin-triggered "Update client" (`CommandType.UpdateClient`,
      `PowerService.UpdateClientAsync`, `ClientUpdater` on the client). The server serves whatever
      build is zipped at `ClientUpdateFile.FilePath` over a plain HTTP GET
      (`NetworkConstants.ClientUpdateRoute`); a selected online PC downloads it, extracts to a temp
      folder, and hands the actual file swap + restart to a short PowerShell script (a running .exe
      can't overwrite its own files) before exiting. No automatic polling/version check by design -
      see README "Updating the client later".

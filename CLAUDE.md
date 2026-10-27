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
- [x] Phase 6: manual/non-networked resources (offline PCs, billiards - now generic named "Other" resources), free-form tariffs, context menus, split reports
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
      see README "Updating the client later". The command is signed per PC (`ClientUpdateAuth`, key =
      the PC's token hash) and the client checks signature + zip hash; the script (`ClientUpdateScript`
      in Shared, so it is unit-tested) backs up the install folder and rolls back if the new build
      writes no health marker (first successful heartbeat) in time. Clients report their build in
      `HeartbeatRequest.ClientVersion`; the server keeps it in memory only (no EF column) and shows it
      in the PC card tooltip.
- [x] Post-phase-6 change: main-panel card colours - red = offline, blue = connected and free,
      green = session running (prepay and postpay), orange = limit reached; no more greyed-out cards.
      Brush keys: `StateOfflineBrush` / `StateIdleBrush` / `StateRunningBrush` / `StateLimitBrush`.
- [x] Post-phase-6 change: Reports has a "Sessions" tab (`ReportsService.SessionLine`: which PC/other, start,
      end, duration, type, tariff, amount, items, note; also in the CSV), a Reports entry in the right-click
      menu, and the stop / payment windows show when each session started and how long it ran
      (`TimeText`, key `StopStartedFormat`).
- [x] Post-phase-6 fix: the "re-launch every 1 minute" scheduled-task trigger (survives Task Manager "End
      Task") now also reaches PCs installed before it existed: every client start runs `StartupTaskRepair`
      (script from `ClientTaskRepair` in Shared), which adds the repetition to the existing
      `ClubControl Client` task's logon trigger if missing. It lives in the new client build, not in
      `ClientUpdateScript`, because the update script is written by the OLD client being replaced.
      Not exercised against a real Task Scheduler task yet - check on one PC after the first update.
- [x] Post-phase-6 change: `ResourceKind.Billiard` is now `ResourceKind.Other` (same stored value 1) and
      every non-computer resource has an admin-chosen `Pc.Name` ("Table 3", "PS5 room"; new column +
      `AddPcName` migration). The name shows on the card, in dialogs (`PcRow.Label`/`Strings.ResourceLabel`)
      and in reports.
- [x] Post-phase-6 change: free sessions removed (no Free menu item/window, no "Free minutes" in reports;
      `SessionType.Free` and `Session.IsFree` stay so old rows still display). Starting a prepay/postpay
      session now takes an optional note (`Session.Note`, shown in the card tooltip and event log).
- [x] Post-phase-6 fix: startup upgrades the database through `DatabaseUpgrade` (full copy to
      `Backups/club_before-upgrade_*.db` before pending migrations, clear message for a pre-migrations
      club.db instead of a crash), covered by `DatabaseUpgradeTests`.
- [x] Post-phase-6 feature: items bought during a session (`SessionExtra` table + `AddSessionExtras`
      migration; `SessionService.AddExtraAsync` / `RemoveExtraAsync` / `GetExtrasAsync`, `AddItemWindow`).
      Listed with the total in the postpay payment window and the prepay stop window, shown in the card
      tooltip, kept apart from `Session.AmountUzs` (so tariff min/max and prepaid time are unaffected) and
      totalled separately in Reports ("Items sold").
- [x] Post-phase-6 feature: `ClubControl-Setup.exe` (Inno Setup, `installer/ClubControl.iss`, built by
      `installer/build-installer.ps1` and the Build workflow): one installer with a Server/Client choice,
      firewall rules / kiosk startup task / uninstall cleanup, app icon `assets/ClubControl.ico` (also the
      exe and tray icon). Both apps are now published self-contained (the installer bundles .NET), so the
      "Update client" zip must be self-contained too. Tested here (silent): server and client install,
      upgrade over an existing install and uninstall all clean; setup is about 100 MB.

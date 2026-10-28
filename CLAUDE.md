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
- [x] Post-phase-6 change: stopping a prepay session early now records only the money used
      (`BillingCalculator.PrepayIncomeOnEarlyStop` = paid - the refund shown in the stop window), so reports
      show the real income; a session that ran its full time stays fully paid. The stop window says
      "Refund" instead of "no refund" (`StopPrepayRefundNote`). This reverses the old "no refund" rule.
- [x] Post-phase-6 feature: "Update client" now reports its result. `ClientUpdateTracker` (Server, memory only,
      singleton) remembers which PCs were sent the command and judges from their heartbeats: new
      `ClientVersion` = updated, restart with the same version = unchanged, 5 minutes of nothing = no
      response (`OfflineMonitorService` checks). Shown as card state text (`PcRow.CardStateDisplay`,
      keys `UpdateCard*`) and as event-log lines (Warning for the two problem cases). No client change.
- [x] Post-phase-6 changes (admin panel): Reports now has `← Day | Week | Month →` instead of date pickers
      (arrows move one day and only work in Day mode); the Extend feature is removed completely (button, menu,
      `ExtendSessionWindow`, `SessionService.ExtendPrepay*`, `IsValidExtendIncrement` and their tests; old
      `EventType.SessionExtended` rows still display); window title bars follow the theme (`TitleBar`, DWM dark
      mode + Windows 11 caption colour, applied to every window on Loaded); the event log can be resized or hidden
      with a `GridSplitter` (height kept in `log-panel-height.txt`); the left toolbar wraps on narrow windows.
- [x] Post-phase-6 fix (robustness): the server no longer shuts down on an unexpected UI-thread error once it is up
      (`App` handlers: `ServerLog` to `server-log.txt`, same error at most once a minute, tray balloon at most every
      10 min; errors before `_startedUp` or on other threads still end it) and checks `club.db` at startup
      (`DatabaseHealth`: integrity check, restore the newest good backup only with the admin's OK, damaged file kept as
      `club_damaged_*.db`). The check opens the file normally, not read-only, so SQLite can finish a write that a power cut
      interrupted (the live DB runs in WAL mode). Checked against copies of the real club.db and newest backup.
- [x] Post-phase-6 change: the client update zip now lives next to the server program
      (`<server folder>\ClientUpdate\ClubControl.Client.zip`, `ClientUpdateFile.ServerFolderPath`), because that
      folder is writable by the server's Windows user (a file in ProgramData created by an elevated process may not
      be). A server in a protected folder falls back to the old `%ProgramData%` place (`LegacyPath`); when both
      exist the newer file is served. The installer (`installer/ClubControl.iss`) now proposes `C:\ClubControl`
      for the Server type via its own folder page (the built-in one is disabled, because the type is chosen
      after it) and gives `{app}\Server` Users-modify; the Client type keeps Program Files and is NOT made
      user-writable (it runs elevated at logon). An existing install keeps its folder. Tested silently with a
      renamed copy of the installer (server default folder + ACL, upgrade in place, client folder, uninstall);
      the interactive wizard pages were not clicked through.
- [x] Post-phase-6 feature: "Update server" button (`ServerUpdateWindow`, `ServerUpdater`, `ServerUpdateRelease` in
      Shared). Reads the rolling GitHub "latest" release (public repo, no token), downloads the server and
      client zips, verifies them against `SHA256SUMS.txt` (new Build workflow step), copies the client zip
      to `ClientUpdateFile.FilePath`, and swaps the server's own files with the same PowerShell script as
      the client update (`ClientUpdateScript.Build`: wait for exit, back up, robocopy, start, health marker,
      roll back). The server writes `server-healthy.flag` after the database upgrade, before the login
      prompt. Needs a user-writable install folder (not Program Files) and the admin password
      (`LoginMode.VerifyForUpdate`). Untested end to end: needs a release built with `SHA256SUMS.txt` and a
      second, newer build to update to - try "Update anyway" first on a spare copy.
- [x] Post-phase-6 fix: "Update client" on many PCs at once only updated one or two (all 30 PCs pulling the
      ~65 MB zip at the same moment overload the server's network and hit the client's download time
      limit). `PowerService.UpdateClientAsync` now sends the command one PC every `UpdateGap` (8 s); waiting
      PCs show "Waiting" (`ClientUpdateStatus.Waiting`), and `ClientUpdater` retries a failed download 3 times.
      The server side works with any client; the retry needs the new client build. Cause is inferred, not
      confirmed from a client-log.txt - check one failed PC's log if it still happens.
- [x] Post-phase-6 feature: staff unlock (Ctrl+Shift+F12) is now visible to the admin. The existing
      `ReportManualUnlock` hub call sets the PC to `PcState.ManualUnlocked` via
      `SessionService.MarkManualUnlockAsync` (only when it has no session): the card shows "Repair" (purple,
      `StateRepairBrush`) with 0 UZS and the top bar gets a Repair chip. Ends on a new session or when the PC
      reconnects (`BuildCurrentCommandAsync` locks it again). Server-only, current clients already report it.
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
- [x] Post-phase-6 feature: "client missing" status. A PC whose heartbeats stopped for 2 minutes is probed on the
      network (`PcPowerProbe`: ARP lookup via `SendARP`, then ping; MAC must match the one it reported). If it
      answers twice in a row while no client is connected, `ClientMissingTracker` (memory only, no migration)
      flags it: pink card (`StateNoClientBrush`) with "No client", a top-bar chip, a tooltip and one Warning log
      line. `ClientMissingMonitorService` runs the checks (own loop, so slow probes never delay
      `OfflineMonitorService`); skipped for disabled/manual PCs, PCs mid "Update client", and the first 2 minutes
      after a server start. Server-only, works with the clients already installed. The ARP call was run against
      this dev PC's gateway; it is not yet seen on the club's real network (check with a PC whose client folder
      is renamed, and with a normal shutdown - that must stay red).
- [x] Post-phase-6 change: starting a session on a switched-off PC now sends Wake-on-LAN by itself
      (`StartSessionWindow.OnOk` -> `PowerService.WakeForSessionAsync`, rule in `PowerService.NeedsWakeUp`: networked
      computer, not connected, MAC known). One event-log line per start. The session is billed from the moment
      it is started, also while the PC boots (unchanged behaviour). Needs Wake-on-LAN enabled in the PC's BIOS/NIC;
      not yet tried on a real switched-off club PC (the existing "Turn on" button uses the same packet).

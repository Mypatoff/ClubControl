# ClubControl

Computer club PC lock + billing system for Windows 11. See `SPEC.md` for the full specification
and `CLAUDE.md` for the project rules.

One admin laptop runs **ClubControl.Server** (server + admin panel). Every gaming PC runs
**ClubControl.Client**. Both must be on the same local network.

## Installing with ClubControl-Setup.exe (recommended)

One setup file installs either program. Download `ClubControl-Setup.exe` from the "latest" release
on GitHub (or build it, see below) and copy it to each PC. It needs nothing else installed - .NET is
bundled - and asks for Administrator rights once.

1. Run it and choose the setup type:
   - **Server** - on the admin laptop. Installs to `C:\ClubControl\Server` (the setup proposes it on the
     folder page; change it if you like), a folder your normal Windows user can write to, so that
     "Update server" in the admin panel can replace the program by itself. Adds the
     Start menu entry (and an optional desktop shortcut) named "ClubControl Server", opens the two
     firewall ports (TCP 5050, UDP 5051, **Private network profile only**) and offers to start it.
   - **Client** - on each gaming PC. Installs to `C:\Program Files\ClubControl\Client` - on purpose a
     folder only an administrator can change, because the client starts with administrator rights at
     logon (a folder anyone can write to would let a player swap its program). It asks which
     Windows account logs on automatically on that PC (the kiosk account from "Configure Windows
     auto-logon" below). It creates the "ClubControl Client" startup task for that account, the same
     one `install-client.ps1` makes, and starts it.
2. The per-PC Windows settings below (Wake-on-LAN, Fast Startup, auto-logon) are still done by hand.
3. To update, run a newer `ClubControl-Setup.exe` over the old install with the same type. It stops
   the running program first and keeps the database. An existing install stays in the folder it is in
   (a server that was installed to `C:\Program Files` by an older setup keeps working, but cannot use
   "Update server"; to move it, uninstall it and install the server again - the database stays).
   (Server and clients can also be updated from the admin panel, see below.)
4. Uninstall from Windows "Apps & features" (or the Start menu entry). It removes the program, the
   startup task and the firewall rules, and **leaves `%ProgramData%\ClubControl` (database, backups,
   logs) untouched**.

For unattended installs the setup accepts the usual Inno Setup switches plus two of its own:
`ClubControl-Setup.exe /VERYSILENT /TYPE=client /KioskUser="ClubPC1"` installs the client for that
auto-logon account without any questions (add `/NoStart=1` to not start it right away), and
`/TYPE=server` installs the server (to `C:\ClubControl`; `/DIR="D:\Club"` picks another folder).

To build the setup yourself you need [Inno Setup 6](https://jrsoftware.org/isdl.php) installed, then:

```
powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1
```

It publishes both programs self-contained and writes `dist\ClubControl-Setup.exe` (about 100 MB:
it contains the .NET runtime twice). The installer script is `installer/ClubControl.iss`; the
product name and version are at its top, the icon is `assets/ClubControl.ico`.

## Building without the setup

```
dotnet publish src/ClubControl.Server/ClubControl.Server.csproj -c Release -r win-x64 --self-contained true -o publish/server
dotnet publish src/ClubControl.Client/ClubControl.Client.csproj -c Release -r win-x64 --self-contained true -o publish/client
```

Copy `publish/server` to the admin laptop and `publish/client` to every gaming PC (e.g. with a
USB stick or a shared folder). `scripts/install-server.ps1` / `scripts/install-client.ps1` (and their
double-click launchers `install-server.bat` / `install-client.bat`) are not included in a local
publish output - copy them into the same folder as the matching `.exe` before running them. The
builds are self-contained on purpose: the setup installs them without .NET, so a client updated
with a framework-dependent build would stop starting.

## Updating the server ("Update server" in the admin panel)

Once a server runs a build that has this button, you never need to reinstall it by hand again:

1. Click **⬆ Update server** (top right). It asks GitHub for the newest build (the "latest" release that
   every push to `main` produces) and shows the installed and the newest version.
2. Click **Download and update** and enter the admin password. It downloads the server zip and the client
   zip, checks both against the published `SHA256SUMS.txt` (a download that does not match is thrown
   away), and **replaces the client update file** (`ClientUpdate\ClubControl.Client.zip` in the server's own
   folder, see "Updating the client later") with the new client zip. Then the server closes, swaps its own files and starts again by itself in
   about 30 seconds. Log in again.
3. To update the gaming PCs afterwards, select them and use "Update client" as described below - the zip is
   already in place.

Good to know:
- The gaming PCs keep running their sessions while the server restarts and reconnect by themselves.
- The old server files are backed up first. If the new server does not come up (it writes
  `%ProgramData%\ClubControl\server-healthy.flag` once it has started) within 90 seconds, the old build
  is put back. A database upgrade the new build already did is **not** undone - it makes its own backup
  in `Backups` first.
- It only works when the server runs from a folder your Windows user can write to: the setup's default
  `C:\ClubControl\Server`, or the unzipped `ClubControl-Server-win-x64` folder. A server in
  `C:\Program Files` (an older setup put it there) needs administrator rights: use the "Open download
  page" button and run `ClubControl-Setup.exe` again instead.
- The server needs internet access for this. Anyone who can push to the GitHub repository can ship a build
  this way, so keep the repository's access limited.
- The very first time you still have to put a build with this button on the server by hand.

## Updating the client later ("Update client" in the admin panel)

After the first install, you do not need to walk to every PC again for a client-side bug fix:

1. Publish the client as above (self-contained, so the zip is about 65 MB), zip the **contents** of `publish/client` (not the folder itself -
   `ClubControl.Client.exe` must be at the top level of the zip), and save it as
   `ClubControl.Client.zip`.
2. On the admin laptop, copy that zip to the `ClientUpdate` folder **inside the server's own folder**
   (for example `C:\ClubControl\Server\ClientUpdate\ClubControl.Client.zip`; create the folder if it
   is not there), overwriting whatever was there. "Update server" does this for you. (A server in a
   protected folder such as Program Files cannot write there and keeps using the older place,
   `%ProgramData%\ClubControl\ClientUpdate\ClubControl.Client.zip`; if both exist the newer file is used.)
3. In the admin panel, select the PC(s) to update (or none, to update every enabled PC) and click
   "Update client" (or right-click > "Update client"). Each selected online PC downloads the zip
   from the server, swaps its own files, and restarts itself - no manual copying, no reinstalling
   the Task Scheduler task. (Each start of the client also checks that task and, if it was made by an
   older install, adds the "re-launch every minute" repetition that brings back a client ended in
   Task Manager. It only touches the trigger of the task called `ClubControl Client`, and logs
   to `%ProgramData%\ClubControl\client-log.txt` when it changed something.)

Only online PCs receive the command, same as Shutdown/Restart. Roll out to one or two PCs first
and confirm they come back up correctly before updating the rest - there is no automatic version
check or staged rollout, "Update client" always sends whatever is currently zipped at that path.

Safety nets:
- **Signed:** the server signs the zip's hash for each PC with that PC's own token, and the PC
  refuses anything that doesn't match. A PC that was never configured/provisioned (no token yet)
  is skipped - configure it once in Settings and reinstall its client by hand that first time.
- **Rollback:** the old files are backed up first. If the new build doesn't get a heartbeat through
  to the server within 90 seconds, the PC restores the old build and starts it again.
- **Version:** hover over a PC card to see the build it runs (`v1.0.0+<git commit>`).
- **One PC at a time:** with several PCs selected the server sends the command to one PC every 8
  seconds (30 PCs take about 4 minutes), because every PC downloads the whole zip (about 65 MB) from the
  server and 30 downloads at once overload its network, so only a few finished. PCs still waiting show
  "⏳ Waiting". A failed download is retried up to 3 times by the PC (this part needs the new client build).
- **Result on the card and in the log:** right after you click "Update client" each PC's card says
  "⬇ Updating" (or "⏳ Waiting" until its turn). Then you see, on the card and as a line in the event log:
  - "✔ Updated" - the PC now runs a new version (`PC #5 updated: v… -> v…`; the card text fades after 2 minutes).
  - "⚠ Same version" - the PC restarted but still reports the same version: it already had that build, or
    the update was rolled back.
  - "✖ Update failed" - nothing happened within 5 minutes (no restart, no new version). The card keeps
    this until the PC is updated again or changes version. The reason is in
    `%ProgramData%\ClubControl\client-log.txt` on that PC.
  This is judged by the server from what each PC reports, so it works with any client build, and it is
  forgotten when the server restarts.

## One-time setup per PC

> Installed with `ClubControl-Setup.exe`? Then the `install-server` / `install-client` steps below (firewall rules, startup entry/task) are already done - do only the Windows settings (Wake-on-LAN, Fast Startup, auto-logon) and the first-run steps.

### Admin laptop (server)

1. Copy the published server files and `install-server.ps1` into one folder.
2. Double-click `install-server.bat` (or
   `powershell -ExecutionPolicy Bypass -File install-server.ps1`). It asks for Administrator
   rights once. It:
   - Adds two Windows Firewall inbound rules (TCP 5050, UDP 5051, **Private network profile
     only**) - it never disables the firewall or changes any other rule.
   - Registers `ClubControl.Server.exe` to start automatically when you log in (the app also does
     this itself on every run, so this step is a safety net, not strictly required).
3. Start `ClubControl.Server.exe` once. The first run asks you to create the admin password.
4. ClubControl runs in the system tray (like Telegram Desktop) - closing the window only hides it.
   Use the tray icon's "Exit" (password-protected) to actually quit.

### Every gaming PC (client)

Each of these is a one-time setting on that PC. Steps 1-3 make Wake-on-LAN and auto-logon work;
without them "Turn on" from the admin panel, or the client starting without someone at the
keyboard, will not work.

1. **Enable Wake-on-LAN in the BIOS/UEFI.** Look for a setting like "Wake on LAN",
   "Power On by PCIE/PCI", or "Resume by LAN" under Power Management and enable it. Exact wording
   depends on the motherboard.
2. **Enable "Wake on Magic Packet" on the network adapter.** In Device Manager, open the PC's
   network adapter properties:
   - *Power Management* tab: check "Allow this device to wake the computer".
   - *Advanced* tab: set "Wake on Magic Packet" to **Enabled** (and "Wake on Pattern Match" if
     present).
3. **Turn off Fast Startup.** Control Panel > Power Options > "Choose what the power buttons do" >
   "Change settings that are currently unavailable" > uncheck **Turn on fast startup**. Fast
   Startup (hybrid shutdown) leaves the network adapter in a state that often does not respond to
   Wake-on-LAN packets after a full shutdown.
4. **Configure Windows auto-logon.** The client must be running on the logon screen's other side
   (i.e. a real desktop session) for the lock screen and timer to show, including right after a
   remote "Turn on". Use Sysinternals Autologon
   (`https://learn.microsoft.com/sysinternals/downloads/autologon`): run it, enter the Windows
   username/password for that PC, click Enable. Use a dedicated low-privilege Windows account per
   PC, not an admin account.
5. Copy the published client files and `install-client.ps1` into one folder on the PC, then
   double-click `install-client.bat` while logged in as the auto-logon account from step 4 (or, from
   a command prompt, `install-client.bat -UserId <that account>`). It asks for Administrator
   rights once and creates a Task Scheduler task that starts `ClubControl.Client.exe` at logon for
   that user, with the highest available privileges (needed for the keyboard-blocking hook and
   shutdown/restart), set to restart itself automatically if it ever exits unexpectedly, with no
   time limit on how long it can keep running. **`-UserId` matters** - if you had to use a
   temporary admin account just to run this elevated, the task still needs to target the actual
   kiosk account from step 4, not whichever account happened to run this script.
6. Log off and back on (or reboot) to confirm the client starts automatically and finds the
   server. The first time, it appears as "New PC" in the admin panel - assign it a number and
   tariff under Settings > Computers & Other.

## PCs with no LAN connection, and named resources (billiard table, PS5 room, ...)

Not everything billable runs `ClubControl.Client`: a PC with no network cable/Wi-Fi, or a
billiard table or a PS5 room, has nothing to install software on. For those, use the **"Add Computer" /
"Add Named Resource"** buttons under Settings > Computers & Other instead of waiting for
auto-discovery:

- Pick a number, pick a tariff (create one first under Settings > Tariffs if you need a new price
  tier - tariffs are freely named, not limited to a fixed pair), and Save.
- These rows never show as online/offline (there is no client to report a heartbeat), and the
  Turn-on/Restart/Shut-down buttons do nothing for them (nothing to power-cycle) - that is expected,
  not a bug. Start/Stop/Move work exactly like any other row.
- Right-click a row (or a multi-selection) for the same actions as the toolbar, scoped to just
  that row/selection.

## Manual test checklist (Phase 5 and 6)

- **Client stays running**: start the client, confirm no window ever appears (not even in the
  taskbar), and that nothing you do on that PC can close/exit it (the lock screen itself blocks
  most of that anyway, but there's no window to accidentally close now either). Open Task Manager
  (via Ctrl+Alt+Del) and end `ClubControl.Client.exe` - the scheduled task should relaunch it
  within about a minute, and it should pick up exactly where it left off (re-discover the server,
  show the correct lock/session state).
- **Wake-on-LAN**: fully shut down a configured client PC, select it in the admin panel, click
  "Turn on" (with nothing selected, it asks to confirm turning on all enabled PCs). The PC should
  power on within a few seconds. If it does not, re-check BIOS/NIC/Fast Startup settings above and
  make sure the admin laptop and the PC are on the same subnet/VLAN.
- **Shutdown / Restart**: with a client online, select it (or several, or none for "all"), click
  "Shut down" or "Restart". If any target has an active session you are asked to confirm first.
- **Reports**: open Reports, switch between the Computers/Other buttons, try Daily/Weekly/
  Monthly and the manual date pickers after running a few test sessions (prepay, postpay) on
  both kinds. Check totals and the per-PC/per-tariff/per-type breakdowns match what you started,
  and that an "Other" session never shows up in the Computers report or vice versa.
- **Manual resources**: click "Add Computer" and "Add Named Resource" (it asks for a name, e.g. "Table 3"), confirm both appear in the main
  table with the right icon, can Start/Stop/Move a session, and never go "offline" or
  respond to Turn on/Restart/Shut down.
- **Tariffs**: Settings > Tariffs - add a new tariff, rename an existing one, save, and confirm
  the new/renamed name shows up immediately in the Add dialogs and the main table.
- **Daily backup**: check `%ProgramData%\ClubControl\Backups` - a `club_YYYY-MM-DD.db` file should
  appear within an hour of the server running on a new day. Older backups beyond the newest 30 are
  deleted automatically.
- **Damaged database**: every time the server starts it checks `club.db` before using it. If the file is
  damaged (a power cut, a failing disk, a virus), it says so and offers to restore the newest backup that
  passes the same check (Yes) or to exit (No). The damaged file is never deleted: it is kept next to the
  database as `club_damaged_<date>_<time>.db`. Anything entered after that backup is lost. If there is no
  good backup it changes nothing and tells you to copy `C:\ProgramData\ClubControl` somewhere safe.
- **Unexpected errors**: an error in the admin panel no longer closes the server (the gaming PCs depend on
  it). It is written to `%ProgramData%\ClubControl\server-log.txt` (the same error at most once a minute;
  the file is rotated at 2 MB) and the tray icon shows a short notice, at most once every 10 minutes. An
  error while the server is still starting up, or on a background thread, still ends it, with a message.
  If you see the notice often, send me `server-log.txt`.
- **Languages**: Settings > General > "Admin panel language" (Russian/Uzbek) and "Lock-screen
  language" (Uzbek/Russian/English). The lock screen/timer on the client update immediately; the
  admin panel asks you to restart ClubControl to fully switch (already-open windows keep their
  text until then).
- **Theme**: Settings > General > "Theme" (Light/Dark) - should apply immediately on Save, no
  restart needed, including the main table's row colors and grid lines.
- **Auto-shutdown**: Settings > General > "Shut down idle locked PCs after" (minutes, 0 =
  disabled; default 1 on a fresh install). Lock a PC (end its session, or just leave a configured
  PC unstarted) and wait - it should shut itself down on its own once the timer elapses. It must
  **not** shut down while "Limit reached" (waiting for payment) or mid-session. If you're
  upgrading an existing installation rather than starting fresh, this is **disabled (0)** by
  default on your existing settings row - set it explicitly in Settings if you want it.
- **Items**: start a session, select its card and click "Add item" (or right-click > Add item), enter
  a name and a price, repeat for a second item and Close. Hover the card to see the items total, then
  Stop: the payment window (postpay) lists the time charge, each item and the total; for prepay the
  stop window shows "Items to collect now". Reports show an "Items sold" box.
- **Bulk start**: select several PCs (or named resources) that share one tariff (Ctrl/Shift-click or
  drag), click Start - one dialog should start identical sessions on all
  of them. Try mixing PCs with different tariffs selected together - it should refuse with a clear
  message instead of guessing. Try it with one of the selected PCs already mid-session - it should
  start the rest and tell you which one it skipped.
- **Right-click menu**: right-click an actual row for the action menu; right-click empty space
  below the last row, or the column headers, and confirm nothing pops up.
- **Amount column**: start a prepay session and watch the Amount column over time - it should read
  "consumed / total UZS" and the consumed side should grow as time passes. Start a postpay session
  and confirm it reads "current cost / tariff max UZS".
- **Firewall rule on first run**: on a fresh admin laptop (rules not yet added), run
  `ClubControl.Server.exe` as Administrator once, or run `install-server.ps1` - check
  `netsh advfirewall firewall show rule name="ClubControl Server (TCP 5050)"` reports the rule.
- **PC auth token provisioning**: assign a brand-new "New PC" a number and tariff and Save; check
  `%ProgramData%\ClubControl\client-token.dat` appears on that PC within a few seconds (its next
  heartbeat). Restart the client - it should reconnect normally. Then manually delete/corrupt that
  file and restart the client again - the server's event log should show a rejected-heartbeat
  entry and the PC should show offline/stuck (not silently keep working), until you clear and
  reassign its Number in Settings to re-provision it.

## Database upgrades

The server now uses real EF Core migrations (`src/ClubControl.Server/Data/Migrations`). On every
start it applies any migration it hasn't applied yet - **your data survives future updates**;
you never need to delete `club.db` again after this point.

Before it applies a migration to a database that already has data, the server saves a full copy as
`%ProgramData%\ClubControl\Backups\club_before-upgrade_<date>_<time>.db` (kept until you delete it).
If an upgrade ever fails, the server shows the reason and does not start; restore that copy to go back.

**One-time exception:** if your `club.db` was created before this change (anything up through the
Phase 6 build that still used `EnsureCreatedAsync`), delete it once more
(`%ProgramData%\ClubControl\club.db`) before running this build - after that, upgrades are safe.

When a future change needs a schema update, it is done via the "Add EF Migration" GitHub Actions
workflow (Actions tab → run it with a migration name) rather than by hand, since generating a
migration requires actually building the WPF project.

## Security notes

- **Network traffic is plain HTTP, not TLS.** Kestrel listens on `http://0.0.0.0:5050`; SignalR
  traffic (including the one-time delivery of a PC's auth token - see below) is unencrypted on
  the LAN. This is an accepted trade-off for a single-club, physically-controlled private
  network with no existing certificate infrastructure to distribute to ~30 client PCs - adding
  real TLS would mean generating a certificate and getting every client to trust it, which is a
  deployment project of its own. If the club's network is ever not fully trusted (e.g. a shared
  building network, guest Wi-Fi on the same VLAN, anything beyond PCs you physically control),
  put the server behind a LAN-only reverse proxy/VPN or revisit this before relying on it.
- **Per-PC identity.** A gaming PC's `MachineGuid` alone is no longer enough to speak for it.
  The first time a PC heartbeats *after* you give it a number and tariff (Settings > Computers &
  Other), the server mints a random secret token for it, sends it once, and the client stores
  it locally (`%ProgramData%\ClubControl\client-token.dat`). Every heartbeat after that must
  include the matching token, or the server ignores it outright (logged as a rejected-heartbeat
  event) - so a second machine that merely learns or guesses a real PC's GUID can no longer take
  over its commands or overwrite its reported IP/MAC/hostname. A brand new, not-yet-configured PC
  is still trusted at face value the moment it first appears as "New PC" (same as before) - that
  bootstrap moment is unavoidable without a separate out-of-band provisioning step, and is no
  weaker than today's "recognise it by hostname/IP in the list" process.
- **Revoking/rotating a PC's token:** clear its **Number** in Settings > Computers & Other and
  Save. That wipes its stored token; the next heartbeat (from whatever machine next claims that
  GUID) re-provisions a fresh one the moment you reassign a number. Use this if a PC is replaced,
  reimaged, or you suspect its token leaked.
- **Admin panel:** the SignalR hub (`ClubHub`) exposes only `Heartbeat` to connected PCs - no
  session/billing/command RPCs are reachable from a terminal. The admin panel calls
  `SessionService` in-process, in the server's own process, never over the wire.
- **SignalR detailed errors** are only enabled when `ASPNETCORE_ENVIRONMENT=Development`; a
  normal install (no such variable set) never sends exception details/stack traces to clients.
  Unhandled hub exceptions are still captured server-side via ASP.NET Core's default logging.

## Notes and known limitations

- Reports' "income" reflects each session's stored amount - a postpay/free session still running
  during the report period contributes 0 until it ends, same as everywhere else in the app.
- A handful of internal validation error messages (e.g. "Payment must be between X and Y UZS")
  are still English-only; all screen labels, buttons, and the lock screen are translated.

# ClubControl

Computer club PC lock + billing system for Windows 11. See `SPEC.md` for the full specification
and `CLAUDE.md` for the project rules.

One admin laptop runs **ClubControl.Server** (server + admin panel). Every gaming PC runs
**ClubControl.Client**. Both must be on the same local network.

## Building

```
dotnet publish src/ClubControl.Server/ClubControl.Server.csproj -c Release -r win-x64 --self-contained false -o publish/server
dotnet publish src/ClubControl.Client/ClubControl.Client.csproj -c Release -r win-x64 --self-contained false -o publish/client
```

Copy `publish/server` to the admin laptop and `publish/client` to every gaming PC (e.g. with a
USB stick or a shared folder). `scripts/install-server.ps1` / `scripts/install-client.ps1` (and their
double-click launchers `install-server.bat` / `install-client.bat`) are not included in a local
publish output - copy them into the same folder as the matching `.exe` before running them.

## One-time setup per PC

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
   tariff under Settings > Computers & Billiards.

## PCs with no LAN connection, and billiard tables

Not everything billable runs `ClubControl.Client`: a PC with no network cable/Wi-Fi, or a
billiard table, has nothing to install software on. For those, use the **"Add Computer" /
"Add Billiard"** buttons under Settings > Computers & Billiards instead of waiting for
auto-discovery:

- Pick a number, pick a tariff (create one first under Settings > Tariffs if you need a new price
  tier - tariffs are freely named, not limited to a fixed pair), and Save.
- These rows never show as online/offline (there is no client to report a heartbeat), and the
  Turn-on/Restart/Shut-down buttons do nothing for them (nothing to power-cycle) - that is expected,
  not a bug. Start/Extend/Stop/Move work exactly like any other row.
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
- **Reports**: open Reports, switch between the Computers/Billiards buttons, try Daily/Weekly/
  Monthly and the manual date pickers after running a few test sessions (prepay, postpay, free) on
  both kinds. Check totals and the per-PC/per-tariff/per-type breakdowns match what you started,
  and that a billiard session never shows up in the Computers report or vice versa.
- **Manual resources**: click "Add Computer" and "Add Billiard", confirm both appear in the main
  table with the right icon, can Start/Extend/Stop/Move a session, and never go "offline" or
  respond to Turn on/Restart/Shut down.
- **Tariffs**: Settings > Tariffs - add a new tariff, rename an existing one, save, and confirm
  the new/renamed name shows up immediately in the Add dialogs and the main table.
- **Daily backup**: check `%ProgramData%\ClubControl\Backups` - a `club_YYYY-MM-DD.db` file should
  appear within an hour of the server running on a new day. Older backups beyond the newest 30 are
  deleted automatically.
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
- **Bulk start**: select several PCs (or billiards) that share one tariff (Ctrl/Shift-click or
  drag), click Start (or right-click > Free) - one dialog should start identical sessions on all
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
  Billiards), the server mints a random secret token for it, sends it once, and the client stores
  it locally (`%ProgramData%\ClubControl\client-token.dat`). Every heartbeat after that must
  include the matching token, or the server ignores it outright (logged as a rejected-heartbeat
  event) - so a second machine that merely learns or guesses a real PC's GUID can no longer take
  over its commands or overwrite its reported IP/MAC/hostname. A brand new, not-yet-configured PC
  is still trusted at face value the moment it first appears as "New PC" (same as before) - that
  bootstrap moment is unavoidable without a separate out-of-band provisioning step, and is no
  weaker than today's "recognise it by hostname/IP in the list" process.
- **Revoking/rotating a PC's token:** clear its **Number** in Settings > Computers & Billiards and
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

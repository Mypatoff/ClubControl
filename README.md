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
USB stick or a shared folder). `scripts/install-server.ps1` / `scripts/install-client.ps1` are not
included in the publish output - copy them into the same folder as the matching `.exe` before
running them.

## One-time setup per PC

### Admin laptop (server)

1. Copy the published server files and `install-server.ps1` into one folder.
2. Run `install-server.ps1` (right-click > Run with PowerShell, or
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
5. Copy the published client files and `install-client.ps1` into one folder on the PC, then run
   `install-client.ps1`. It asks for Administrator rights once and creates a Task Scheduler task
   that starts `ClubControl.Client.exe` at logon with the highest available privileges (needed for
   the keyboard-blocking hook and for shutdown/restart).
6. Log off and back on (or reboot) to confirm the client starts automatically and finds the
   server. The first time, it appears as "New PC" in the admin panel - assign it a number and
   tariff under Settings > Computers & Billiards.

## PCs with no LAN connection, and billiard tables

Not everything billable runs `ClubControl.Client`: a PC with no network cable/Wi-Fi, or a
billiard table, has nothing to install software on. For those, use the **"Add Computer" /
"Add Billiard"** buttons on the main toolbar instead of waiting for auto-discovery:

- Pick a number, pick a tariff (create one first under Settings > Tariffs if you need a new price
  tier - tariffs are freely named, not limited to a fixed pair), and Save.
- These rows never show as online/offline (there is no client to report a heartbeat), and the
  Turn-on/Restart/Shut-down buttons do nothing for them (nothing to power-cycle) - that is expected,
  not a bug. Start/Extend/Stop/Move work exactly like any other row.
- Right-click a row (or a multi-selection) for the same actions as the toolbar, scoped to just
  that row/selection.

## Manual test checklist (Phase 5 and 6)

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
- **Firewall rule on first run**: on a fresh admin laptop (rules not yet added), run
  `ClubControl.Server.exe` as Administrator once, or run `install-server.ps1` - check
  `netsh advfirewall firewall show rule name="ClubControl Server (TCP 5050)"` reports the rule.

## Notes and known limitations

- Reports' "income" reflects each session's stored amount - a postpay/free session still running
  during the report period contributes 0 until it ends, same as everywhere else in the app.
- A handful of internal validation error messages (e.g. "Payment must be between X and Y UZS")
  are still English-only; all screen labels, buttons, and the lock screen are translated.
- The database schema changed in Phase 6 (tariffs became free-form, PCs gained a resource kind).
  There is no migration system yet - if you have an existing `club.db` from before Phase 6, delete
  it (`%ProgramData%\ClubControl\club.db`) once; the server recreates it with default tariffs on
  next start. Fine while testing; worth adding real EF Core migrations before this holds real data
  you can't afford to lose.

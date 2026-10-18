# Fellowship

A Windows desktop LAN companion for **BFME2 1.00**. Native WPF windows for a room browser, battle rooms, friends, and direct messages. No central service; peers communicate over Radmin VPN.

## Run

Download and run `Fellowship-0.1.23-win-x64.msi` on each Windows 10/11 x64 PC. It installs for your Windows account in `%LOCALAPPDATA%\Programs\Fellowship`, adds a Start menu shortcut and an entry in Installed apps, and starts quietly in the tray at sign-in. Settings lets you disable startup and open the preferences folder. A portable ZIP is also available. The build includes .NET. Neither BFME2 nor Radmin VPN is bundled. See `TEST-WITH-A-FRIEND.txt` inside the download.

New rooms default to **Rhûn, six players / 3v3, 4,000 resources, 2× command points, Ring Heroes off and Custom Heroes off**. The patch is locked to 1.00. For supported map/rule selections, the host launcher saves the selected map preference under the current Windows account. Rules are applied when the native LAN lobby opens. Open Multiplayer → Network → Create Game. Guest players open Network and join the host. Fellowship waits for the matching native lobby and applies each local player's selected team and spot through native LAN functions. With sync enabled, Fellowship prepares its helper before launching BFME2. Approve the Windows prompt before the game opens; the helper waits for the configured game and editable LAN lobby. This keeps the approval dialog out of graphics initialization. If opening the game manually, wait for “Sync ready” first. Teams and spots synchronize automatically in both directions. The host's map, resources, command-point multiplier, Custom Heroes and Ring Heroes also synchronize both ways. There is no Apply button.

Closing the browser hides it and keeps Fellowship connected. Closing a battle room leaves it. Use the tray icon to reopen Rooms, Friends, or the current room. Use **Leave** to leave a room, and **Quit Fellowship** in the tray menu to exit completely. Windows may initially place the icon in the hidden-icons area.

## Implemented

- Separate, resizable room-browser, battle-room, friends and private-chat windows, with single-instance activation.
- Compact match summary, automatic/manual preset indicator, collapsible settings, default-rule reset and ready-player count.
- Incoming private messages keep your current window focused, show an unread count in their window title, and play an optional alert sound. Toggle sound in Settings.
- Connection retries recover after port conflicts; discovery distinguishes unreachable peers from peers with no hosted room.
- Public room discovery recovers from offline-peer UDP errors without restarting the app. Discovery uses UDP 28743, direct-IP fallback, and private rooms restricted to the host's saved friend addresses.
- Host map/rule selectors at creation and editing, installed-map catalog, dynamic executable detection, room preview, player info, and online/offline friend groups.
- Live roster counts, four-second room refresh, and TCP keepalive for lost VPN connections.
- Host-owned rooms, player limit, roster, room chat, and readiness.
- Map/rule synchronization; changing settings clears guest readiness.
- Host launch requests invoke each ready player's locally selected executable. A peer never supplies executable paths or shell commands to another PC.
- Individual launch/relaunch at any time. Exiting the game does not end the launcher room.
- A guest can disconnect and rejoin a launched room without automatically starting their game.
- Friends saved locally, periodic presence checks, acknowledged private messages.
- Bounds-checked length-prefixed JSON over TCP 28742, bound only to the selected IPv4 interface.

## Current limits

- Live sync pauses during the match and reconnects after observing the native LAN room become inactive or its setup close and a stable new lobby open. It reapplies the current Fellowship selections. If teardown was not observed, it stays paused rather than guessing. Host map, rules, capacity, team and valid map spot are remembered for new Fellowship rooms. The 0.1.15 Start-crash safeguard was confirmed by a user test. Version 0.1.18 ignores stale countdown timestamps when both native start flags are clear; live team/spot writes and readback passed in an affected post-match lobby. Version 0.1.18 also recognizes an inactive LAN room even when the setup screen remains allocated. The recorded close/reopen sequence confirmed that the LAN room pointer becomes null while the setup screen remains allocated; this case is covered by regression checks. Native map/rule/team/spot writes passed in the new lobby. A full repeated-match test of automatic reconnection remains pending.

- Native lobby creation and guest joining remain manual. Direct team/spot assignment is verified on the host; native guest synchronization still needs a two-PC test. Cursor navigation is not included. Active-match recovery is not implemented.
- No claim that BFME2 can restore a player's army after quitting an ongoing match. Relaunching and rejoining the **Fellowship room** is supported; active-game recovery is not.
- Public rooms use UDP discovery on the selected VPN interface. Broadcast delivery depends on the VPN and firewall; direct host IP remains available. There is no Internet-wide directory.
- No user accounts, crash-driven host recovery, offline messaging or durable chat history.
- Trust the VPN peers. Names are self-asserted, and this prototype has no application-level authentication or encryption. Use it on a private Radmin network with people you know.
- Executable version checking is a basic 1.00 compatibility gate, not a guarantee that mods or map archives match. Both players must use the same game files/patch.
- Unsigned community test build.

## Native game integration

Cursor navigation was removed in 0.1.11. Map and rule presets use game data. Version 0.1.18 also calls BFME2's native LAN team/spot setters on its game thread, then reads back the result. Players still open/join the native LAN lobby themselves. No cursor, key presses, or menu navigation are used. Direct host assignment was verified live; cross-PC native guest synchronization still requires a two-computer test.

## Development

Requires .NET 9 SDK with Windows desktop targeting support, Visual Studio C++ x86 tools and Windows SDK libraries. The native bridge builds automatically before the WPF project; it has no external runtime dependency.

```powershell
dotnet build Fellowship/Fellowship.csproj -c Release
dotnet run --project tests/ProtocolTests.csproj -c Release
dotnet publish Fellowship/Fellowship.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist/Fellowship
```

`tests/ProtocolTests.csproj` exercises real TCP loopback peers: discovery, join, capacity, room/private chat, readiness, configuration changes, launch signals, disconnect/rejoin, malformed frames, and host departure. No actual BFME2 process is launched in those tests.

Configuration lives in `%LOCALAPPDATA%\Fellowship\settings.json`; the latest selected setup is saved as `last-session.json`. No installed game or GameRanger file is changed. The host preset backs up NetworkPref.ini into this folder, then updates its map preference. The preset helper requests elevation separately from the network-facing app, launches only lotrbfme2.exe, verifies the game.dat SHA-256 and rule descriptors, and changes four default values in the new game process. They disappear when BFME2 exits; they also affect new skirmish defaults during that session. See host-preset.log for diagnostics. Alternate 1.00 binaries are rejected by automatic setup rather than using unverified offsets.




## Downloads and updates

[Download the latest Windows build](https://github.com/Joshua131313/fellowship-bfme2/releases/latest). From version 0.1.9 onward, use Settings > Check for updates. Version 0.1.11 prefers the MSI asset, verifies its GitHub SHA-256 digest, closes Fellowship, installs the update into the stable application folder, and relaunches it. Shortcuts and preferences are preserved. Installed clients do not fall back to versioned portable folders. Older ZIP updaters remain supported; run the MSI once to migrate to an installed copy. Uninstall through Windows Installed apps; personal preferences remain for reinstall.

The lobby provides inline map/rule controls, explicit room-name/privacy confirmation, and previews read from the local game installation. No game assets are bundled.

Run `dotnet run --project ui-smoke/UiSmoke.csproj -c Release` for offscreen UI and updater metadata checks. Private reference captures under research/ are optional and excluded from the repository. Installed-game checks are skipped when BFME2 is absent.

## Spot and team selection

In a room, click an empty map circle to claim a starting spot, and choose your team. Click your own spot again or Clear my spot to release it. The host resolves conflicting claims; map changes clear spots, disconnects release them, and placement changes reset readiness. Use the same version on all PCs. An elevated local listener starts automatically when BFME2 is running and a Fellowship room is open. It waits for the matching native LAN setup lobby, applies the local player's choices, and monitors both directions about twice per second. Native edits update Fellowship; confirmed Fellowship edits call the native LAN setters. The Fellowship host must also be the BFME2 host: host map/rule edits use the native map setter and rule-commit callback, including normal readiness reset and LAN broadcast. Guests receive those settings through the host. It pauses outside the matching lobby and stops when the room, app, or game closes. The bridge rejects occupied spots, wrong maps/hosts, invalid choices, non-local slots, and unsupported game binaries. The listener uses a current-user-only local pipe, validates both process identities, and accepts only bounded lobby setting commands. A rejected selection pauses the listener rather than repeatedly overriding manual corrections. Reopen the Fellowship room to reconnect after a failure. On failure, the BFME2 lobby stays open for manual correction. Each guest still joins BFME2 manually.

## Room host controls

The battle-room X and Leave button leave the room. A departing host hands it to the next player; right-click a player to promote or kick them. Everyone must use 0.1.9+ and have their VPN listener active. Planned handoff preserves the room, rules, spots and teams, and resets guest readiness. Sudden host crashes are not automatically recovered. If handoff fails after activation, reconnect to the new host address shown. Closing the main browser still minimizes it to the tray.

Version 0.1.9 fixes Windows update ZIP cleanup. If an older updater cannot complete installation, download 0.1.9 manually once. Each shipped change receives a new version and a published GitHub release; source-only pushes do not update installed apps.

Build the installer after publishing to `dist/Fellowship-0.1.23`: `dotnet build installer/Fellowship.wixproj -c Release`. WiX SDK 5.0.2 is restored through NuGet.

### Troubleshooting startup while a room is open

Settings → Live BFME2 synchronization can be switched off before launching. This keeps Fellowship rooms and chat connected without starting the game sync helper. While off, configure the native game manually. It can be re-enabled after the native LAN lobby opens. This isolates reported DirectX startup failures; it is not a confirmed graphics fix.

Version 0.1.22 moves sync preparation ahead of game launch. Launch mine and Launch all wait for the authenticated helper to acknowledge readiness. A cancelled or failed preparation disables live sync rather than repeatedly showing permission prompts. The helper cannot launch games or arbitrary commands and exits when its room connection closes. Windows approval is still required; no persistent elevated service or UAC bypass is installed.

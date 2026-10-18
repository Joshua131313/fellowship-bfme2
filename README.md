# Fellowship

A Windows desktop LAN companion for **BFME2 1.00**. Native WPF windows for a room browser, battle rooms, friends, and direct messages. No central service; peers communicate over Radmin VPN.

## Run

Extract `dist/Fellowship-0.1.8-win-x64.zip` on each Windows 10/11 x64 PC and run `Fellowship.exe`. The build includes .NET. Neither BFME2 nor Radmin VPN is bundled. See `TEST-WITH-A-FRIEND.txt` inside the download.

New rooms default to **Rhûn, six players / 3v3, 4,000 resources, 2× command points, Ring Heroes off and Custom Heroes off**. The patch is locked to 1.00. For supported map/rule selections, the host launcher applies the native Rhûn preference and session-only rule defaults. Open Multiplayer → Network → Create Game, then assign three players to each team. Guest players open Network and join the host. Map and rule selections also apply through the verified host preset helper; team assignment remains manual.

Closing the browser or battle-room window hides it and keeps Fellowship connected. Use the tray icon to reopen Rooms, Friends, or the current room. Use **Leave** to leave a room, and **Quit Fellowship** in the tray menu to exit completely. Windows may initially place the icon in the hidden-icons area.

## Implemented

- Separate, resizable room-browser, battle-room, friends and private-chat windows, with single-instance activation.
- Compact match summary, automatic/manual preset indicator, collapsible settings, default-rule reset and ready-player count.
- Incoming private messages keep your current window focused, show an unread count in their window title, and play an optional alert sound. Toggle sound in Settings.
- Connection retries recover after port conflicts; discovery distinguishes unreachable peers from peers with no hosted room.
- Public room discovery over UDP 28743, direct-IP fallback, and private rooms restricted to the host's saved friend addresses.
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

- Experimental navigation can be enabled in Settings for the captured English 16:9 menus: host creation and guest navigation to Network. Live testing confirmed navigation through Multiplayer to Network. The updated Create Game step still needs live confirmation. Guest room selection/joining and team assignment remain manual. Active-match recovery is not implemented.
- No claim that BFME2 can restore a player's army after quitting an ongoing match. Relaunching and rejoining the **Fellowship room** is supported; active-game recovery is not.
- Public rooms use UDP discovery on the selected VPN interface. Broadcast delivery depends on the VPN and firewall; direct host IP remains available. There is no Internet-wide directory.
- No user accounts, host migration, offline messaging or durable chat history.
- Trust the VPN peers. Names are self-asserted, and this prototype has no application-level authentication or encryption. Use it on a private Radmin network with people you know.
- Executable version checking is a basic 1.00 compatibility gate, not a guarantee that mods or map archives match. Both players must use the same game files/patch.
- Unsigned test build; not an installer or a commercial GameRanger replacement.

## Experimental navigation

Enable **Experimental navigation** in Settings, quit any existing BFME2 process, then launch from a Fellowship room. Approve the helper permission prompt and keep the game focused. It waits up to five minutes for a stable main menu, then recognizes menu labels before clicking, sends each step once, and stops on focus loss, Escape, an unknown screen or a timeout. The host route is Main Menu → Multiplayer → Network → Create Game. Guests stop at Network and must select/join their host manually. This is an opt-in prototype, not verified GameRanger-equivalent integration. It supports the exact 1.00 binary already required by the preset helper and the captured English 16:9 layout. No match is started automatically. Diagnostics are in host-preset.log.

## Development

Requires .NET 9 SDK with Windows desktop targeting support.

```powershell
dotnet build Fellowship/Fellowship.csproj -c Release
dotnet run --project tests/ProtocolTests.csproj -c Release
dotnet publish Fellowship/Fellowship.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist/Fellowship
```

`tests/ProtocolTests.csproj` exercises real TCP loopback peers: discovery, join, capacity, room/private chat, readiness, configuration changes, launch signals, disconnect/rejoin, malformed frames, and host departure. No actual BFME2 process is launched in those tests.

Configuration lives in `%LOCALAPPDATA%\Fellowship\settings.json`; the latest selected setup is saved as `last-session.json`. No installed game or GameRanger file is changed. The host preset backs up NetworkPref.ini into this folder, then updates its map preference. The preset helper requests elevation separately from the network-facing app, launches only lotrbfme2.exe, verifies the game.dat SHA-256 and rule descriptors, and changes four default values in the new game process. They disappear when BFME2 exits; they also affect new skirmish defaults during that session. See host-preset.log for diagnostics. Alternate 1.00 binaries are rejected by automatic setup rather than using unverified offsets.




## Downloads and updates

[Download the latest Windows build](https://github.com/Joshua131313/fellowship-bfme2/releases/latest). From version 0.1.8 onward, use Settings > Check for updates. Download & restart verifies the GitHub SHA-256 digest and launches the updated copy under LocalAppData/Fellowship/versions. Settings and friends are preserved. Existing shortcuts still open their original copy.

The lobby provides inline map/rule controls, explicit room-name/privacy confirmation, and previews read from the local game installation. No game assets are bundled.

Run `dotnet run --project ui-smoke/UiSmoke.csproj -c Release` for offscreen UI and updater metadata checks. Private reference captures under research/ are optional and excluded from the repository. Installed-game checks are skipped when BFME2 is absent.

## Spot and team selection

In a room, click an empty map circle to claim a starting spot, and choose your team. Click your own spot again or Clear my spot to release it. The host resolves conflicting claims; map changes clear spots, disconnects release them, and placement changes reset readiness. Use the same version on all PCs. These are Fellowship lobby preferences only: guests must still join BFME2 manually and players must apply spots/teams in its lobby. Next integration stages are guest auto-joining, then automatic spot/team assignment with manual recovery.

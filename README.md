# Fellowship

### Your BFME2 games, together in one place.

[![Download Fellowship for Windows — direct MSI installer download](docs/download-windows.svg)](https://github.com/Joshua131313/fellowship-bfme2/releases/latest/download/Fellowship-Setup.msi)

**Fellowship is a Windows companion for The Battle for Middle-earth II 1.00.** Find games on your VPN, gather your friends, plan teams and starting positions, and carry supported selections into the native BFME2 lobby. A desktop dashboard brings rooms, online players, messages, and preferences together, with separate windows when you need them.

**[Other downloads & release notes](https://github.com/Joshua131313/fellowship-bfme2/releases/latest)** · **[Report an issue](https://github.com/Joshua131313/fellowship-bfme2/issues)**

![Fellowship dashboard in dark mode, showing room discovery, server selection, search, ping, and direct-IP joining](docs/screenshots/room-browser.png)

[Features](#features) · [Get started](#get-started) · [Rhûn 3v3](#automatic-rhûn-3v3-sides-and-substitutions) · [Screenshots](#screenshots) · [Troubleshooting](#troubleshooting) · [Build from source](#build-from-source)

## Features

### Find your next game

- **Discover public rooms** on the selected VPN interface, with Radmin VPN selected by default when available. Switch networks to see that network's rooms.
- **Search and filter** by room name and game mode, then inspect the map, rules, and roster before joining. Direct host-IP joining is available when discovery misses a room.
- **Sort the room table** by its column headers, with ascending and descending indicators. Sorting and resized column widths survive tab changes and app restarts; reset widths whenever you want the default layout back.
- **See connection quality** with ping indicators in the room browser, player roster, and player information.

The room browser aligns server, mode, and search filters above the room list. The Create room form remembers your last successfully created room title, including after a restart. Use **View → Reset column ordering** or the table context menu to restore the original column order; resetting column widths is available separately.

### Find players in one Radmin group

Open the **Server** menu and hover over **Radmin VPN** to choose a group from its side menu, or use the group selector in Settings. Click **Radmin VPN** itself to use all groups available to Fellowship. The side menu opens to the left in Arabic and to the right in the other languages; refreshing keeps it open. Fellowship reads the actual group names and members from your running Radmin client and remembers your choice.

The selection filters Fellowship discovery, player lists, messaging and room connections. **Manage actual VPN group connections in Radmin.** The automatic menu-driving connection controller has been removed; Fellowship does not click Enable/Disable Network commands or fall back to that method. Switching groups leaves the current Fellowship room.

BFME2 and other applications continue to use the groups connected in Radmin. Keep Radmin running so Fellowship can refresh its member list. [Integration details and current limits](docs/radmin-groups.md).

### Build a room around your group

New rooms default to the **3v3 preset** when Rhûn is installed:

> **Rhûn · 6 players · 4,000 starting resources · 2× command points · Custom Heroes off · Ring Heroes off**

Choose built-in **3v3, 2v2, 1v1, and FFA** presets, save your own, or select **Custom** to edit the setup. Search the installed map catalog, set the player limit and hero options, and choose a public or friends-only room.

Click the map preview to claim a starting spot, choose a team and player color, and save preferred colors and per-map positions in **Game preferences**. Valid, available preferences are applied when joining. Map previews use the active theme, and player information and preview windows let you keep interacting with the rest of Fellowship.

### Keep the native lobby in sync

With **Live BFME2 synchronization** enabled, Fellowship synchronizes supported map, rules, team, starting-position, and color selections through the game's own lobby controls. Changes can flow back from BFME2 into Fellowship, so you can make supported adjustments from inside the game.

The helper validates the supported game binary, waits for a matching, stable LAN setup lobby, checks occupied positions, and pauses writes during match transitions. Each player's helper changes that player's selections. The **BFME2 lobby host can be a different player from the Fellowship room owner**.

For a closer look at the special Rhûn rules, see [Automatic Rhûn 3v3 sides and substitutions](#automatic-rhûn-3v3-sides-and-substitutions).

### Get everyone ready and launch

- **Request ready:** the room host can prompt participants to ready up, with a dedicated ready-check sound that follows their sound preferences.
- **Launch mine / Launch all:** launch your local game, or coordinate a host launch for participants who have explicitly readied up.
- **Abort locally:** while your game is running, Abort stops your own BFME2 instance. An active authorized live-sync helper reuses its permission to stop the game.
- **Keep the group together:** Fellowship rooms remain open between games. Promote another room host, hand off ownership, or manage participants without recreating the whole group.

Fellowship readiness authorizes a launch request; **BFME2's native readiness is managed separately**.

### See who is around

The **Online players** tab shows players discovered on your selected network, including people outside your friends list. Search by player name or VPN address from the dashboard, or keep the Friends window open alongside your room.

Player activity distinguishes **waiting in a lobby**, **in game**, **AFK**, and **reconnecting**. Right-click a player to open player information, send a message, manage friendship or blocking, invite them, or view and join their visible game room.

AFK status can follow idle detection automatically, or you can set it yourself. **Manual AFK stays active until you mark yourself available again.** Player information shows idle duration and connection history; recent last-online times use labels such as “5 minutes ago,” with an exact timestamp on hover. Click a player's IP address to copy it.

### Chat while you organize

Use room chat for the group and private messages for individual conversations. Compact bubbles keep your messages on the right and incoming messages on the left. Opening a chat focuses the composer, and new messages stay in view unless you scroll back to read earlier messages. Press **Enter** to send, select text across multiple messages, or use the message context menu to copy a message or selection. Player info opens once per player and includes a **Send message** action. Player context menus open beside the cursor, preferring the right side. Sound cues and Windows notifications help you notice invitations, messages, friend activity, and new rooms.

### Make Fellowship feel like yours

| Preference | Options |
| --- | --- |
| Appearance | **Light**, **Dark**, **Black** with true-black surfaces, or **System** to follow Windows. |
| Language | **English, Français, Español, العربية, Türkçe**, including translated menus, tooltips, tray actions, and notification text. |
| Sound | Master mute, volume control, individual event toggles, and sound previews—including the ready check. |
| Notifications | Enable or disable Windows notifications for friend and room activity. |
| Game defaults | Preferred player color and starting spot for each map. |
| Backup | Export and restore preferences and contacts. |
| Startup | Choose whether Fellowship starts in the tray at Windows sign-in. |

Theme, sound, and settings changes **apply and save automatically**. First-run onboarding helps you choose language, appearance, and sound defaults and stays completed on later launches.

## Get started

### Requirements

- **Windows 10 or 11, x64.** The installer and portable build include the .NET runtime.
- Your own **BFME2 1.00 installation**, with matching game files and maps across players. Fellowship does not include the game.
- **Radmin VPN**, or another compatible IPv4 LAN/VPN connection, configured on every computer. Radmin is the default network choice when present.
- The **same current Fellowship version** on all participants, especially when using coordinated live sync.

### Your first game

1. **Install Fellowship.** Download the `.msi` from [the latest release](https://github.com/Joshua131313/fellowship-bfme2/releases/latest). The installer creates desktop and Start menu shortcuts. A portable ZIP is also available.
2. **Connect your group.** Join the same VPN network on each PC. In Fellowship, select the matching server/network interface. With Radmin, choose a specific **Radmin group** to filter Fellowship to its members. Enable or disable actual VPN group connections in Radmin itself.
3. **Set up your profile.** Complete the first-run preferences, then check your nickname and detected `lotrbfme2.exe` path in **Settings**.
4. **Host or join.** Create a room using the default 3v3 preset or choose another setup. Join a discovered room, preview it first, or enter the host's VPN IP directly.
5. **Choose your place.** Pick your team, map spot, and color. Participants ready up when they want to permit a host launch; the host can send a ready request.
6. **Prepare sync and launch.** With live sync enabled, approve Windows' permission prompt and wait for **Sync ready**. Use **Launch mine**, or let the host use **Launch all**.
7. **Enter BFME2's LAN lobby.** The in-game host opens **Multiplayer → Network → Create Game**. Other players join that native lobby manually. Fellowship begins supported synchronization once the lobby matches.

Fellowship coordinates the group and supported lobby selections. **Creating and joining the native BFME2 lobby still happens inside BFME2.** Rejoining an already-running match or restoring an army after quitting is not supported.

## Automatic Rhûn 3v3 sides and substitutions

These special rules apply **only when the Fellowship room uses the 3v3 preset/mode and the map is Rhûn**. Other modes and maps use normal preference and selection behavior.

| Situation | What Fellowship does |
| --- | --- |
| A team hosts the native BFME2 lobby | Put that team on the **right as Team 2**, with the opposing team on the **left as Team 1**. |
| The opposing team hosts the next game | Mirror positions across the map while preserving each player's top, middle, or bottom position. |
| A different player from the same team hosts | Keep the team's side and players' positions; changing the hosting player alone does not flip them. |
| An active player replaces an observer | After the native roster settles, assign an eligible unassigned replacement a known, free position for their selected team. |

Fellowship reads the **actual BFME2 hosting player and native team selections**, then uses the previous match's lineup to identify the hosting team. The Fellowship room owner distributes the shared plan; room ownership does not decide which team hosts the game.

Temporary observer status while connecting is allowed to settle before replacement logic acts. Existing occupied spots and explicit manual changes are respected. Hosting history lasts for the current Fellowship room; replacement history lives with the sync helper. A full app restart clears this transient history.

All participating players need the updated app and enabled live sync for coordinated changes. Simulated lobby and loopback multiplayer tests cover these rules; full multi-PC game testing remains important. See [the rotation and synchronization notes](docs/automatic-rotation.md) for implementation details and validation scope.

## Screenshots

The dashboard above shows the room browser. Expand the views below to see the lobby, player activity, and customization.

<details>
<summary><strong>Private chat — compact bubbles, conversation selection, and automatic scrolling</strong></summary>

![Private chat with incoming messages on the left and your messages on the right](docs/screenshots/private-chat.png)

</details>

<details>
<summary><strong>Battle room — roster, chat, map positions, and launch controls</strong></summary>

![Battle room with player roster, room chat, interactive map preview, team and color controls, ready request, and launch buttons](docs/screenshots/battle-room.png)

</details>

<details>
<summary><strong>Online players — game activity, room names, and AFK indicators</strong></summary>

![Online players in light mode showing an in-game player and an AFK player waiting in a lobby](docs/screenshots/online-players.png)

</details>

<details>
<summary><strong>Settings — appearance, language, sound, and automatic saving</strong></summary>

![Settings in light mode with profile, network, language, theme, sound volume, and backup sections](docs/screenshots/settings.png)

</details>

*Screenshots are rendered from the app with demonstration data. Map previews come from a local BFME2 installation; Fellowship downloads do not contain BFME2 or its map archives.*

## How it connects

```text
Fellowship on your PC ←──── selected LAN / VPN ────→ Fellowship on another PC
        │                                                     │
   local sync helper                                    local sync helper
        │                                                     │
   your BFME2 lobby ←──────── native game LAN ────────→ their BFME2 lobby
```

Fellowship uses direct peer connections, with **no account signup or central room service**. Discovery uses **UDP 28743**; room state, presence queries, and messaging use **TCP 28742**. GitHub provides downloads and update metadata.

- **Discovery is network-local.** Online players are discovered reachable peers, not a worldwide player directory. Firewalls and VPN routing can affect discovery.
- **Friends-only rooms follow the room host's friends list.** Their details are hidden from other peers.
- **Brief connection loss has a grace period.** Reconnecting status allows time for a peer to return; confirmed room closures are removed.
- **Fellowship room ownership can transfer.** An orderly handoff and lost-host recovery are supported where reachable peers can take over. This does not transfer a running BFME2 match.
- **Chat requires connectivity.** Offline message delivery and durable chat history are not implemented.

This is an unsigned community build intended for trusted groups. Player names are self-asserted, and the peer protocol does not provide encrypted messaging. Native sync supports validated BFME2 binaries; compatibility with arbitrary mods or map archives is not guaranteed.

## Updates, backups, and Windows behavior

Use **Settings → Check for updates** or the dashboard's update notice to download and install a release. The updater reopens Fellowship after installation. Settings provides backup export/restore and a shortcut to the preferences folder.

| Location | Purpose |
| --- | --- |
| `%LOCALAPPDATA%\Programs\Fellowship` | Installed application. |
| `%LOCALAPPDATA%\Fellowship` | Preferences, contacts, activity history, and support files. |

Closing the dashboard keeps Fellowship running in the system tray. Closing a battle room leaves that room and attempts a host handoff when needed. Use the tray menu's **Quit Fellowship** action to exit fully. Startup at Windows sign-in is configurable in Settings.

## Troubleshooting

| Problem | What to check |
| --- | --- |
| Rooms or players are missing | Confirm all PCs use the same VPN network, selected interface, and current app version. Refresh presence or join by host IP. Check Windows Firewall access for Fellowship on that network. |
| A player stays Reconnecting | Check that their VPN and Fellowship are still running. A brief interruption can clear after subsequent presence checks. |
| Radmin groups or members cannot be read | Keep Radmin VPN running and choose **Refresh Radmin groups** from the Radmin side menu, or use the refresh button in Settings. If needed, open its main window. An unavailable selected group never silently switches to all groups. |
| BFME2 reports a DirectX error at launch | Let sync preparation and its Windows permission prompt finish before launching. If needed, disable live sync, launch BFME2, then enable sync after the game loads. |
| Live sync cannot apply a selection | Keep the native setup lobby open. Check the supported game version, matching map and participants, and occupied spots. Correct conflicting selections in BFME2 when sync pauses. |
| Rhûn sides do not coordinate | Confirm **3v3 + Rhûn**, update every participant, enable live sync, and select native teams. The in-game host determines the hosting side. |
| An older updater loops through repairs | Close Fellowship, run the latest release's `Fellowship-Repair-<version>.exe`, then install that release's MSI directly. The helper restores verified installer sources. If it still fails, include the installer/recovery log in your issue. |
| An update fails | Use **Open log** in the updater and include the error code and log with your report. Error 1603 can have several causes; error 1622 concerns opening the installer log. |

When [reporting an issue](https://github.com/Joshua131313/fellowship-bfme2/issues), include the Fellowship version, selected network, game version, steps to reproduce, and any relevant log or screenshot. Mention whether the Fellowship room owner and BFME2 lobby host are different players when reporting sync problems.

## Build from source

Requires **Windows**, the **.NET 9 SDK**, **Visual Studio C++ x86 build tools**, and Windows SDK libraries. The native bridge builds with the WPF app.

```powershell
# Build the desktop app and native helper.
dotnet build Fellowship/Fellowship.csproj -c Release

# Run loopback multiplayer checks and offscreen UI checks.
dotnet run --project tests/ProtocolTests.csproj -c Release
dotnet run --project ui-smoke/UiSmoke.csproj -c Release

# Publish a self-contained Windows x64 executable.
dotnet publish Fellowship/Fellowship.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist/Fellowship
```

To build the MSI, publish to the versioned output folder expected by `installer/Fellowship.wixproj`, then run:

```powershell
dotnet build installer/Fellowship.wixproj -c Release
```

Protocol checks use real loopback TCP/UDP peers. UI checks render screens offscreen and exercise selections, persistence, lifecycle, localization, and update behavior. These checks complement live testing with multiple PCs and BFME2 instances.

Additional notes: [Rhûn rotation and native sync](docs/automatic-rotation.md) · [Player colors](docs/player-colors.md) · [Blocking](docs/blocking.md) · [Legacy room-isolation removal](docs/room-isolation.md)

---

Fellowship is an independent community project and is not affiliated with GameRanger, Radmin, Electronic Arts, or the owners of The Lord of the Rings.

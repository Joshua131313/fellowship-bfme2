# BFME2 patch support

Fellowship rooms can advertise the BFME2 patch your group wants to play. Create room preselects the **verified installed patch**, with **1.00** as the fallback when the installation cannot be verified. The selector controls the room's map catalog and presets; changing it does not switch your installed game.

## Available patches

The room selector contains **1.00, 1.06, 1.09, 1.09v2, 1.09v3, and 1.09v3.1**, plus the **newest 1.09 beta** recognized by the switcher catalog. The beta option identifies its exact build; different beta builds are different patches.

The selector keeps older releases and historical beta entries out of the regular list. Fellowship retains their internal catalog records for installed-patch detection, existing room recognition, and parent archive dependencies. It also reads the switcher's locally cached release metadata to recognize newer beta builds. A patch appearing in the selector does not mean its game files have been downloaded.

If your verified installed patch is an older beta, Create room retains that exact build as its selected value. That exception does not add historical beta entries to the normal dropdown; the regular list continues to offer only the newest beta.

The integration locates Ecth's Patch Switcher in its usual installation directories, beside the game, or through its Windows installation registration. Discovery and detection read patch metadata, downloaded archives, and relevant archive options without changing them. Choosing a room patch, creating a room, or joining a room does not start the switcher or alter game files. The separate **Set to …** button is an explicit request to run the switcher's native command line, as described below.

## Maps, previews, and presets

Select **PATCH** in Create room before choosing the map. The available maps, map previews, and starting positions are read from that patch's locally available BIG archives. For a patch that inherits earlier releases, Fellowship combines the selected release and its required parent archives. The switcher's applicable zoom, floor, and custom archive options are taken into account.

This lets a 1.09 room show its additional multiplayer maps while a 1.00 room uses the 1.00 catalog, even when your game is currently switched to a different patch. The relevant files must already exist locally; Fellowship does not download patch or map content. If a map preview or its starting-position metadata is unavailable, the app cannot reconstruct it from the patch name alone.

If an alternate patch's map files are missing, Create room explains how to install them and leaves its map list empty. Download the patch in Ecth's Patch Switcher before creating a room for it. This does not prevent joining somebody else's room.

Changing the patch keeps the selected map when it exists in both catalogs; otherwise the form chooses an available fallback. Saved presets belong to a patch. You can save the same preset name on two patches without replacing the other patch's preset. Older saved presets are treated as 1.00 presets.

Built-in map defaults also follow the selected patch:

| Patch / preset | Default map |
| --- | --- |
| 1.00: 3v3, 2v2, 1v1 | Rhûn, Buckland, and Rohan respectively; the existing 1.00 defaults stay unchanged. |
| 1.09v3 family: 3v3 | Six-player Lebennin IV. |
| 1.09v3 family: 2v2 | Four-player Lebennin. |
| 1.09v3 family: 1v1 | Two-player Fords of Isen II, with separate 1,000-resource and 4,000-resource presets. |
| 1.06 and all 1.09 versions: 4v4 | Eight-player Tournament Hills, with four players on each team. |

The 1.09v3 family includes 1.09v3.1 and its newer v3.2/u4 beta descendants. The Lebennin variants are selected by their exact archive paths, so their similar names cannot substitute a four-player map for a six-player map. Tournament Hills has a symmetric arrangement of four starting positions on each half of the map. A preset stays disabled when its exact map is unavailable locally. These room presets do not enable native automation on patches other than verified 1.00.

Map files with the same internal path can differ between patches. Fellowship uses the selected archive set for both the map image and its starting locations. Archive precedence follows the game's filename ordering: case-insensitive ASCII order, with the first matching archive taking priority. For example, a `#BT…big` overlay takes priority over `_WSMaps.big`, which takes priority over `Maps.big`.

## Joining with a different patch

You can join a Fellowship room even when your installed patch differs. Room chat, player information, and readiness remain available. The room shows a warning explaining the required patch and the detected local patch; the launch flow repeats that warning when needed.

If the installation cannot be verified, Fellowship says so instead of guessing. Detection checks the installed game binary and relevant archive files against local switcher sources or recognized stock signatures. A mixture of patch files, missing dependencies, or an unknown overlay may therefore produce an **Unknown** result.

Before playing:

1. Close BFME2.
2. Open Ecth's Patch Switcher. Use **Set to …** beside the room warning for a supported command-line target, or select the room's exact patch manually in the switcher, including its beta build.
3. Let the switch finish, then launch BFME2 from Fellowship.

A successful Fellowship room connection does not make different BFME2 patches compatible with each other. Everyone still needs matching game files and the selected map to play together.

## Set to the room's patch

The patch warning includes a **Set to …** action. It requires **BFME2 to be closed and Ecth's Patch Switcher to be already open**. If the switcher is not open, open it and retry the action. Fellowship invokes the installed `data\BFME2Switcher.exe` command directly; it does not navigate or click through the switcher interface.

The inspected compiled switcher supports these command targets:

| Requested patch | Native command arguments |
| --- | --- |
| 1.00 | `-switch -1.00 -force` |
| 1.06 | `-switch -1.06 -force` |
| The exact supported latest installed patch | `-switch -latest -force` |
| Another exact release or beta | Select it manually in Ecth's Patch Switcher. |

Fellowship uses `-latest` only when it can establish that the switcher will select the exact patch the room requires. It never maps an older requested release to a different latest release. Incomplete or unrecognized newer patch folders can prevent that exact-target check; use manual selection when the action explains that the requested patch cannot be switched through this command.

The switcher's command-line implementation may close its running GUI as part of its own switching process. Fellowship waits for the operation and verifies the installed patch before reporting success. Patch changes happen only after this explicit action or your own manual switcher use, never automatically when creating or joining a room. Fellowship does not implement its own patch-file editing or graphical switcher automation.

## Automatic synchronization

| Feature | Verified 1.00 installation and 1.00 room | Other patches or an unverified installation |
| --- | --- | --- |
| Room discovery, chat, player information, and ready requests | Available | Available |
| Local or coordinated game launch | Available | Available; patch warnings remain advisory |
| Room map catalog, previews, and saved presets | Read from local selected-patch files | Read from local selected-patch files |
| Native map, rules, team, spot, and color synchronization | Available with live synchronization enabled | Set these options manually in BFME2 |
| Automatic Rhûn 3v3 sides and replacement handling | Available for **3v3 + Rhûn** with live synchronization | Manual native setup |

The native adapter reads and writes the memory layout of one verified BFME2 1.00 binary. Other patches can change that layout, so patch catalog support does not enable that adapter on them. Fellowship skips incompatible synchronization preparation and leaves their native lobby setup to the player. The existing binary validation remains in place.

For 1.00 side handling, the **host of the actual BFME2 lobby** determines which team plays on the right. Another player from the same team hosting again keeps that side; the opposing team hosting mirrors positions. Replaying a match after a crash or OOS follows that hosting-team rule, rather than a game counter. See [automatic rotation and synchronization](automatic-rotation.md) for the detailed rules and validation scope.

## If maps or detection look wrong

- Check that Settings points to the intended `lotrbfme2.exe` and its accompanying `game.dat`.
- Make sure the selected patch and its required parent files are downloaded in the switcher. A catalog entry alone does not provide maps or previews.
- Finish any patch switch before launching the game. Do not switch game files while BFME2 is running.
- For an unknown or mixed installation, verify or reapply the desired patch in the switcher. Fellowship does not repair patch files automatically.
- Include the exact room patch, detected patch, and switcher build when reporting a patch or map issue.

Fellowship downloads contain neither BFME2 nor its patch archives. [Return to the README](../README.md).

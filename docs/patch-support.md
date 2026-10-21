# BFME2 patch support

Fellowship rooms can advertise the BFME2 patch your group wants to play. The patch selector defaults to **1.00** when creating a room. It controls the room's map catalog and presets; changing it does not switch your installed game.

## Available patches

The catalog includes **1.00, 1.06, 1.09, 1.09v2, 1.09v3, and 1.09v3.1**, plus individual beta builds listed by Ecth's Patch Switcher. Beta families include the switcher's v2 betas, 1.09v3 betas, 1.09v3.1 betas, and 1.09v3.2/u4 betas. Choose the exact displayed build: two different betas are different room patches.

Fellowship contains a catalog for recognizing room patches even when a participant does not have the switcher. It also reads the switcher's locally cached release metadata, so locally known catalog entries can supplement that list. A patch appearing in the selector does not mean its game files have been downloaded.

The integration locates Ecth's Patch Switcher in its usual installation directories, beside the game, or through its Windows installation registration. It reads patch metadata, downloaded archives, and relevant archive options. It never starts the switcher, clicks its controls, or copies, renames, or changes its game files.

## Maps, previews, and presets

Select **PATCH** in Create room before choosing the map. The available maps, map previews, and starting positions are read from that patch's locally available BIG archives. For a patch that inherits earlier releases, Fellowship combines the selected release and its required parent archives. The switcher's applicable zoom, floor, and custom archive options are taken into account.

This lets a 1.09 room show its additional multiplayer maps while a 1.00 room uses the 1.00 catalog, even when your game is currently switched to a different patch. The relevant files must already exist locally; Fellowship does not download patch or map content. If a map preview or its starting-position metadata is unavailable, the app cannot reconstruct it from the patch name alone.

If an alternate patch's map files are missing, Create room explains how to install them and leaves its map list empty. Download the patch in Ecth's Patch Switcher before creating a room for it. This does not prevent joining somebody else's room.

Changing the patch keeps the selected map when it exists in both catalogs; otherwise the form chooses an available fallback. Saved presets belong to a patch. You can save the same preset name on two patches without replacing the other patch's preset. Older saved presets are treated as 1.00 presets.

Map files with the same internal path can differ between patches. Fellowship uses the selected archive set for both the map image and its starting locations. Archive precedence follows the game's filename ordering: case-insensitive ASCII order, with the first matching archive taking priority. For example, a `#BT…big` overlay takes priority over `_WSMaps.big`, which takes priority over `Maps.big`.

## Joining with a different patch

You can join a Fellowship room even when your installed patch differs. Room chat, player information, and readiness remain available. The room shows a warning explaining the required patch and the detected local patch; the launch flow repeats that warning when needed.

If the installation cannot be verified, Fellowship says so instead of guessing. Detection checks the installed game binary and relevant archive files against local switcher sources or recognized stock signatures. A mixture of patch files, missing dependencies, or an unknown overlay may therefore produce an **Unknown** result.

Before playing:

1. Close BFME2.
2. Use Ecth's Patch Switcher to select the room's exact patch, including its beta build.
3. Let the switch finish, then launch BFME2 from Fellowship.

A successful Fellowship room connection does not make different BFME2 patches compatible with each other. Everyone still needs matching game files and the selected map to play together.

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

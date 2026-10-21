# Automatic native lobby replacements

The feature reads the hash-validated BFME2 1.00 LAN lobby. Automatic replacement and side rules are enabled only for Fellowship's selected 3v3 mode on Rhûn. Actual teams and the hosting player come from the native BFME2 lobby, never from Fellowship room ownership.

## Rhûn sides

The native hosting player's team occupies the right side (Team 2); the other team occupies the left (Team 1). A shared plan preserves each player's vertical position while mirroring sides. Clients first release their old position with a guarded native change, then claim their target after the host observes that occupied positions have been released. Each client changes only its own slot.

At native match start, the app records the actual BFME2 host and lineup. When the next native lobby has a different hosting team, that lineup determines the mirrored positions. Repeated reports do not toggle sides. A different host from the same team leaves that team on the right. Concurrent manual changes cancel a pending plan. History lasts for the Fellowship room; it is not persisted across a full application restart. All participants need the updated app and enabled live sync for coordinated changes.

Replaying a match after a crash or out-of-sync error follows the same hosting-team rule; there is no game counter that automatically flips sides. Explicit native spot selections take priority. Players whose new-lobby spot is Random fall back to their previous match's vertical position, including when the same player hosts again. Known positions are allocated before players without a remembered position, and every target remains unique.

The Fellowship room owner distributes the plan, but is not assumed to host BFME2. A protocol test uses different players for those roles and verifies the plan, positions, and native hosting history on every participant. Other modes and maps retain ordinary saved-preference behavior.

## Native evidence

- Slot +0x04: occupancy state (6 identifies a human connection).
- Slot +0x10: zero-based starting position; -1 means unassigned.
- Slot +0x18: faction; -2 is Observer and -1 is Random.
- Slot +0x1c: zero-based team; -1 means no selected team.
- Slot +0x38: six-byte connection address, already used to identify the local player.

Disassembly of the verified installed game.dat confirms that the faction adapter at 0x843f29 calls 0x7ffb6b, which stores the faction at +0x18. Its Observer branch clears the starting position at +0x10. The native lobby UI at 0x8460d9–0x846107 associates -2 with `GUI:Observer` (0xbeb308); `GUI:Random` is at 0xbeb318. The decoder remains read-only.

## Behavior

The helper requires five readings and two seconds of unchanged native roster/team/spot/faction data. Connecting players initially appearing as observers do not create a vacancy: the algorithm only learns positions actually occupied by active players.

It remembers each occupied starting position's native team and last occupant. An active human with Random position can inherit a known free position for their selected team. Returning players have priority on their own available position; remaining replacements are allocated in connection-address order. Existing positions and manual changes to Random are respected. It never guesses a team for someone with no team selected, changes a faction, or writes another player's slot.

A newly connected observer does not clear saved preferences until that player has been seen active in the native session. Observer selections are imported with their cleared position and are never overwritten by saved active assignments. Shared map/rules sync still runs for a host who observes. The existing countdown, native-session, map-validity, occupied-position and color protections remain. A separate automatic bridge mode rechecks that the local player is not observing, still has Random position, and still has the expected team before changing anything; native color is preserved.

Replacement history is retained while the sync helper remains alive, including subsequent native lobby sessions on the same map/host. A map or native-host change resets learned replacement positions. Restarting Fellowship loses this transient history. A replacement needs a known native team position to inherit.

## Validation

`RotationTests` covers 2/3/4-player team layouts, observer versus Random, temporary joins, team ambiguity, simultaneous replacements, occupied/manual choices, map changes, and recreated/reused native lobby identities. Existing UI and protocol suites cover placement synchronization and multiplayer behavior. These are simulated lobby-state tests, not a completed live multi-PC substitution test.

Run `dotnet run --project ui-smoke/UiSmoke.csproj -c Release -- --rhun-sides` for the focused rotation and side-plan regressions. Recreated-match coverage uses deliberately unsorted remembered positions, every possible hosting player, retained versus Random spots, and explicit new selections during same-team and opposing-team host changes. The protocol suite's `--rhun-sides` scenario checks propagation with a Fellowship room owner who is not the BFME2 host.

# Installer source recovery

The 0.1.66 MSI contains `NOT WIX_DOWNGRADE_DETECTED` with the message “A newer Fellowship is already installed.” Running `/fv` on that product while a newer version is registered can fail its launch conditions.

Recovery now downloads and verifies the exact original MSI, then registers its durable source using Windows Installer's `MsiSourceListSetInfo` (`PackageName` and `LastUsedSource`). It does not execute the old product's repair actions or manually delete installer registrations. New update packages are retained outside disposable staging. The real upgrade still runs through Windows Installer.

Source API reference: https://learn.microsoft.com/en-us/windows/win32/api/msi/nf-msi-msisourcelistsetinfow

The reported remote PC's error 1603 cannot be conclusively diagnosed without its repair log. The release includes a standalone repair helper for restoring sources before running the new MSI on a PC whose old updater cannot get past this failure.

The source-registration probe succeeded against the laptop’s verified 0.1.116 MSI without running repair actions.

External Windows Installer calls now receive resolved physical package and log paths rather than MSIX-virtualized LocalAppData aliases. Every attempt precreates its own log directory and file, addressing error 1622 (failure to open an installation log). Reusing an already retained MSI compares its hash before copying, including when logical and physical paths refer to the same file.

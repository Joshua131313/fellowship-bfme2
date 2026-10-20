# Automatic native lobby replacements

The feature reads the hash-validated BFME2 1.00 LAN lobby. It does not infer teams from Fellowship's Mode, room capacity, or player count.

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

History is retained while the sync helper remains alive, including subsequent native lobby sessions on the same map/host. A map or native-host change resets learned positions. Restarting Fellowship loses this transient history. Initial matches still use existing saved assignments; there must be a known native team position to inherit.

## Validation

`RotationTests` covers 2/3/4-player team layouts, observer versus Random, temporary joins, team ambiguity, simultaneous replacements, occupied/manual choices, map changes, and recreated/reused native lobby identities. Existing UI and protocol suites cover placement synchronization and multiplayer behavior. These are simulated lobby-state tests, not a completed live multi-PC substitution test.

# Installer source recovery

The 0.1.66 MSI contains `NOT WIX_DOWNGRADE_DETECTED` with the message “A newer Fellowship is already installed.” Running `/fv` on that product while a newer version is registered can fail its launch conditions.

Recovery now downloads and verifies the exact original MSI, then registers its durable source using Windows Installer's `MsiSourceListSetInfo` (`PackageName` and `LastUsedSource`). It does not execute the old product's repair actions or manually delete installer registrations. New update packages are retained outside disposable staging. The real upgrade still runs through Windows Installer.

Source API reference: https://learn.microsoft.com/en-us/windows/win32/api/msi/nf-msi-msisourcelistsetinfow

The reported remote PC's error 1603 cannot be conclusively diagnosed without its repair log. The release includes a standalone repair helper for restoring sources before running the new MSI on a PC whose old updater cannot get past this failure.

The source-registration probe succeeded against the laptop’s verified 0.1.116 MSI without running repair actions.

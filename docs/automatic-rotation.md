# Automatic native lobby replacements

The feature reads the hash-validated BFME2 1.00 LAN lobby. Automatic replacement and side rules are enabled only for Fellowship's selected 3v3 mode on Rhûn. Actual teams and the hosting player come from the native BFME2 lobby, never from Fellowship room ownership.

## Rhûn sides

The native hosting player's team occupies the right side (Team 2); the other team occupies the left (Team 1). A shared plan preserves each player's vertical position while mirroring sides. Clients first release their old position with a guarded native change, then claim their target after the host observes that occupied positions have been released. Each client changes only its own slot.

At native match start, the app records the actual BFME2 host and lineup. When the next native lobby has a different hosting team, that lineup determines the mirrored positions. Repeated reports do not toggle sides. A different host from the same team leaves that team on the right. Concurrent manual changes cancel a pending plan. History lasts for the Fellowship room; it is not persisted across a full application restart. All participants need the updated app and enabled live sync for coordinated changes.

Replaying a match after a crash or out-of-sync error follows the same hosting-team rule; there is no game counter that automatically flips sides. Explicit native spot selections take priority. Players whose new-lobby spot is Random fall back to their previous match's vertical position, including when the same player hosts again. Known positions are allocated before players without a remembered position, and every target remains unique.

Assignment starts with the players who have joined; it does not wait for six active players. The plan has revisions within the same native lobby, so late arrivals can be assigned after the current clear phase completes. Departing players and observers release their reservations. Continuing players retain their current selections, including a deliberate change to Random. Arrivals seen during the clear phase retain their chosen relationship to the host's team even if the native team labels change while clearing.

Substitutes can choose any available position. A player can give up a position to a substitute and select another one; a remembered position is a fallback, not a reservation or a returning-player entitlement. Manual edits during an unfinished plan cancel its writes and import the native layout atomically, avoiding conflicts with the cancelled reservations. If a historical team cannot be identified unambiguously, or a side contains more than three active players, the app keeps the current layout instead of guessing or partially publishing a plan.

The Fellowship room owner distributes the plan, but is not assumed to host BFME2. A protocol test uses different players for those roles and verifies the plan, positions, and native hosting history on every participant. Other modes and maps retain ordinary saved-preference behavior.

## Personal sides in mixed lobbies

For Rhûn 3v3, a Fellowship client can also mirror its own spot when the native game host or other participants do not run Fellowship. The native host's team belongs on the right; the opposing team belongs on the left. Native team numbers are retained, and each helper writes only its own row. Once native teams are known, same-team host changes preserve the side instead of toggling it.

Mirroring pairs positions by their vertical order in the actual map metadata: top with top, middle with middle, and bottom with bottom. A fresh lobby with Random position can restore the remembered vertical position. New explicit spot choices replace that preference; a deliberate Random choice within the current lobby stays Random. Explicit Fellowship team edits remain available after the initial native team is established.

At native match start, the helper remembers a complete, unambiguous six-human 3+3 lineup. In a later lobby in the same room, that lineup can establish whether the local player and host were teammates while current teams remain unset. Current native teams and deliberate team edits override this fallback. Contradictory lineups, unknown hosts, observers, and ambiguous identities defer the decision. The status explains when last game's teams are being used. No completed-game counter or alternating-host assumption is involved, and restarting Fellowship clears this transient history.

An occupied counterpart stays pending. The helper does not pick another vertical seat or clear an outsider. Two known Fellowship clients exchanging complementary seats may each clear their own spot before claiming the released counterpart. The intermediate Random echo does not erase the remembered vertical position. Countdown/session guards still stop native writes during a match.

`PersonalRhunSideTests` covers personal mirroring and its transitions. Run `dotnet run --project ui-smoke/UiSmoke.csproj -c Release -- --personal-sides` for these cases plus the existing external-host and coordinated Rhûn regressions. These are simulated native-lobby tests; live multi-PC behavior is not claimed as tested.

## Native evidence

- Slot +0x04: occupancy state (6 identifies a human connection).
- Slot +0x10: zero-based starting position; -1 means unassigned.
- Slot +0x18: faction; -2 is Observer and -1 is Random.
- Slot +0x1c: zero-based team; -1 means no selected team.
- Slot +0x38: six-byte connection address, already used to identify the local player.

Disassembly of the verified installed game.dat confirms that the faction adapter at 0x843f29 calls 0x7ffb6b, which stores the faction at +0x18. Its Observer branch clears the starting position at +0x10. The native lobby UI at 0x8460d9–0x846107 associates -2 with `GUI:Observer` (0xbeb308); `GUI:Random` is at 0xbeb318. The decoder remains read-only.

## Behavior

The helper requires five readings and two seconds of unchanged native roster/team/spot/faction data. Connecting players initially appearing as observers do not create a vacancy: the algorithm only learns positions actually occupied by active players.

It remembers each occupied starting position's native team. An active human with Random position can inherit a known free position for their selected team. Random replacements are allocated in connection-address order, with no priority for former occupants. Existing positions and manual changes to Random are respected. It never guesses a team for someone with no team selected, changes a faction, or writes another player's slot.

A newly connected observer does not clear saved preferences until that player has been seen active in the native session. Observer selections are imported with their cleared position and are never overwritten by saved active assignments. Shared map/rules sync still runs for a host who observes. The existing countdown, native-session, map-validity, occupied-position and color protections remain. A separate automatic bridge mode rechecks that the local player is not observing, still has Random position, and still has the expected team before changing anything; native color is preserved.

Replacement history is retained while the sync helper remains alive, including subsequent native lobby sessions on the same map/host. A map or native-host change resets learned replacement positions. Restarting Fellowship loses this transient history. A replacement needs a known native team position to inherit.

## Validation

`RotationTests` covers 2/3/4-player team layouts, observer versus Random, temporary joins, team ambiguity, simultaneous replacements, occupied/manual choices, map changes, and recreated/reused native lobby identities. Existing UI and protocol suites cover placement synchronization and multiplayer behavior. These are simulated lobby-state tests, not a completed live multi-PC substitution test.

Run `dotnet run --project ui-smoke/UiSmoke.csproj -c Release -- --rhun-sides` for the focused rotation and side-plan regressions. Recreated-match coverage uses deliberately unsorted remembered positions, every possible hosting player, retained versus Random spots, and explicit new selections during same-team and opposing-team host changes. The protocol suite's `--rhun-sides` scenario checks propagation with a Fellowship room owner who is not the BFME2 host.

`RhunJoinTests` covers incremental joins during clear and claim, departed observers, explicit spot exchanges, returning players without priority, manual Random choices, native team-label changes, cancellation imports, occupied reservations, invalid/duplicate positions, oversubscribed teams, and frozen plans after match start.

# Installer source recovery

The 0.1.66 MSI contains `NOT WIX_DOWNGRADE_DETECTED` with the message “A newer Fellowship is already installed.” Running `/fv` on that product while a newer version is registered can fail its launch conditions.

Recovery now downloads and verifies the exact original MSI, then registers its durable source using Windows Installer's `MsiSourceListSetInfo` (`PackageName` and `LastUsedSource`). It does not execute the old product's repair actions or manually delete installer registrations. New update packages are retained outside disposable staging. The real upgrade still runs through Windows Installer.

Source API reference: https://learn.microsoft.com/en-us/windows/win32/api/msi/nf-msi-msisourcelistsetinfow

The reported remote PC's error 1603 cannot be conclusively diagnosed without its repair log. The release includes a standalone repair helper for restoring sources before running the new MSI on a PC whose old updater cannot get past this failure.

The source-registration probe succeeded against the laptop’s verified 0.1.116 MSI without running repair actions.

External Windows Installer calls now receive resolved physical package and log paths rather than MSIX-virtualized LocalAppData aliases. Every attempt precreates its own log directory and file, addressing error 1622 (failure to open an installation log). Reusing an already retained MSI compares its hash before copying, including when logical and physical paths refer to the same file.

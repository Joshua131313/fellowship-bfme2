# Local BFME2 / GameRanger inspection

Read-only inspection performed on 2026-10-17. No installed files were changed and no proprietary game/client binaries are included in this repository.

## Verified observations

- BFME2 is installed under `C:\Program Files (x86)\Electronic Arts\The Battle for Middle-earth (tm) II`.
- `lotrbfme2.lcf` directs the local launcher to `game.dat`.
- Installed `game.dat` reports file version `1.0.2194.40862`.
- The installed patch switcher has separate `100`, `106`, and `109*` directories; the `100` directory contains its own game executable and INI/map archives.
- The user's `Options.ini` selects a 26.x IPv4 address and contains `FirewallPortOverride = 8288`. This is evidence of existing interface/port configuration, not proof of end-to-end connectivity.
- `NetworkPref.ini` stores the player name, faction and color preferences. The examined file did not contain a ready-to-use LAN host setup.
- `Skirmish.ini` has profile-specific serialized `GameInfo` entries, map checksums and map paths, including `maps\map wor rhun\map wor rhun.map`. These are skirmish records; copying them into LAN configuration is unverified.
- Targeted executable string inspection found `LANGameInfo`, `StartLobby`, `DoCreateGame`, `DoJoinGame`, `-automatch`, `-resumeGame`, `-file`, `-noshellmap` and normal window/mod switches. Finding a string does not establish that it is an external integration API or document its semantics.
- Read the BIG archive directory and inspected network-related lines in `gamedata.ini` and `multiplayer.ini`; this did not establish a supported LAN automation entry point.
- GameRanger includes `GameRanger.dll`, `GameRangerLaunch.dll`, and launch-event/shared-memory identifiers. This is consistent with specialized integration; it does not establish the behavior of its BFME2 launch implementation.

## Engineering conclusion

A persistent, independent TCP launcher room can coordinate player consent, send launch signals, share setup choices, and allow users to relaunch or reconnect without tying room lifetime to the game process. That behavior is implemented.

Automatic LAN setup and active-match recovery remain separate, unverified engine-integration tasks. Do not assume that `-automatch`, `-resumeGame`, or Skirmish.ini editing achieves either one. The next phase needs controlled in-game testing against BFME2 1.00 and a validated integration method, followed by a real two-PC LAN test.

## 0.1.3 host preset

Native Rts:Map and Rts:UseSystemMapDir preferences select the built-in Rhûn map. The original NetworkPref.ini is backed up under LocalAppData/Fellowship before updating it. A live launch confirmed Rhûn, 4000 resources and 2X in the host lobby (user verified without changing controls).

The helper launches lotrbfme2.exe and applies session-only default values to the resulting game.dat process. It requires SHA-256 D79265812638D141C4F02E5924B03100D3F69EACA10A9379CFE14B074FBAF716 and checks descriptor IDs, choice pointers, counts, initial defaults and selected values before writing. Choice defaults use RVAs 0x867c94 (index 3 → 200%) and 0x867cfc (index 10 → 4000). Boolean defaults use 0x867c24 (Custom Heroes) and 0x867c2c (Ring Heroes), both zero. Values are read back, page protection is restored, and failures attempt to roll back all four defaults. No executable file is patched. The helper exits after applying defaults; the network-facing app remains unelevated.

This does not navigate menus, assign teams, join guests to the native lobby, or restore an ongoing match. Earlier integration conclusions above describe versions through 0.1.2.
Final verification: build and protocol/profile checks pass. Rhun/4000/2X were confirmed in-game by the user. Hero descriptor values are validated against the installed binary; final visual verification of both disabled hero options remains pending because the user reopened BFME2 into another player's lobby. Fellowship main window was visually verified running with the VPN listener ready.


## 0.1.5 validation

Typed map/rules, UDP discovery, private-room filtering/join restrictions, roster updates, privacy changes, port recovery and existing protocol checks pass on local loopback peers. Installed map archives yield 240 multiplayer entries; no proprietary map data is shipped. WPF browser/editor resources and icons rendered in an offscreen smoke test.

The generalized preset helper launched lotrbfme2.exe successfully and validated runtime writes for 4000 / 2x / both hero flags off. BFME2 showed the main menu but did not respond to the authorized computer-use mouse click or keyboard attempt. Automatic menu navigation/native host creation/native guest joining remain unimplemented; no verified CLI entry point was found. Cross-PC Radmin broadcast discovery and alternate rule visual confirmation remain user-test items.


## 0.1.6 navigation investigation

Read-only process-token inspection confirmed the test BFME2 process is elevated. The normal computer-use mouse and keyboard attempts still did not activate controls. Elevated status explains why lower-integrity input may be blocked; direct input success is not yet verified. Static command-parser inspection found that the early -automatch comparison result is discarded, so its presence is not a demonstrated LAN launch API. MainMenu.apt/.const contains LocalNetworkButton and a telescoping Multiplayer menu.

An opt-in product helper now contains window/focus/format checks, stable menu fingerprint recognition, paced mouse input, one action per recognized state, Escape cancellation and deadlines. Live input has not been tested through this helper. Captured main, Network, and host setup frames classify correctly at 2048, 2560 and 5120 pixels in offline tests. Blank frames are rejected. Native guest-room selection remains manual. Full screenshots remain local research files and are excluded from distribution; only menu-label fingerprints are embedded.

Latest live trial confirmed Main -> Multiplayer -> Network with elevated helper input. Network label difference was 19.8, above the earlier 16 cutoff, preventing Create Game. Network and HostLobby now accept differences below 24 while retaining an eight-point separation from other labels; Main/Multiplayer remain below 16. Offline recognition checks pass at all three tested resolutions. Final host creation still needs live confirmation. Guest name matching and double-click joining are deferred at the user's request.

User confirmed host navigation works. Responsiveness update: poll every 100 ms instead of 250 ms, reduce post-click pause from 500 to 150 ms, cursor settling from 120 to 60 ms. Preserve three matching frames before acting, 120 ms mouse-down hold, loading-screen gating, focus checks and timeout limits. Faster timings still need live user confirmation.

## Direct LAN placement investigation (0.1.11)

Cursor navigation and placement helpers have been removed at the user's request. No native team/spot write has been executed by this build. Read-only live snapshots confirm local slot identification, current map, team and spot values.

For the exact SHA-256 above, preferred image base 0x400000: MP setup singleton at 0xdfe1f4 has an adapter pointer at +0x58. The LAN adapter vtable is 0xc3a9a8. Its +0x34 method (0x843374) sets team; +0x30 (0x84361b) sets starting position. Both are thiscall methods taking slot pointer and zero-based choice, with ret 8. Team writes slot +0x1c. Spot writes BOTH +0x10 and +0x14. Native methods also reset readiness/countdown, broadcast host state or send guest option requests (Team=%d / StartPos=%d), and flag host UI refresh. Therefore a raw field write alone is insufficient.

LAN subsystem global 0xdf9800: +0x44 active game, +0x48 local address. Game slots are eight entries starting at +0xb0, stride 0x1d0; state +4, ready +8, IP/port +0x38. Local matching must use LAN address, since game and Fellowship names may differ. Human state is 6; no selection is -1. Team 2 / Rhun middle-left was observed as native team 1 / spot 1.

Remaining: execute the native methods on the correct game thread while the LAN lobby is active; validate binary, adapter, local slot, map, occupancy and bounds; verify resulting state and host/guest synchronization on two PCs. No blind remote-thread call, memory write, spoofed network message or cursor fallback is implemented. These findings establish a candidate integration path, not working automatic assignment.

## 0.1.12 native assignment implementation and live verification

An x86 helper uses a thread-specific WH_GETMESSAGE hook on BFME2's engine window (class E99E8455-CC9B-488a-BA22-0E8A8F74F9FA), not the separate Mod Framework window. A private registered message schedules the checked native setters on that engine thread; it is not a keyboard/mouse/menu action. The hook is removed after one bounded request. The helper validates the executable SHA-256; the callback validates the active LAN object, setup adapter, local slot/address, map, bounds and occupancy before mutation. It rejects pending setup transitions. The native valid-spot routine at 0x83c791 checks the map's player count. Native setters reset readiness and dispatch their normal LAN updates. The application verifies stable readback and reports failure without claiming success. Background waiting is bounded and cancelled when the initiating app exits or its room/choices change.

Verified live on the installed 1.00 host: Team 3 / spot 2 -> Team 4 / spot 1 -> Team 3 / spot 2, including an independent read-only process snapshot and the final self-contained Fellowship executable. No game file was edited and no navigation was performed. Read-only guard trials rejected wrong map, stale lobby identity, non-local player, team index 4 and spot index 8. Unit checks cover the 568-byte command ABI and zero-based/unset encoding. Protocol and WPF smoke checks pass. Native guest-to-host synchronization, occupied human slots and a real two-PC launch still need multiplayer validation.

MSIX file virtualization initially caused the extracted DLL path to be invisible to BFME2. GetFinalPathNameByHandle now resolves both helper and request files before crossing process boundaries. No ACL or security-policy change was needed.

Windows hook requirements and thread semantics: https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowshookexw and https://devblogs.microsoft.com/oldnewthing/20180926-00/?p=99825 .

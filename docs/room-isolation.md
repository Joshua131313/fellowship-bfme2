# Experimental room isolation (0.1.37)

Enable **Settings → Isolate new rooms (experimental)** before hosting. It applies to new Fellowship rooms, and the host's room policy applies to joining clients even if their own new-room setting is off. Isolated rooms reject clients predating the capability handshake. Launch BFME2 through Fellowship, approve the helper, and wait for **Room isolation active** before opening Network.

The supported 1.00 executable uses the WSOCK32 `sendto` and `recvfrom` imports for its LAN UDP wrapper. Read-only inspection found the send wrapper at 0x992fea and receive wrapper at 0x992d66. The receive wrapper treats WSAEWOULDBLOCK as an empty poll. The opt-in bridge redirects the game's import entries at 0xbb7934 and 0xbb7988 after the executable hash check. It pins its own module until game exit so function pointers cannot outlive their code.

Outgoing limited/subnet broadcasts become unchanged UDP payloads sent to current room addresses. Other outbound UDP destinations are dropped. Incoming UDP packets from outside the roster are consumed and reported as an empty poll. Roster updates also run while game-setting synchronization is disabled or paused for a match. This only changes BFME2's process: Fellowship's TCP chat/room connections, UDP discovery, Radmin configuration and system firewall are untouched.

## Rollback and limitations

- Isolation is **off by default** pending two-PC validation. It is an experimental trusted-VPN routing feature, not cryptographic authentication or protection against address spoofing.
- To roll back: exit BFME2, leave the Fellowship room, turn the new-room setting off, and create a new room. Restarting BFME2 removes the in-memory hooks. No game files, firewall rules or VPN settings require restoration.
- If Fellowship/helper disappears, the last allowlist remains until BFME2 exits. This avoids silently exposing the running lobby, but disconnected peers may remain in that local list. Other connected clients continue enforcing their current rosters.
- Restart BFME2 when switching rooms or testing isolated versus non-isolated operation. Native cached lobby entries may need time to expire; visibility, directed discovery compatibility, and actual matches require the two-PC test below.
- This filters the verified LAN UDP path. It does not promise isolation for unrelated Internet modes, other versions or modified game binaries.
- Fellowship room capacity is separately configurable from 2 to 64. BFME2 still supports at most eight match players. Additional Fellowship room members do not create additional BFME2 slots.

## Two-PC verification

1. Install 0.1.37 on both PCs and close BFME2 on both.
2. On PC A enable isolation and create a Fellowship room. PC B joins that same room.
3. Launch BFME2 through Fellowship on both. Wait for the isolation-active status. A creates a native Network lobby; B should see and join it. Verify chat, settings sync and a short match.
4. Exit both games. Leave the Fellowship rooms. Enable isolation on B too and create separate Fellowship rooms on A and B.
5. Launch both games again. A creates a native lobby; B should not see it or join it. Fellowship's public room browser and private messages should still work.
6. Test rollback using the steps above, then confirm ordinary native LAN discovery returns.

Automated validation covers native routing functions with injected send/receive functions, broadcast fanout, allow/deny behavior, disabled passthrough, protocol ABI, address encoding, roster limits, ten-client room state, legacy-client rejection, and unaffected Fellowship discovery/private chat. These tests do not replace an actual two-PC BFME2 traffic and gameplay test.

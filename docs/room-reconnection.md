# Room reconnection

A temporary network interruption keeps you in the same Fellowship room while the app tries to reconnect to its original host. Losing contact with the host does not make you the host or start an automatic ownership election.

## When a connection drops

The room shows **Reconnecting**. Your local room identity, player identity, and selected team, map spot, and color are retained while Fellowship retries the original host automatically. The displayed roster may reflect the last received room state until the connection returns.

Launching and native lobby synchronization pause while the room connection is unavailable. Fellowship does not send new game selections based on an unconfirmed connection. Once the room connection is restored, the app receives the host's current room state and can resume normal room actions.

If the host is still running, it holds the disconnected member's place for up to **120 seconds**. That reservation keeps a brief interruption from immediately removing the player or allowing someone else to take their place. The host waits for disconnected participants rather than treating them as connected and ready to launch.

Restore the VPN or local network connection and leave the room open to let the automatic retry continue. Choosing to leave the room stops that attempt; you can then join a different room normally.

## Ownership stays with the original host

Fellowship distinguishes a deliberate handoff from a connection failure:

| Event | Room ownership |
| --- | --- |
| A guest temporarily loses its network connection | The original host keeps ownership; the guest reconnects to it. |
| Guests can see each other but cannot reach the host | No guest is promoted automatically. The app continues trying the original host. |
| The host explicitly promotes another participant | The confirmed handoff transfers ownership to that participant. |
| The host leaves normally and hands off the room | The confirmed handoff transfers ownership to the next host. |
| The host crashes or its computer shuts down unexpectedly | The app does not assign ownership to another guest. Players can leave and join or create a fresh room. |

This prevents a brief interruption from producing two competing owners of the same room. It also keeps the **Fellowship room owner** separate from the **BFME2 lobby host**; room reconnection does not change the in-game hosting team or trigger a Rhûn side change.

## If reconnection does not finish

1. Check that the original host's Fellowship app is still running and hosting the same room.
2. Confirm that your VPN is connected and Fellowship is using the intended network interface and Radmin group.
3. Restore the connection before the host's 120-second reservation expires where possible.
4. If the host has crashed or the reservation has expired, leave the disconnected room and rejoin a reachable room, or create a fresh room for the group.

The reservation applies to the Fellowship room connection. It does not restore a crashed BFME2 match or rejoin an army in an already-running game. Fellowship does not start or close BFME2 merely because a room connection is interrupted.

[Return to the README](../README.md).

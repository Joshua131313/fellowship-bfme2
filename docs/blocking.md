# Blocking and friends

Right-click a player in a Fellowship room or the active-player list to add/remove a friend, send a message, or block them. Remove and Block use red hover feedback. Friends also have a Block/Unblock context action.

Manage blocks under Settings > Blocked players or Friends > Blocked players. Blocks persist across restarts. A blocked VPN address cannot query your identity/room, join or resume your hosted room, or send you direct messages. Discovery replies to that address are suppressed. Outgoing DMs and joins to blocked addresses are also refused.

Blocking a current guest removes them. Blocking a player in a shared room while you are a guest leaves that room; joining a third-party room containing a blocked player also disconnects you. Blocking does not change Radmin or BFME2 network traffic. This is an address-based application feature, not authenticated account banning: a changed VPN address needs a new block.

Native post-match synchronization retains the countdown guard, waits for a verified room closure and stable new lobby, then resets setup and placement reconciliation. Fellowship's selected rules/team/spot/color are reapplied before subsequent native changes are imported. Occupied spots/colors remain protected rather than overwriting another player.

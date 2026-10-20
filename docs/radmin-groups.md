# Radmin group selection

Select a Radmin network interface, then choose **Radmin group** in the room browser or Settings. **All Radmin groups** preserves the original adapter-wide behavior. The saved group preference survives restarting Fellowship.

![Fellowship Radmin group selector with a saved group and filtered player scope](screenshots/radmin-group.png)

## Automatic reader

Radmin exposes its network names and displayed member IPv4 addresses through Windows UI Automation. Fellowship reads the current Windows session's `RvRvpnGui.exe` main window, identified by its `MainWindow` automation ID. It matches the selected network's `NetworkName` and reads addresses from that group's `NodesContainer` only.

For a collapsed group, the reader invokes only its `NetworkWidget.Triang` expand control and restores its original state in `finally`. It does not activate the window, move the mouse, invoke power/connect/disconnect actions, load Radmin DLLs, read passwords, or modify Radmin configuration. The supported Radmin layout was verified against the installed client: six groups were detected; a collapsed group returned 85 members and was restored to collapsed. Names and counts can change as Radmin updates.

A separate Fellowship process performs the accessibility read. The parent dashboard limits it to 20 seconds so an unresponsive external provider cannot freeze Fellowship. Membership is refreshed every 25 seconds while Radmin is selected. If a read fails, the last successful list is usable for at most two minutes. After that, new out-of-scope/unknown connections remain blocked. A fresh app launch requires a fresh successful read. The app never broadens a selected group to all groups on failure.

The reader depends on Radmin's accessible UI layout. Radmin must remain running. If data cannot be read, open its main window and use the refresh button beside the selector. Members hidden by Radmin's own display settings may not be exposed by its UI; newly exposed or online members become available on the next successful refresh.

## What is filtered

- Room and presence discovery sends unicast probes only to the selected group's known members, rather than adapter-wide broadcasts.
- Inbound UDP and TCP requests from other members are rejected before processing.
- Outgoing discovery, direct-IP joins, messages, invitations, room-owner resolution, and host handoffs use the same membership policy.
- Player lists, room previews, activity alerts, and ping refreshes follow that scope.
- A room containing participants outside the selected group is not joined or retained. Changing the selected group first leaves the current Fellowship room.

The local user's address remains allowed. A peer belonging to several Radmin groups remains eligible if it is a member of the selected group; membership does not identify which underlying Radmin tunnel carried a packet.

This is Fellowship application filtering. It does not disconnect any Radmin group, change Windows routes or firewall rules, or filter BFME2's native LAN traffic or other programs. It is not a separate VPN isolation/security boundary.

## Validation

The protocol checks use three loopback peers to verify permitted membership, incoming and outgoing messages, direct-IP joins, UDP discovery without unrelated broadcasts, removal from a roster, expired/missing data, saved preferences, and non-Radmin adapters. UI checks verify selected group labels and filtered online player rows. The real-client probe verifies group reading and restored expansion state. Live group discovery should also be checked with updated Fellowship clients on separate PCs.

# Radmin group selection

In the room browser, open **Server** and hover over **Radmin VPN**. Its side menu lists the actual Radmin groups, opening to the right in English, French, Spanish and Turkish, and to the left in Arabic. Select a subgroup to enable it and disable every other joined group in Radmin VPN itself. Click the parent **Radmin VPN** item, or choose **All Radmin groups** in Settings, to enable all joined groups again. Groups are never left or deleted. The saved Fellowship group preference survives restarting the app.

![Fellowship Radmin group selector with a saved group and filtered player scope](screenshots/radmin-group.png)

## Automatic reader

Radmin exposes its network names and displayed member IPv4 addresses through Windows UI Automation. Fellowship reads the current Windows session's `RvRvpnGui.exe` main window, identified by its `MainWindow` automation ID. It matches the selected network's `NetworkName` and reads addresses from that group's `NodesContainer` only.

For a collapsed group, the reader invokes only its `NetworkWidget.Triang` expand control and restores its original state in `finally`. Manual refresh and periodic membership reads never invoke connection commands. They do not load Radmin DLLs or read passwords. The supported Radmin layout was verified against the installed client. Names and counts can change as Radmin updates.

## Changing actual connections

Only explicit selection runs the connection controller, in a separate process with a timeout. It temporarily exposes the native group headers, reads each group's Enable/Disable Network command, enables the selected group first, then disables the others. It verifies each change and the final enabled set before saving the new selection. Clicking the selected subgroup again reapplies the connection state if it was changed manually in Radmin.

The controller accepts exact connection-action captions from Radmin's 91 installed translation catalogs. It never chooses actions by menu position; neighboring Leave and Delete actions are rejected. Qt accessibility invokes the connection action, then the controller dismisses its menu explicitly. Group expansion, scrolling and main-window visibility are restored afterward. No global mouse input, main power switch, credentials, routes or firewall rules are changed.

All connection commands are checked before mutation. On failure, the controller attempts to restore the previous enabled states. If restoration or the external provider times out, Fellowship displays an error asking you to check Radmin and retry, and does not save a successful selection. Keep the Radmin main window available and close any open Radmin menu before selecting a group. Refreshing the menu stays open and never reconnects or disconnects groups.

A separate Fellowship process performs the accessibility read. The parent dashboard limits it to 20 seconds so an unresponsive external provider cannot freeze Fellowship. Membership is refreshed every 25 seconds while Radmin is selected. If a read fails, the last successful list is usable for at most two minutes. After that, new out-of-scope/unknown connections remain blocked. A fresh app launch requires a fresh successful read. The app never broadens a selected group to all groups on failure.

The reader depends on Radmin's accessible UI layout. Radmin must remain running. If data cannot be read, open its main window and choose **Refresh Radmin groups** in the side menu or use the refresh button in Settings. Members hidden by Radmin's own display settings may not be exposed by its UI; newly exposed or online members become available on the next successful refresh.

## What is filtered

- Room and presence discovery sends unicast probes only to the selected group's known members, rather than adapter-wide broadcasts.
- Inbound UDP and TCP requests from other members are rejected before processing.
- Outgoing discovery, direct-IP joins, messages, invitations, room-owner resolution, and host handoffs use the same membership policy.
- Player lists, room previews, activity alerts, and ping refreshes follow that scope.
- A room containing participants outside the selected group is not joined or retained. Changing the selected group first leaves the current Fellowship room.

The local user's address remains allowed. A peer belonging to several Radmin groups remains eligible if it is a member of the selected group; membership does not identify which underlying Radmin tunnel carried a packet.

The membership policy also filters Fellowship's own traffic. Disabling other Radmin groups affects BFME2 and other applications using those connections. A player shared with the enabled group remains reachable. This is group connection selection, not room-specific packet isolation or a separate security boundary.

## Validation

The protocol checks use three loopback peers to verify permitted membership, incoming and outgoing messages, direct-IP joins, UDP discovery without unrelated broadcasts, removal from a roster, expired/missing data, saved preferences, and non-Radmin adapters. Connection tests verify target-first selection, switching groups, reconnecting all, idempotence, missing/duplicate group rejection, preflight failure, rollback, state verification, localized action recognition and rejection of Leave/Delete. A real-client probe checks exclusive selection and reconnection across the installed groups. Live game discovery should also be checked with updated Fellowship clients on separate PCs.

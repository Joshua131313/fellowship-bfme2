# Room isolation removed

Experimental isolation was removed in 0.1.38 because it delayed discovery and prevented joining on some PCs. Fellowship no longer filters BFME2 network traffic. Rooms and chat continue over Radmin VPN.

Radmin group selection scopes Fellowship’s own traffic. Manage actual VPN connections in Radmin. The connection-menu automation from 0.1.126 was removed in 0.1.127. See [Radmin group selection](radmin-groups.md).

After upgrading from 0.1.37, fully quit BFME2 on every PC, update Fellowship, and reopen the game. Disabling the old checkbox alone does not unload its hooks.

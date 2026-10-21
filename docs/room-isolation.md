# Room isolation removed

Experimental isolation was removed in 0.1.38 because it delayed discovery and prevented joining on some PCs. Fellowship no longer filters BFME2 network traffic. Rooms and chat continue over Radmin VPN.

Starting with 0.1.126, explicitly selecting a Radmin subgroup enables that group and disables other joined Radmin groups. This changes actual VPN connections; it does not restore the removed room-isolation hooks. See [Radmin group selection](radmin-groups.md).

After upgrading from 0.1.37, fully quit BFME2 on every PC, update Fellowship, and reopen the game. Disabling the old checkbox alone does not unload its hooks.

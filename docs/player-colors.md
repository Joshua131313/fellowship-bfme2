# BFME2 1.00 player colors

Fellowship 0.1.36 adds color to the existing native lobby selection channel. Public values are 0 (Random), then 1–10 in `PlayerColors.All`. Native values are -1, then 0–9. The palette is from `INI.big/data/ini/multiplayer.ini`, using `RGBColor` in declaration order. No original game archive is distributed.

For the already hash-validated 1.00 executable, the native color field is slot + 0x0c. Adapter vtable 0xc3a9a8 + 0x14 resolves to 0x843e18. This callback stores the color, broadcasts host state or submits `Color=%d` to the host for a guest, and updates local preferences. The native color list stores -1 for Random and the palette index for fixed choices. The bridge invokes the callback on the game thread; it does not simulate input.

Bridge ABI v3 appends color at byte 1100, for a total of 1104 bytes. It uses a new message name and DLL filename. Existing binary, local-player, map, lobby-generation and countdown guards still apply. Occupied fixed colors are rejected before writes, including native human and AI players; Random can be shared. Fellowship serializes claims at the host and preserves readiness. A rejected native choice is read back rather than continually retried.

Automated checks cover ABI conversion, color bounds, simultaneous claims, broadcasts, readiness, persistence, native collision resolution, and color-only reconciliation in both directions. A live BFME2 run and two-PC verification of the color callback remain outstanding for this release.

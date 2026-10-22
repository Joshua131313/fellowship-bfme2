# Delay fix and borderless mode

Open **Settings → Game installation** and use **Delay fix + borderless**. The switch manages the two supplied 32-bit DLLs as one package in the folder containing the selected `lotrbfme2.exe`:

- `d3d9.dll`
- `dinput8.dll`

Close BFME2 before enabling or disabling. No game process is stopped automatically. Windows may request permission when the game folder is protected.

Enabled means both files match the bundled pair. Disabling removes that pair and restores any previous DLLs. Originals are retained under `.fellowship-enhancements/originals` inside the game folder. Unrelated files, including an existing `config.json`, are left unchanged.

If only part of the pair is present, the switch shows an indeterminate state. Switch it off to restore a consistent disabled state, then on again to repair. Fellowship blocks its own game launch while a known partial installation or interrupted change needs recovery. It does not overwrite managed files that someone has subsequently replaced outside Fellowship.

Both files are staged and verified before installation. Failed operations roll back, and a durable journal preserves recovery information if that rollback is interrupted. The next explicit change attempts recovery first. The state follows the selected game folder and is not imported from a settings backup.

The bundled payloads have these SHA-256 hashes:

| File | SHA-256 |
| --- | --- |
| `d3d9.dll` | `73BA5238B1A530139EBEEE3DEE2B5E64C9AA6D9F43100D9307B0541D50BDC9EF` |
| `dinput8.dll` | `09C63CFD93D2A17B8C5D0D3652D390347B2B1045112BF5B7FCF5A15D34615551` |

`--game-enhancements` and `--game-enhancements-ui` in the UI smoke runner test installation, restoration, partial files, conflicts, locked destinations, rollback, damaged recovery data, running-game guards, path changes, and light/dark/Arabic layouts. The tests use isolated temporary game folders; they never load or execute either DLL. In-game behavior of the supplied DLLs is not covered by those tests.

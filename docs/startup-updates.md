# Automatic startup updates

Normal launches check for a newer published release before onboarding, the dashboard, or room connections start. The compact startup screen shows an animated Fellowship emblem, download progress, and package verification. A verified update proceeds automatically; no Download confirmation is required.

The initial check has a six-second deadline. Offline or unavailable metadata opens the current app. During checking or downloading, **Open Fellowship** cancels the attempt and opens the current version. A failed download or handoff offers **Retry** and **Open Fellowship**. Closing the startup window exits Fellowship. Downloads retain the existing SHA-256, package size, executable version, and trusted GitHub checks.

The installed app hands the verified MSI to the existing installer worker, with source recovery, logs, automatic handling of a busy Windows Installer, and Retry. It checks the installed version before reopening. Installer failure also offers **Open Fellowship** when an executable remains available.

An update relaunch skips the startup check once, avoiding a repeated check or failure loop. Windows sign-in launches check quietly when current; when an update exists they show progress without taking focus and preserve tray startup afterward. Explicit activation or opening the current app promotes the launch to the foreground. A second Fellowship process activates the existing startup screen rather than running a second updater.

Portable startup updates select the ZIP package and launch its verified executable. They do not silently choose an MSI-only release. The ZIP is retained so an old portable shortcut can reuse the downloaded version after checking its release digest, executable version and archived contents, rather than repeatedly downloading it. The original portable executable is not replaced; using the downloaded updated executable directly also works offline.

Repair tools and native, enhancement, installer, and preset workers bypass startup updating. Background checks while the dashboard is already open remain notices; they do not automatically close rooms or restart the app. The dashboard shares the recent startup-check timestamp to avoid immediately requesting the same metadata again.

`--startup-updates` exercises the coordinator with simulated check/download/handoff functions, including latest/offline, retries, cancellation, stale progress, portable assets, and tray relaunch arguments. `--startup-update-ui` renders checking, downloading, and failure states in light/dark/black themes and Arabic. Those fixtures do not run a real self-update; release installation is verified separately.

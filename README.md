# The Great Big Rebalancing

The Great Big Rebalancing, or TGBR, is a balance and compatibility mod for **Nuclear Option 0.34**. It tones down oppressive equipment, adds new aircraft options, restores a few older mechanics, and expands supported mod aircraft.

See the short [release notes](CHANGELOG.md) or the [detailed user-facing changes](DETAILED_CHANGES.md).

## Installation

1. Install BepInEx 5.4.23.4 for Nuclear Option.
2. Download `TheGreatBigRebalancing.dll` from the latest release.
3. Place it in `Nuclear Option\BepInEx\plugins\TheGreatBigRebalancing\`.
4. Launch the game normally.

Every player, host, and dedicated server in a multiplayer session must use the same TGBR release.

## Highlights

- Removes the AAM-36 Scimitar from the game.
- Restores the targeted/red radar-warning indication from before update 0.34.
- Restores the T/A-30 Compass to its lighter pre-0.34 dry mass while keeping its current thrust.
- Reworks the AB-4 into the less oppressive `AB-4 "Buford"`, leaving the IRM-S2 as its only air-to-air missile.
- Adds long-range high-altitude contrails with a gradual fade-in between 6,000 m and 7,500 m.

## Optional aircraft-mod changes

These features appear only when their source aircraft mods are already installed. TGBR does not load Aryx or Project 2082 content by itself.

- **Aryx F-99 Shrike:** new outer IRM-S2 pylons and a two-position centerline mount.
- **Aryx FS-41 Eclipse:** dedicated targeting-pod and drop-tank stations, two new outboard MMR-S3 stations, and revised wing-store limits.
- **F-16VX Viper II:** a separate `$70m` King Viper variant with more capacitor capacity, more flares, dedicated electronic-warfare pods, NATO-facing weapon names, and a six-bomb GBU-38 rack.
- **Project 2082 FS-3 Ternion:** a revised and consistently enforced pylon/loadout configuration.

## Multiplayer compatibility

Current source builds use matchmaking protocol `tgbr-v58`. Vanilla clients and clients using a different TGBR version cannot join a TGBR server.

## This Project is not official, affiliated with, or endorsed by, Shockfront Studios.
Copyright © 2026 Shockfront Studios. Shockfront Studios and Nuclear Option are trademarks or registered trademarks of Shockfront Studios in the U.S. and/or other countries. All other trademarks are the property of their respective owners.

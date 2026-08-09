# The Great Big Rebalancing

The Great Big Rebalancing, or TGBR, is a balance and utility mod for **Nuclear Option 0.34**. It tones down oppressive equipment, adds new aircraft options, restores a few older mechanics, and provides tools for editing aircraft pylons.

See [CHANGELOG.md](CHANGELOG.md) for the first-release changes.

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
- Adds HPW Editor 2 to the aircraft selector for creating, moving, mirroring, renaming, linking, and restricting pylons.

## Optional Aryx aircraft changes

These features appear only when their source Aryx aircraft are already installed. TGBR does not load Aryx content by itself.

- **F-99 Shrike:** new outer IRM-S2 pylons and a two-position centerline mount.
- **F-16VX Viper II:** a separate `$70m` King Viper variant with more capacitor capacity, more flares, dedicated electronic-warfare pods, NATO-facing weapon names, and a six-bomb GBU-38 rack.
- **FS-3 Ternion:** a revised and consistently enforced pylon/loadout configuration.

## Multiplayer compatibility

TGBR uses matchmaking protocol `tgbr-v57`. Vanilla clients and clients using a different TGBR version cannot join a TGBR server.

## Building from source

The project requires Nuclear Option 0.34 reference assemblies and the installed game's BepInEx assemblies. Copy `Directory.Build.local.props.example` to `Directory.Build.local.props`, set the two paths, then run:

```powershell
dotnet build .\TheGreatBigRebalancing.sln -c Release
```

Game binaries and extracted game assets are not included. No open-source license has been granted.

# The Great Big Rebalancing — Release Notes

## 0.4.65 — First Release — 2026-08-09

For **Nuclear Option 0.34**.

For the complete player-facing breakdown, see [Detailed Changes](https://github.com/NurdVEVO/TheGreatBigRelancing_ing/blob/main/DETAILED_CHANGES.md).

### Additions

- Added the **F-16VX Viper II** as a separate `$70m` aircraft when the Aryx F-16M King Viper is installed.
  - `500 kJ` capacitor capacity.
  - `160` flares, firing four per cycle for 40 total dispenses.
  - Three `$27m` centerline electronic-warfare pods.
  - NATO-facing weapon names and a six-bomb GBU-38 rack.
- Added new outer IRM-S2 pylons and a two-position centerline mount to the **F-99 Shrike**.
- Added a revised pylon and weapon configuration for the **FS-3 Ternion**.
- Restored the targeted/red radar-warning indication from before update 0.34.
- Added high-altitude contrails, fading from invisible at `6,000 m / 19,685 ft` to full strength at `7,500 m / 24,606 ft`.
- Added **HPW Editor 2** to the aircraft selector for editing aircraft pylons, hardpoints, attachments, models, links, weapon limits, and positions.
- Added explicit multiplayer separation from vanilla and mismatched TGBR clients.

### Removals

- Removed the **AAM-36 Scimitar** from every aircraft and loadout.
- Removed all AB-4 air-to-air missiles except the **IRM-S2**.
- Removed ordinary weapons from the Viper II centerline; it accepts only its three electronic-warfare pods.
- Removed the IRM-S2 from the Viper II.
- Removed the obsolete pylon-editor and weapon-workshop interfaces replaced by HPW Editor 2.

### Changes

- **T/A-30 Compass**
  - Restored the pre-0.34 dry mass of `5,320 kg`.
  - Kept update 0.34 thrust at `31,600 N` per engine.
- **AB-4 “Buford”**
  - Updated its player-facing name and description.
  - Split the external wing pylons into independent left and right stations.
  - Each fitted jammer pod can jam two targets.
- **F-99 Shrike**
  - Renamed its pylon groups for clearer fore, aft, mid-wing, and outer positions.
  - Limited the paired centerline mount to AGM-68, CB-400, and GPO-500.
- **F-16VX Viper II**
  - Reduced the size, mass, and drag of all three electronic-warfare pods.
  - The `ADJ-94` reduces ECM use and improves capacitor recharge.
  - The `OJS-808` provides one defensive jamming channel, prioritises incoming missiles, and locks ECM until `300 kJ` after depletion.
  - The `MiM-20` is a passive fitted suite in this release.
- Improved contrail, jammer, editor, and dedicated-server performance.

### Fixes

- Fixed AB-4 renaming breaking aircraft selection.
- Fixed Viper II aircraft, weapons, racks, flares, and jammer pods disappearing or behaving incorrectly after deployment.
- Fixed OJS activation, target selection, missile priority, and recharge-lock behaviour.
- Fixed F-99 centerline positioning, naming, linking, and mirrored stores.
- Fixed Ternion and similar aircraft moving the weapon instead of the pylon assembly in the editor.
- Fixed pylon model changes resetting placement, incorrect mirroring, and the selector free camera being pinned in place.
- Fixed excessive or abruptly appearing contrails.
- Added safeguards against removed or disallowed weapons returning through old loadouts.

### Compatibility

- Nuclear Option: `0.34`
- BepInEx: `5.4.23.4`
- TGBR matchmaking protocol: `tgbr-v57`
- DLL SHA-256: `A52886BBCB0785B0B8B17E11F6CA2B3526EBEA4A0573CB2A2134E457D86338CB`

Aryx-dependent changes activate only when their source aircraft are already installed.

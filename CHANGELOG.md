# The Great Big Rebalancing — Release Notes

## 0.4.65 — First Release — 2026-08-09

This release is compared against unmodded **Nuclear Option 0.34**.

### Additions

#### Aircraft and loadouts

- Added the **F-16VX Viper II** as a separate `$70m` aircraft wherever Aryx's F-16M King Viper is already available.
  - Uses its own aircraft, ownership, save and network identity while sharing the King Viper's Blueprinter-backed aircraft prefab and livery configuration.
  - Does not force-load Aryx content. If the F-16M definition is absent, the Viper II remains unavailable.
  - Adds a `500 kJ` capacitor, increased from the King Viper's `400 kJ`.
  - Adds three Viper II-only `$27m` centerline electronic-warfare suites:
    - `ADJ-94 Self-Defence ECM Suite`
    - `OJS-808 Semi-Offensive Jamming Suite`
    - `MiM-20 Datalink and Radar-Return Spoofer`
  - Adds Viper II-specific weapon-mount prefabs with independent save/network identities and NATO-facing names:
    - PAB-80LR → `GBU-53/B StormBreaker`
    - PAB-250 → `GBU-38 JDAM`
    - PAB-250LR → `GBU-38ER`
    - PAB-125 → `GBU-29`
    - GPO-500 → `GBU-16 Paveway 2`
    - GPO-2P Auger → `GBU-10`
    - AGM-68 → `AGM-65K Maverick`
    - AGM-48 → `AGM-114L Longbow Hellfire`
    - MMR-S3 → `AIM-9X Sidewinder`
    - AAM-29 → `AIM-170 Scythe 2`
  - Adds a permanent inner-wing `GBU-38 JDAM x6` mount using two complete three-bomb rack assemblies.
  - Adds one flare launch point to each inner-wing pylon. These fire alongside the stock dispensers as part of the normal flare cycle.

- Expanded the **Aryx F-99 Shrike** with two new pylon sets when its source aircraft is present:
  - `Outer Wing Pylon`: two physical hardpoints restricted to the IRM-S2.
  - `Aft Centerline Pylon`: one rearward hardpoint natively linked to the original centerline selection.

- Added a deterministic **FS-3 Ternion** pylon policy when its Blueprinter aircraft is present.
  - Covers all nine sets: Internal Cannon, Combined Weapon Bay, Forward Weapon Bay, Rear Weapon Bay, Front Fuselage Pylons, Rear Fuselage Pylons, Inner Wing Pylons, Middle Wing Pylons and Outer Wing Pylons.
  - Applies the exported hardpoint positions, damage-piece attachments and weapon allow-lists to the prefab, selector preview, spawned aircraft, AI/default loadouts and server validation data.

#### Radar warning receiver

- Restored the pre-0.34 aircraft-targeting warning.
  - An aircraft actively selecting you once again sets `OnRadarWarning.isTarget`.
  - The tactical display can therefore distinguish ordinary radar illumination from active targeting with the targeted/red threat indication.
  - Uses Nuclear Option's existing synchronized target state and adds no network messages.

#### High-altitude contrails

- Added locally generated, multiplayer-consistent high-altitude engine contrails for compatible aircraft.
  - Visibility begins at `6,000 m` / `19,685 ft` ASL.
  - Reaches 50% at `6,750 m` / `22,146 ft`.
  - Reaches full strength at `7,500 m` / `24,606 ft`.
  - Requires at least `40 m/s` airspeed and a powered engine/nozzle.
  - Uses exact jet-nozzle thrust transforms where available and registered engine transforms as fallback anchors.
  - Fades in `15 ft` / `4.572 m` behind the engine and retains each trail point's formation-altitude opacity.
  - Uses one pooled missile-style ribbon per active exhaust point, emitted by distance rather than frame rate.

#### Modding tools

- Added **HPW Editor 2** to the aircraft selector.
  - Opens automatically for the selected aircraft.
  - Provides a compact HardpointSet navigator, master pylon configurator, transform palette and selector-scoped free camera.
  - Adds, mirrors, renames and removes hardpoints or complete HardpointSets.
  - Supports two-stage confirmation before removing original game hardpoints or sets.
  - Separates the visible pylon assembly transform from the child weapon-anchor transform.
  - Edits aircraft-, parent- or world-space position, rotation and scale.
  - Mirrors physical placement as `(-X, Y, Z)` without reversing the pylon's facing direction.
  - Changes anchor parents and assigned damageable `UnitPart` objects while preserving aircraft-space placement.
  - Includes `Choose nearest` attachment selection with a temporary animated line, whole-part highlight and bounds outline.
  - Selects pylon models from loaded aircraft templates without copying weapons, colliders, scripts or damage systems.
  - Edits native same-store links, combined selector names, mutual exclusions, removable/Base visual variants and complete weapon allow-lists.
  - Exports full configuration data, including editor metadata, hierarchy paths, transforms, attachment parts, pylon visuals and allowed weapons.
  - Includes a functional free camera with RMB look, WASD/QE movement, boost/precision controls and adjustable speed.

- Retained the session-only weapon-prototype backend for balancing work.
  - Supports individual-store construction or complete rack copies, up to 64 physical stores.
  - Creates isolated mount prefabs and WeaponInfo metadata with collision-safe JSON keys.
  - Registers prototypes with the encyclopedia, allows live hierarchy editing and performs deterministic cleanup.
  - The former multi-window weapon-builder UI is not part of this release; this backend currently supports implemented content such as the permanent Viper II GBU-38 x6 rack.

#### Multiplayer compatibility

- Added explicit separation from vanilla and incompatible mod clients.
  - Hosted games are marked as modded and advertise protocol `tgbr-v57`.
  - Player-hosted and dedicated-server searches filter for the matching protocol.
  - Authentication uses a TGBR-salted build hash and rejects mismatched direct connections.

### Removals

- Removed the **AAM-36 Scimitar** from the usable game arsenal.
  - Disables the missile definition and all seven equipable mount variants: single, double, single-internal, double-internal, compact-double-internal, triple-internal and the AB-4 eight-pack.
  - Removes it from every aircraft hardpoint and from HPW Editor 2's weapon catalog.
  - Blocks stale saves, default/AI loadouts, mission JSON, runtime spawning and later mount clones that still identify as AAM-36.
  - Disabled registry placeholders remain internally so save and network definition indexes stay stable.

- Removed all AB-4 air-to-air weapons except the dedicated **IRM-S2**.
  - This includes the eight-round AAM-29 Scythe and the now globally removed AAM-36 Scimitar.
  - Sanitizes defaults, AI loadouts, saved/custom loadouts and runtime spawns.

- Removed ordinary weapons from the Viper II centerline. It accepts only the three Viper II electronic-warfare suites.

- Removed the IRM-S2 from Viper II hardpoints. The AAM-36 is unavailable through the global removal.

- Removed the obsolete tabbed pylon editor, separate hardpoint-setup window, standalone free-camera settings window, `Mount & rack` tab and former four-window weapon-workshop UI from the compiled mod.

### Changes

#### T/A-30 Compass

- Restored the pre-0.34 structural dry mass of `5,320 kg`, down from vanilla 0.34's `6,110.64478 kg`.
- Restored the affected cockpit, engine and intake part masses before runtime damage/mass initialization.
- Preserved vanilla 0.34 thrust at `31,600 N` per engine / `63,200 N` total.
- Recomputes the cached aircraft mass so local and simplified remote physics use the same value.

#### AB-4 “Buford”

- Changed the practical player-facing name from `Alkyon AB-4` to `AB-4 "Buford"` without changing its underlying aircraft identity.
- Replaced the aircraft description with the Alkyon Bernard Design Bureau/NATO reporting-name text.
- Split the paired external wing pylons into independently selectable `Left Wing Pylon` and `Right Wing Pylon` sets.
- Migrates old paired selections onto both new independent pylons.
- Increased each physical AB-4 jammer pod to two independent target channels, allowing four simultaneous targets with both pods fitted.

#### F-99 Shrike

- Renamed the original `Outer Wing Pylons` to `Mid Wing Pylon`.
- Renamed the original centerline to `Fore Centerline Pylon` and the added rear station to `Aft Centerline Pylon`.
- Uses `Centerline Pylon` as the combined linked selector name.
- Positioned the fore centerline at `(0, -0.295, 0.688)` and the aft centerline at `(0, -0.215, -2.155)`, aligned on aircraft-local X.
- Both centerline hardpoints mirror one selected store and retain the stock empty-weapon-bay requirement.
- Restricted the linked centerline pair to AGM-68, CB-400 and GPO-500.
- Migrates stock, AI, saved and runtime loadouts into the expanded pylon ordering.

#### F-16VX Viper II

- Increased flare ammunition from the King Viper's `64` to `160`.
- Each dispense now fires four flares: the stock cycle plus both inner-wing points.
- Total dispenses increase from 32 to 40 while consuming four flares per cycle.
- Changed Viper II Radar ECM consumption to 160% of the stock F-16M rate when no ADJ/OJS economy modifier applies.
- Electronic-warfare pods consume no separate power; their behavior is tied directly to the aircraft's `Radar ECM` control.
- Reduced all three pod models to 90% stock length and 72% stock width/height.
- Reduced pod physical effects to match the new geometry:
  - Mass: `195.9552 kg` from a nominal `420 kg`.
  - Drag: `0.05184` from a nominal `0.1`.
- `ADJ-94` behavior:
  - Reduces Viper II ECM consumption by 30%, resulting in 112% of stock F-16M consumption.
  - Increases engine-driven capacitor generation to 130% of the Viper II baseline.
  - Adds no independent directional-jamming channel.
- `OJS-808` behavior:
  - Halves the adjusted Viper II ECM cost, resulting in 80% of stock F-16M consumption.
  - Maintains full ECM power and effectiveness at every nonzero capacitor level instead of tapering with remaining charge.
  - Provides one directional-jamming target channel.
  - Prioritizes the nearest inbound ARH or SARH missile threat over ordinary emitters.
  - Jams an ARH missile directly; for SARH, jams the illuminating radar at the source.
  - Otherwise retains a valid target, then prefers a pilot-designated emitter or the nearest hostile emitter within 50 km.
  - On depletion, automatically disarms radar, reports `LOW POWER`, and locks Radar ECM until charge reaches `300 kJ`.
- `MiM-20` remains a passive fitted suite in this release and adds no `Unit.Jam` target channel.

#### Ternion

- Replaced index-dependent pylon edits with stable name-based configuration.
- Corrected hardpoint positions, attached damage pieces and canonical mount references across all nine pylon sets.
- Removed all AAM-36 entries from the exported configuration to comply with the global Scimitar removal.

#### Runtime and visual performance

- Dormant aircraft without active/fading contrails are evaluated at 5 Hz; active trails retain frame-rate sampling.
- Contrails use 12 m point spacing, at most 16 catch-up points per nozzle per frame, a 1,024-point cap and a 15-second fade lifetime.
- Contrail ribbons are allocated lazily and returned to a shared pool when no longer required.
- OJS targeting runs at its 0.2-second jamming cadence; expensive live-unit fallback scans are capped at once per second.
- Cached OJS pod and Viper II power-supply registrations replace repeated hierarchy searches.
- Passive ADJ/MiM pod weapon behavior is disabled after attachment.
- Aircraft-selector lifecycle hooks replace repeated global menu searches.
- Dedicated servers do not construct or update the client-only pylon editor or contrail renderer.

### Fixes

- Fixed the 0.34 regression that forced aircraft radar warnings to report `isTarget = false`.
- Fixed AB-4 player-facing renaming so selecting it in the loadout screen still selects the correct underlying aircraft.
- Fixed Viper II failing to appear by registering it only after the source F-16M and required assets are available.
- Fixed Viper II preview, spawned, multiplayer-client and mission-editor instances losing their variant identity when Blueprinter instantiated the shared King Viper prefab.
- Fixed Viper II-specific weapons and electronic-warfare pods appearing in the loadout screen but disappearing during deployment/server validation.
- Fixed custom weapon prefabs becoming inactive or disappearing when physically spawned.
- Fixed the GBU-38 x6 hierarchy so both complete racks and all six stores survive physical hardpoint spawning.
- Fixed the Viper II wing flare points replacing the stock dispenser cycle; they now fire simultaneously as additions to the normal cycle.
- Fixed OJS activation so holding aircraft Radar ECM actually pulses the fitted physical pod and applies `Unit.Jam` authoritatively.
- Fixed OJS target-channel registration, missing-pod recovery, target selection, SARH source handling and recharge-lock behavior.
- Fixed ADJ/MiM being accidentally treated as independently selectable/firing jammer weapons.
- Fixed F-99 fore/aft centerline positioning, native linking, naming and mirrored-store behavior.
- Fixed Ternion and similar aircraft editing the mounted weapon transform instead of the visible pylon assembly.
- Fixed pylon model replacement resetting station placement by separating immutable assembly binding, model slot and weapon anchor.
- Fixed hardpoint mirroring to use aircraft-space `(-X, Y, Z)` while preserving orientation.
- Fixed the selector free camera being overwritten by Nuclear Option's normal selection-camera `LateUpdate`.
- Fixed new editor-created weapons being absent from source search and allow-list selection.
- Fixed contrails spawning excessive particle/trail combinations by using one ribbon system per distinct active nozzle.
- Fixed hard on/off contrail appearance with a persistent altitude-based opacity gradient and shutdown hysteresis.
- Added runtime sanitation and authoritative spawn guards so removed or disallowed weapons cannot return through stale data.

### Compatibility and release information

- Nuclear Option: `0.34`
- Unity: `2022.3.62f2`
- BepInEx: `5.4.23.4`
- Harmony: `2.9.0`
- Target framework: `.NET Framework 4.7.2`
- TGBR matchmaking protocol: `tgbr-v57`
- Release DLL SHA-256: `A52886BBCB0785B0B8B17E11F6CA2B3526EBEA4A0573CB2A2134E457D86338CB`

Aryx-dependent changes are conditional. TGBR does not load Aryx packages itself; it applies the F-99, F-16VX and Ternion changes only after their source definitions have already been registered by the game/mod loader.

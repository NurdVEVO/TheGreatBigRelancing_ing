# The Great Big Rebalancing

A multiplayer-aware BepInEx 5 rebalance mod for Nuclear Option update 0.34.

Player-facing first-release notes are available in [CHANGELOG.md](CHANGELOG.md).

## Installation

1. Install BepInEx 5.4.23.4 for Nuclear Option.
2. Download `TheGreatBigRebalancing.dll` from the matching GitHub release.
3. Copy it to `Nuclear Option\BepInEx\plugins\TheGreatBigRebalancing\TheGreatBigRebalancing.dll`.
4. Launch the game normally.

Every participant in a multiplayer session, including the host or dedicated server, must use the same TGBR release. Vanilla clients and clients using a different TGBR protocol are intentionally rejected. Aryx-dependent F-99, F-16VX and Ternion changes activate only when their source definitions are already installed and registered; this mod does not force-load Aryx content.

## Current balance changes

### T/A-30 Compass

- Restores the pre-0.34 structural dry mass of `5,320 kg`, down from update 0.34's `6,110.64478 kg`, by restoring the forward-cockpit, engine, and intake part masses to their pre-0.34 values.
- Preserves update 0.34's increased engine output: both engines remain at `31,600 N` each (`63,200 N` total).
- Recomputes the aircraft definition's cached mass from the restored structure so local and simplified remote physics use the same value.

### Radar warning receiver

- Restores the pre-0.34 aircraft targeting warning: an aircraft actively selecting your aircraft once again sets `OnRadarWarning.isTarget`, producing the distinct targeted/red threat indication instead of looking like an ordinary radar illumination.
- Uses Nuclear Option's existing synchronized weapon-station target state and original `Unit.CheckIsTarget` calculation; it adds no new network message and does not classify every radar contact as targeted.

### Global weapon removals

- Removes the AAM-36 Scimitar from every aircraft and hardpoint, including its single, double, internal, compact-internal, triple-internal, and AB-4 eight-missile mount variants.
- Disables all registered Scimitar mounts and rejects AAM-36 selections through the normal availability check, runtime loadout sanitation, and final mount-spawn guard. Stale saves, AI/default loadouts, custom mission JSON, and later mount clones identifying as AAM-36 therefore cannot restore it.
- Disables the AAM4 missile definition as well, hiding it from normal encyclopedia/mission-editor availability. All disabled definitions remain in the internal registry solely to preserve stable save/network lookup indexes; the weapon is not selectable or spawnable.

### Alkyon AB-4 "Buford"

- Removes the eight-round AAM-29 Scythe (`AAM2x8`) from both main weapon bays.
- Removes the eight-round AAM-36 Scimitar (`AAM4x8`) from both main weapon bays.
- Preserves the IRM-S2 (`AAM3_single_internal`) in the dedicated heater bays.
- Sanitizes aircraft defaults, AI standard loadouts, saved/custom loadouts, and runtime weapon spawning so the removed missiles cannot be restored through a stale selection or mission file.
- Splits the formerly paired external wing pylons into independently configurable left and right stations.
- Preserves paired loadouts during migration by applying the former wing-pylon selection to both new stations.
- Each radar-jamming pod has two independent target channels, allowing up to four simultaneous radar targets when both external pods are equipped.

### Aryx F-99 Shrike

- Adds `Outer Wing Pylon` with two IRM-S2-only hardpoints at the exported Shrike positions and renames the stock `Outer Wing Pylons` set to `Mid Wing Pylon`.
- Adds `Aft Centerline Pylon` linked to `Fore Centerline Pylon` through the game's native symmetry system; the combined selector is named `Centerline Pylon`.
- Relocates the primary centreline hardpoint to `(0, -0.295, 0.688)` and places its rearward linked partner at `(0, -0.215, -2.155)`, aligning both hardpoints on the local X axis.
- The centreline selection is mirrored onto both physical hardpoints and both inherit the stock rule that requires the weapon bay to be empty.
- Limits the linked `Centerline` pair to AGM-68, CB-400, and GPO-500; unsupported stores from stale saves or mission JSON are cleared at runtime.
- Migrates stock, AI, saved, and runtime loadouts into the new centreline-pair ordering.

### Aryx F-16VX Viper II

- Adds `F-16VX Viper II` as a distinct purchasable version of the `F-16M King Viper` wherever the King Viper is available.
- Costs `$70m` to purchase.
- Increases capacitor capacity from `400 kJ` to `500 kJ` without changing the original King Viper.
- Limits the centerline pylon to three Viper II-exclusive `$27m` electronic-warfare pods: `ADJ-94 Self-Defence ECM Suite`, `OJS-808 Semi-Offensive Jamming Suite`, and `MiM-20 Datalink and Radar-Return Spoofer`.
- Gives each pod a distinct save/network key, selector name, cloned runtime prefab, and pod identity while sharing the stock directional jammer's range behavior; unsupported centerline stores from defaults, AI loadouts, or stale saves are cleared.
- Renders all three Viper II jammer pod models at 90% of stock length and 72% of stock width/height: the prior 10% uniform reduction plus another 20% slimmer radial profile.
- Scales physical effects geometrically from the stock pod's nominal `420 kg` mass and `0.1` drag: `0.72² × 0.9 = 0.46656` volume gives `195.9552 kg`, while `0.72² = 0.5184` frontal area gives `0.05184` drag. Price and jammer behavior are unchanged.
- Keeps fitted Viper II electronic-warfare pods out of the selectable weapon stations. Only the `OJS-808` registers directional-jamming capability: its physical pod creates one target channel, and holding `Radar ECM` pulses it directly. `ADJ-94` and `MiM-20` mount physically but register zero `Unit.Jam` channels. If early aircraft initialization skipped the selected centerline mount, the authoritative ECM pulse reconstructs and attaches it. The OJS prioritizes the nearest known inbound ARH or SARH missile over ordinary radar emitters: it jams an ARH missile directly, while a SARH threat redirects the channel to its illuminating radar source. With no supported inbound threat, it retains a valid target, then prefers a pilot-designated emitter or the nearest hostile emitter within 50 km—first from fresh HQ tracks, then from the live-unit registry. The channel applies the stock jammer's range falloff, line-of-sight test, 0.2-second server tick, `Unit.Jam` call, and reward accounting.
- The fitted suite adds no separate electrical draw of its own. Viper II Radar ECM consumes 160% of the stock capacitor charge while retaining the same supplied output.
- With the `ADJ-94` fitted, Radar ECM consumes 30% less than the Viper II baseline (112% of stock consumption), and engine-driven capacitor generation runs at 130% of the Viper II baseline.
- With the `OJS-808` fitted, that adjusted Radar ECM cost is halved to 80% of stock charge consumption. ECM power and effectiveness remain steady at every nonzero capacitor level instead of tapering with the supply curve. On depletion, the aircraft radar auto-disarms, the local pilot receives `LOW POWER` rather than the normal manual `Radar Disarmed` notification, and Radar ECM remains locked until the capacitor recharges to 300 kJ.
- OJS detection uses the authoritative loadout/hardpoint key, and its smoke test invokes the real aircraft `RadarJammer.Fire()` using that instance's configured power and intensity rather than a synthetic draw.
- Temporarily exposes the three jammer mounts and every Viper II-specific weapon prefab to the server's shared King Viper loadout validator during a Viper II spawn request, then restores every original King Viper hardpoint list and selection so the custom stores survive deployment without leaking onto the source aircraft.
- Increases the built-in flare ammunition by 2.5x, from 64 to 160, preserves the complete stock dispensing cycle, and adds one emitter to each inner-wing pylon. Both wing emitters inherit the stock flare velocity and fire alongside every normal cycle step, producing four-flare dispenses and 40 total dispenses versus the stock aircraft's 32.
- Uses a distinct save/ownership identity while instantiating Aryx's original Blueprinter asset-backed prefab, avoiding a broken clone of an already-initialized runtime aircraft.
- Configures selector previews, spawned aircraft, multiplayer clients, and mission-editor instances as Viper II immediately after Blueprinter's prefab is instantiated.
- Registers Viper II-specific weapon-mount assets with unique save/network keys, cloned mount GameObject prefabs, and cloned weapon metadata. They currently inherit the vanilla projectile definitions and performance, but can be balanced independently at the mount/presentation layer without modifying the King Viper: PAB-80LR → GBU-53/B StormBreaker, PAB-250 → GBU-38 JDAM, PAB-250LR → GBU-38ER, PAB-125 → GBU-29, GPO-500 → GBU-16 Paveway 2, GPO-2P Auger → GBU-10, AGM-68 → AGM-65K Maverick, AGM-48 → AGM-114L Longbow Hellfire, MMR-S3 → AIM-9X Sidewinder, and AAM-29 → AIM-170 Scythe 2.
- Permanently registers `TGBR_WORKSHOP_TGBR_F16VX_bomb_250_x6` as `GBU-38 JDAM x6` for the Viper II inner-wing pylons only. It uses two complete three-store rack assemblies at exported local Z positions `1.0` and `-1.3`, six isolated GBU-38 stores, two adapters, `3,000 kg` full mass, `0.9` drag, `0.7` RCS, and the exact workshop-exported empty values and child transforms.
- Removes every IRM-S2 and AAM-36 mount variant from Viper II hardpoints and sanitizes defaults, stale selections, runtime loadouts, and server spawn requests without changing the King Viper or other aircraft.

### FS-3 Ternion

- Promotes the exported `P_Trisurface1` configuration into deterministic mod behavior for all nine pylon sets.
- Applies the exact weapon allow-lists, local hardpoint positions, and attached damageable parts by stable pylon name rather than array index while retaining the Ternion's existing `Base` pylon visuals.
- Applies the same policy to the Blueprinter prefab, selector previews, spawned aircraft, AI/default loadouts, and the authoritative server hardpoint lists used by `WeaponChecker.VetLoadout`.
- Replaces equivalent stale mount references with the registered canonical mount and clears selections that are no longer allowed on their pylon.
- Validates every pylon name, hardpoint count, attached part, position, mount key, and final allow-list before reporting the configuration active; if the optional Ternion Blueprinter asset is absent, the policy safely remains inactive.

### High-altitude engine smoke

- Emits white engine smoke above `7,500 m` (`24,606 ft`) ASL, with a `150 m` shutdown hysteresis to prevent flicker at the boundary.
- Uses exact `JetNozzle` thrust transforms where available and registered engine transforms as the fallback for prop, rotor, and compatible modded aircraft.
- Uses one pooled stock missile-smoke `Ribbon` particle system for each distinct engine/nozzle, with `ribbonCount = 1` and particle rendering disabled. This produces exactly one continuous textured trail per exhaust point: no cross-aircraft shared ribbon and no per-particle trail multiplication.
- Uses exact `JetNozzle.thrustTransform` anchors when available. Aircraft without stock jet nozzles fall back to distinct registered `IEngine.transform` positions, allowing propeller, rotor, and compatible modded aircraft to participate until exact per-aircraft overrides are added.
- Ribbon objects are acquired lazily only while a powered nozzle is producing a contrail, then remain active for the existing `15 s` fade before being cleared and returned to the pool. Parked aircraft, inactive VTOL nozzles, and aircraft below contrail altitude therefore own no continuously simulated particle systems.
- Only lightweight ribbon control points are emitted by distance, with a fixed ceiling of `1,024` live points per active nozzle.
- Begins fully transparent `15 ft` (`4.572 m`) behind each engine, then uses the missile effect's rapid fade-in instead of drawing smoke directly against the nozzle.
- Derives the effect from already-synchronized aircraft positions on every compatible client, so it adds no continuous network messages and is independent of aircraft display-detail culling.
- Emits by distance travelled rather than frames elapsed, keeping smoke density stable across client frame rates.
- Forms contrails with a smooth altitude gradient: 0% visibility at 6,000 m / 19,685 ft ASL, 50% at 6,750 m / 22,146 ft, and the previous threshold now represents 100% visibility at 7,500 m / 24,606 ft. Each ribbon point retains the opacity of its formation altitude, producing a spatial gradient during climbs and descents instead of a whole-trail on/off switch.

### Runtime optimization

- Dormant aircraft without an active or fading contrail are evaluated at 5 Hz instead of every rendered frame; active trails retain full frame-rate sampling.
- OJS target selection runs at its actual 0.2-second server jamming cadence, while the expensive untracked-emitter fallback scans the live-unit registry at most once per second. Inbound missile, designated-target, and HQ-track priorities remain responsive at the jamming cadence.
- Repeated OJS pod hierarchy searches are skipped once the physical pod is registered, and passive ADJ/MiM pods are disabled when attached instead of being rediscovered every Radar ECM pulse.
- The aircraft-selector editor receives menu lifecycle notifications and performs only one fallback object search, eliminating four global Unity object searches per second during normal gameplay.
- Viper II power supplies are registered once, replacing an aircraft-parent component lookup on every ADJ recharge physics tick. High-frequency jamming diagnostics were moved from info-level output to rate-limited debug logging.
- Dedicated servers no longer construct or update the client-only pylon editor and contrail renderer.

#### Aircraft-selector pylon editor

- HPW Editor 2 opens automatically beside the currently selected aircraft; no hotkey is required. Its implemented behavior is frozen in [the design contract](docs/HPW_EDITOR_2_DESIGN.md).
- Replaces the former tab and floating-tool layout with a compact pylon navigator, one three-column master pylon configurator, and one compact square transform palette.
- The navigator lists every `HardpointSet` and expands the selected set into its physical hardpoints. Purple denotes safe navigation/construction, while green denotes the active selection or freecam.
- Two fixed action rows sit beneath the navigator's scrollable pylon tree: `+ hardpoint` / `− hardpoint` for physical mount roots, then `+ HardpointSet` / `− HardpointSet` for whole selector sets. New sets are custom, uniquely named, and initialized with one custom no-visual hardpoint; original hardpoint or set removal requires a two-step red confirmation. Set removal safely remaps exclusion indexes and clears only a symmetry link that depended directly on the removed predecessor.
- The master configurator keeps physical hardpoint structure, native same-store linking, mutual exclusions, pylon-assembly/weapon-anchor hierarchy, anchor-parent and damageable-`UnitPart` assignment, pylon-model templates, visual variants, advanced hardpoint references, and the complete allowed-weapon list in one resizable window without tabs.
- `HardpointSet name` directly edits the set's arbitrary player-facing selector name; physical `+ Hardpoint`, `+ Mirrored`, and `− Hardpoint` controls remain inside the opened set. Original-hardpoint removal requires a two-step red confirmation, while custom-hardpoint removal remains purple.
- Editor-owned sidecar metadata gives every hardpoint a stable editor ID, editable display name, and `Original`/`Custom` provenance without modifying Nuclear Option's `Hardpoint` type or adding multiplayer behavior. The metadata is included in configuration export.
- Adds and removes individual hardpoints and creates aircraft-space mirrored partners without hand-editing code. Every newly created station receives a real pylon-assembly parent and a separate child weapon anchor, even when `No visual model` is selected. The visible hardpoint list is the authoritative count instead of a separate count field.
- Builds new hardpoints from a searchable pylon-model template catalog. Models used by the selected aircraft are listed first, followed by Base, mount-specific, legacy-pylon, and plug renderers from every loaded aircraft prefab. Each station captures an immutable assembly binding and owns a dedicated model slot; changing models copies only passive mesh/material data into that slot, never child hardpoints, scripts, colliders, weapons, or damage systems. Selecting the station's original model restores its renderer directly instead of cloning it.
- Resolves the pylon assembly above `Hardpoint.transform` when an aircraft—such as the Ternion or several vanilla aircraft—uses a visible pylon parent with a child weapon anchor. The compact palette separately edits the assembly (model, anchor and store together) or the anchor offset (store only). Shared wings, fuselage pieces, aircraft roots and assemblies containing another hardpoint are rejected. Mirroring is calculated in aircraft space as `(-X, Y, Z)` while preserving the source station's aircraft-relative rotation and scale.
- Exposes the game's native same-store link to the immediately previous pylon set, including its combined selector name, and separately edits symmetric mutual-exclusion links for pylons or bays that cannot be loaded together.
- Separately shows and changes each weapon anchor's actual aircraft-hierarchy parent and its damageable `UnitPart`. `Choose nearest` uses visible renderer/collider geometry, then draws a pulsing world-space line from the anchor and highlights the entire selected damage piece for 1.75 seconds. The feedback combines a whole-part material pulse with a bounds outline, so aircraft whose `UnitPart` does not directly own a renderer still receive visible confirmation. Reparenting preserves aircraft-space placement, rejects hardpoint/store subtrees, updates Parent-space editing immediately, and Reset restores the original parent and local transform.
- Treats each pylon's weapon options as an allow-list with searchable add/remove controls and copy, replace, and merge operations. Structural and weapon-rule changes are staged until `Apply config preview` deliberately rebuilds the selector dropdowns and stores.
- Inspects and edits removable pylon visual variants, including the game's `Base` fallback, cargo matching, mount-specific matching, and renderer selection.
- `Export config` prints and copies HardpointSet and hardpoint provenance/IDs/names, hardpoint counts, native same-store links, preclusion indexes and names, model-template provenance, stable pylon assembly, model-slot and weapon-anchor paths/transforms, original/active renderers, anchor parents, hardpoint indexes, bay-door references, damageable parts, allowed weapon keys, and complete pylon visual/Base configuration.
- The compact transform palette edits Aircraft, Parent, or World-space position/rotation/scale, and provides reset, copy, paste, partner mirroring, and hardpoint focus without opening explanatory tabs or per-axis button grids.
- Includes a selector-scoped free camera without leaving `CameraMode.selection`: its transform override runs after Nuclear Option's own selector-camera `LateUpdate`, so the stock orbit cannot pin it in place. Hold RMB to look, use RMB+WASD/QE to fly, Shift/Ctrl for boost/precision, and mouse wheel for speed. The main navigator owns the only visible freecam toggle.
- Deletes the former tabbed editor, hardpoint-setup window, separate free-camera settings window, `Mount & rack` tab, and four-window workshop implementation from the compiled source rather than leaving them hidden behind a flag.
- Retains only the non-visual weapon-prototype construction and validation backend for the future HPW Editor 2 weapon-builder design. It still supports genuine individual-store mounts and complete rack copies, isolated prefab hierarchies, collision-safe JSON identities, encyclopedia registration, live hierarchy transforms, pylon allow-list insertion, and deterministic cleanup.

## Compatibility baseline

- Nuclear Option update: 0.34 extracted reference
- Unity: 2022.3.62f2
- Mod loader: BepInEx 5.4.23.4
- Harmony: 2.9.0
- Target framework: .NET Framework 4.7.2
- Reference `Assembly-CSharp.dll` SHA-256: `1414E854EE790F515EA7BBAF9C6BFACF98F8D681051D0D4B899ADF47FBB1BBAB`

## Multiplayer separation

- Advertises matchmaking protocol `tgbr-v57` and marks hosted games as modded.
- Filters player-hosted and dedicated-server searches to the same protocol.
- Salts Nuclear Option's authentication build hash and rejects direct connections whose effective hash differs.
- Vanilla clients and clients with a different mod/protocol combination cannot join a rebalance server. Increment the protocol and build-hash salt whenever network-visible balance data or behavior becomes incompatible.

The build uses the extracted 0.34 assemblies for compile-time game APIs and the installed game's BepInEx assemblies for the loader API. Game binaries are never copied into this repository or the build output.

## Build

```powershell
dotnet build .\TheGreatBigRebalancing.sln -c Release
```

Configure local paths by copying `Directory.Build.local.props.example` to the ignored `Directory.Build.local.props` and editing it, setting the `NUCLEAR_OPTION_034_REFERENCE` / `NUCLEAR_OPTION_ROOT` environment variables, or passing MSBuild properties:

```powershell
dotnet build .\TheGreatBigRebalancing.sln -c Release `
  -p:NuclearOptionReferenceRoot="D:\Extracts\Nuclear Option 0.34\GameAssemblies" `
  -p:NuclearOptionRoot="D:\SteamLibrary\steamapps\common\Nuclear Option"
```

## Optional deployment

Deployment is opt-in so a normal build cannot modify the game installation:

```powershell
dotnet build .\TheGreatBigRebalancing.sln -c Release -p:DeployToGame=true
```

This copies only `TheGreatBigRebalancing.dll` to `BepInEx\plugins\TheGreatBigRebalancing`.

## Source and redistribution

This repository contains only the mod source and supporting documentation. Nuclear Option binaries, extracted assemblies, and extracted game textures are deliberately excluded. No open-source license has been granted; absent a separate license, the source remains all rights reserved.

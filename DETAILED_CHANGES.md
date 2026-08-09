# Detailed Changes — 0.4.65

This is the full player-facing breakdown of TGBR's first release for **Nuclear Option 0.34**. It explains what changed and how it affects play without covering the internal mod code.

## Global changes

### AAM-36 Scimitar removal

- The AAM-36 Scimitar is no longer available on any aircraft.
- Single, double, internal, compact, triple, and AB-4 eight-missile mounts are all removed from usable loadouts.
- Old saves, AI loadouts, and custom missions cannot bring it back into play.

### Radar warning receiver

- The targeted/red radar-warning indication from before update 0.34 has been restored.
- Ordinary radar illumination remains distinct from an aircraft actively targeting you.

### Multiplayer separation

- TGBR games are marked as modded and use protocol `tgbr-v57`.
- Vanilla clients and clients using a different TGBR version cannot join.
- Every player, host, and dedicated server must use the same release.

### Disabled development tooling

- HPW Editor 2 and its weapon workshop are disabled in this release.
- Their source is retained for future development but excluded from the compiled mod.

## T/A-30 Compass

- Dry mass is restored from update 0.34's `6,110.64478 kg` to the pre-0.34 value of `5,320 kg`.
- Update 0.34 engine power is retained at `31,600 N` per engine, or `63,200 N` total.
- The result is a lighter Compass with the current engine performance.

## AB-4 “Buford”

### Identity

- The practical player-facing name is now `AB-4 "Buford"`.
- Its description now identifies it as an Alkyon Bernard Design Bureau supersonic bomber and electronic-warfare support platform.

### Weapons and pylons

- The IRM-S2 is now the AB-4's only air-to-air missile.
- The eight-round AAM-29 Scythe and AAM-36 Scimitar options are removed from its main bays.
- The external wing pylons are split into independent left and right stations instead of sharing one selection.
- Existing paired loadouts are carried over to both independent stations where possible.

### Jammers

- Each physical AB-4 jammer pod can jam two targets.
- Carrying both pods provides up to four simultaneous jammer targets.

## F-99 Shrike

These changes require the Aryx F-99 Shrike to already be installed.

- The original outer pylons are renamed `Mid Wing Pylon`.
- A new `Outer Wing Pylon` pair is added and limited to the IRM-S2.
- The original centerline is renamed `Fore Centerline Pylon`.
- A new rear station is added as `Aft Centerline Pylon`.
- The fore and aft stations share one `Centerline Pylon` loadout selection, giving the centerline two physical hardpoints.
- The paired centerline keeps the original requirement for the weapon bay to be empty.
- Centerline weapons are limited to AGM-68, CB-400, and GPO-500.

## F-16VX Viper II

These changes require the Aryx F-16M King Viper to already be installed. The original F-16M remains available and unchanged.

### Aircraft

- Adds the `F-16VX Viper II` as a separate aircraft costing `$70m`.
- F-16M liveries are available on the Viper II.
- Capacitor capacity is increased from `400 kJ` to `500 kJ`.
- The centerline accepts only the Viper II's electronic-warfare pods.

### Flares

- Flare ammunition is increased from `64` to `160`.
- One extra flare point is added to each inner-wing pylon.
- Both wing points fire alongside the normal dispensers, producing four flares per cycle.
- The aircraft receives 40 complete four-flare dispenses, compared with 32 stock dispenses.

### Electronic-warfare pods

All three pods cost `$27m`. They are shorter, slimmer, lighter, and less draggy than the original pod model.

- **ADJ-94 Self-Defence ECM Suite**
  - Reduces Viper II ECM use by 30%.
  - Increases capacitor recharge to 130% of the normal Viper II rate.
  - Does not provide a separate directional-jamming target.
- **OJS-808 Semi-Offensive Jamming Suite**
  - Provides one directional-jamming target while aircraft Radar ECM is active.
  - Prioritises the nearest incoming active-radar or semi-active-radar missile threat.
  - Jams an active-radar missile directly and attacks a semi-active missile at its illuminating radar source.
  - Falls back to a designated or nearby hostile radar when no supported missile threat is present.
  - Maintains full ECM effectiveness until the capacitor reaches zero.
  - Uses half of the Viper II's normal ECM charge cost.
  - On depletion, radar shuts down with a `LOW POWER` warning and ECM stays locked until the capacitor reaches `300 kJ`.
- **MiM-20 Datalink and Radar-Return Spoofer**
  - Mounts as a passive electronic-warfare suite in this release.
  - Does not provide a directional-jamming target.

### Weapons

- Adds a six-bomb GBU-38 JDAM rack to the inner-wing pylons.
- Viper II weapon mounts use NATO-facing names:
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
- The IRM-S2 is removed from Viper II loadouts.
- The AAM-36 is unavailable because of the global Scimitar removal.

## FS-3 Ternion

These changes require the Project 2082 FS-3 Ternion to already be installed.

- Applies one consistent loadout policy to selector previews, deployed aircraft, AI, saved loadouts, and multiplayer validation.
- Revises all nine weapon groups: Internal Cannon, Combined Weapon Bay, Forward Weapon Bay, Rear Weapon Bay, Front Fuselage Pylons, Rear Fuselage Pylons, Inner Wing Pylons, Middle Wing Pylons, and Outer Wing Pylons.
- Corrects pylon positions and the aircraft parts to which they are attached.
- Keeps the Ternion's removable `Base` pylon visuals.
- Removes all AAM-36 options in line with the global Scimitar removal.

## High-altitude contrails

- Compatible powered aircraft form visible white engine contrails at altitude.
- Contrails begin at `6,000 m / 19,685 ft` above sea level.
- They reach half strength at `6,750 m / 22,146 ft`.
- They reach full strength at `7,500 m / 24,606 ft`.
- The effect fades in behind each engine rather than beginning as a hard block at the nozzle.
- Visibility changes gradually during climbs and descents instead of switching on or off instantly.
- Contrails remain visible at long range and are generated consistently for compatible clients.
- The system is limited to one trail per active exhaust point and sleeps when no trail is needed.

## Player-facing fixes

- Fixed AB-4 renaming preventing correct aircraft selection.
- Fixed Viper II failing to appear when its source aircraft was available.
- Fixed Viper II identity being lost in previews, deployment, multiplayer, or the mission editor.
- Fixed Viper II weapons, the GBU-38 x6 rack, and electronic-warfare pods disappearing after deployment.
- Fixed Viper II flare points replacing the normal flare cycle instead of supplementing it.
- Fixed OJS jamming failing to activate with aircraft Radar ECM.
- Fixed OJS missile priority, semi-active radar source selection, and low-power recharge lock.
- Fixed the F-99 centerline positions, names, linking, and shared stores.
- Fixed excessive contrail spawning and abrupt on/off visibility.
- Fixed removed or disallowed weapons returning through old saves, AI loadouts, or custom missions.

## Compatibility

- Nuclear Option: `0.34`
- BepInEx: `5.4.23.4`
- TGBR matchmaking protocol: `tgbr-v57`
- Release DLL SHA-256: `330E1459532F9192D32CEC589399F672F39E291C1BFF04BF6EABD1FCDDC5C1DC`

Aryx- and Project 2082-dependent changes are conditional. TGBR does not install or force-load either mod's aircraft.

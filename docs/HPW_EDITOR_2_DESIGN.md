# HPW Editor 2 design contract

## Purpose

HPW Editor 2 replaces the aircraft-selector editor's scattered tab and tool-window flow with:

1. A compact aircraft pylon navigator.
2. One master configurator for the open `HardpointSet`.
3. One compact transform palette for the selected physical hardpoint.

The game data hierarchy remains `Aircraft -> WeaponManager -> HardpointSet -> Hardpoint -> spawned WeaponMount prefab`, but a physical external station can use an additional scene hierarchy: `damageable aircraft structure -> pylon assembly/model -> Hardpoint.transform weapon anchor -> spawned WeaponMount`. HPW Editor 2 resolves and exposes both transform layers instead of assuming `Hardpoint.transform` is always the visible pylon root.

## Colour semantics

- Purple: safe navigation or constructive action.
- Green: active selection or enabled toggle.
- Red: removing an original, non-editor-created hardpoint.
- Black/neutral: inactive entries and ordinary information.
- Removing an editor-created hardpoint uses purple. Removing an original hardpoint requires an explicit second confirmation.

## Main navigator

The navigator is always visible while an aircraft-selector preview is available.

- `Export config` prints and copies the complete aircraft pylon configuration.
- `Apply config preview` rebuilds the selector dropdowns and preview stores from the current draft.
- `Open Selected Pylon` opens the master configurator for the selected `HardpointSet`.
- The tree lists every `HardpointSet`; only the selected set expands to show its physical hardpoints.
- A selected set is purple. A selected hardpoint is green.
- Two fixed action rows sit directly beneath the pylon-tree scroll area: `+ hardpoint` / `- hardpoint`, followed by `+ HardpointSet` / `- HardpointSet`.
- The hardpoint row edits the selected set's physical mount roots. The HardpointSet row edits whole selector sets. Neither row scrolls away with the pylon tree.
- `+ HardpointSet` appends one custom set with a unique default name and one custom station at the aircraft root. The station always has a pylon-assembly transform containing a separate weapon-anchor transform, even when its model is `No visual model`.
- `- HardpointSet` removes the selected set; custom sets use purple, while original sets use a two-step red confirmation.
- `Freecam` toggles selector free-camera control and is green while enabled.

## Master pylon configurator

The configurator is a single resizable three-column window with no tabs.

### Physical hardpoints

- Lists all physical hardpoints and their editor display names.
- Shows `ORIGINAL` or `CUSTOM` provenance.
- Selects, adds, mirrors and removes hardpoints.
- Opens the transform palette for the selected hardpoint.

### Pylon and hardpoint configuration

- Edits the `HardpointSet` selector name.
- Shows the set index, stable editor identity, `Original`/`Custom` origin and physical hardpoint count.
- Edits native same-store linking to the immediately previous set.
- Edits the combined selector name.
- Edits mutual pylon exclusions.
- Edits arbitrary editor-only hardpoint display metadata.
- Resolves a safe pylon assembly root once from the hardpoint's unique referenced Pylon/Base/variant renderer and weapon anchor, then retains that immutable station binding for the life of the preview. It rejects the aircraft root, the hardpoint's assigned damage-piece root and any candidate shared by another hardpoint, while accepting dedicated pylon assemblies beneath detached `UnitPart` roots.
- Edits the pylon assembly transform as the normal whole-station layer, moving the model, anchor and mounted store together.
- Separately edits the `Hardpoint.transform` weapon-anchor offset without moving an ancestor pylon model.
- New hardpoints use this two-layer hierarchy from creation; existing aircraft hardpoints resolve it from the referenced pylon renderer and anchor hierarchy.
- Selects the actual aircraft-hierarchy parent of the weapon anchor while preserving its aircraft-space position, rotation and scale.
- Separately selects the damageable `UnitPart`; changing damage metadata never silently reparents the mount root.
- `Choose nearest` uses renderer/collider geometry rather than only transform pivots, assigns the nearest damageable `UnitPart`, and provides 1.75 seconds of feedback through an animated world-space line, a pulsing whole-part material tint, and a bounds-outline fallback for parts without directly owned renderer geometry.
- Selects and applies a pylon-model template through a dedicated child model slot. Alternative models copy only passive `MeshFilter`/`MeshRenderer` data; selecting the original model restores the original renderer references directly. Model replacement cannot re-resolve, reparent or modify the station assembly or weapon anchor.
- Exposes compact pylon visual-variant configuration.
- Shows bay-door, plug, legacy renderer and hardpoint-index details in a collapsed advanced section.

### Allowed weapons

- Shows and removes current allowed `WeaponMount` entries.
- Searches all registered and currently referenced mounts.
- Adds mounts to the allow-list.
- Copies, replaces or merges allow-lists.
- Marks the selector preview dirty after structural or allow-list changes.

## Editor-only metadata

Each physical hardpoint has sidecar metadata owned by the editor:

- Stable editor ID.
- `Original` or `Custom` provenance.
- Editable display name.

This metadata never modifies Nuclear Option's `Hardpoint` type and has no multiplayer behavior. Runtime object references identify the current preview; stable IDs are exported so later draft persistence can re-associate editor nodes.

Each `HardpointSet` also receives a stable editor ID and `Original`/`Custom` provenance. The actual `HardpointSet.name` remains freely editable and is the player-facing selector name.

## Compact transform palette

The palette remains visible while the user works and retargets when another hardpoint is selected.

- Target name, provenance and the active `Assembly`/`Anchor` layer are always visible.
- `Assembly` and `Anchor` buttons switch the compact palette between whole-pylon placement and weapon-offset editing.
- Coordinate space supports Aircraft, Parent and World, with Aircraft as default.
- Position, rotation and scale use a compact X/Y/Z grid.
- Valid numeric changes update the actual hardpoint mount root and its pylon visuals live.
- Reset, copy, paste, aircraft-space mirror and focus are available without opening another panel. Mirror maps position to `(-X, Y, Z)` and preserves the source station's aircraft-relative rotation and scale, so it does not turn the copied station around.
- No explanatory prose or per-axis button grids occupy the palette.

## State and safety

- Structural and weapon-rule edits mark the selector preview dirty.
- `Apply config preview` is the deliberate rebuild boundary.
- Assembly and anchor transform edits remain live because mounted stores are children of the weapon anchor, which is itself normally a child of the pylon assembly.
- Root-parent changes reject hardpoint/store subtrees, preserve world placement, and make Parent-space coordinates relative to the newly selected hierarchy node. Reset restores both the original parent and original local transform.
- Original-hardpoint removal is a two-step red confirmation.
- Custom-hardpoint removal is a single purple action.
- Original-HardpointSet removal is a two-step red confirmation; custom-set removal is a single purple action.
- New sets are append-only. Removing a set remaps byte-indexed exclusions and clears a downstream `SymmetryWithPrev` link that would otherwise silently point at a different predecessor.
- Export includes editor metadata plus the stable assembly, model-slot, original/active renderer and weapon-anchor paths in addition to game-facing configuration.

## Deferred work

- Persistent draft files and full undo/redo.
- Direct 3D transform gizmos.
- Independent pylon-visual offset targeting.
- Safe HardpointSet reordering and insertion between existing sets.
- HPW Editor 2 redesign of the custom weapon builder.

The former tabbed editor, hardpoint-setup window, free-camera settings window and four-window weapon-workshop UI have been deleted from the compiled source. Only the non-visual weapon-prototype construction and validation backend remains for the future weapon-builder redesign.

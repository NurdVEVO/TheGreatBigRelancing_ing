using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace TheGreatBigRebalancing.Tools
{
    internal sealed partial class AircraftSelectorPylonEditor
    {
        private enum Editor2NodeOrigin
        {
            Original,
            Custom
        }

        private enum Editor2TransformSpace
        {
            Aircraft,
            Parent,
            World
        }

        private enum Editor2TransformTargetKind
        {
            Assembly,
            Anchor
        }

        private sealed class Editor2HardpointMetadata
        {
            internal string StableId;
            internal string DisplayName;
            internal Editor2NodeOrigin Origin;
        }

        private sealed class Editor2HardpointSetMetadata
        {
            internal string StableId;
            internal Editor2NodeOrigin Origin;
        }

        private const int Editor2MainWindowId = 0x54474260;
        private const int Editor2MasterWindowId = 0x54474261;
        private const int Editor2TransformWindowId = 0x54474262;

        private static readonly Color Editor2Purple = new Color(0.44f, 0.42f, 0.95f);
        private static readonly Color Editor2Green = new Color(0.18f, 0.76f, 0.25f);
        private static readonly Color Editor2Red = new Color(0.86f, 0.2f, 0.2f);
        private static readonly Color Editor2Neutral = new Color(0.18f, 0.18f, 0.18f);
        private static readonly Color Editor2Warning = new Color(0.95f, 0.62f, 0.15f);

        private readonly Dictionary<Hardpoint, Editor2HardpointMetadata> editor2HardpointMetadata =
            new Dictionary<Hardpoint, Editor2HardpointMetadata>();
        private readonly Dictionary<HardpointSet, Editor2HardpointSetMetadata> editor2HardpointSetMetadata =
            new Dictionary<HardpointSet, Editor2HardpointSetMetadata>();
        private readonly List<WeaponMount> editor2WeaponClipboard = new List<WeaponMount>();
        private readonly string[] editor2TransformPosition = new string[3];
        private readonly string[] editor2TransformRotation = new string[3];
        private readonly string[] editor2TransformScale = new string[3];

        private Rect editor2MainWindowRect = new Rect(18f, 18f, 430f, 700f);
        private Rect editor2MasterWindowRect = new Rect(470f, 18f, 1200f, 760f);
        private Rect editor2TransformWindowRect = new Rect(470f, 790f, 430f, 270f);
        private Vector2 editor2TreeScroll;
        private Vector2 editor2HardpointScroll;
        private Vector2 editor2MiddleScroll;
        private Vector2 editor2ExclusionScroll;
        private Vector2 editor2RootParentScroll;
        private Vector2 editor2PartScroll;
        private Vector2 editor2TemplateScroll;
        private Vector2 editor2VariantScroll;
        private Vector2 editor2VariantChoiceScroll;
        private Vector2 editor2AllowedScroll;
        private Vector2 editor2AvailableScroll;

        private bool editor2MasterOpen;
        private bool editor2TransformOpen;
        private bool editor2RootParentPickerOpen;
        private bool editor2PartPickerOpen;
        private bool editor2TemplatePickerOpen;
        private bool editor2VariantsOpen;
        private bool editor2VariantWeaponPickerOpen;
        private bool editor2VariantRendererPickerOpen;
        private bool editor2AdvancedOpen;
        private bool editor2TransformClipboardValid;
        private bool editor2LayoutLoaded;
        private bool editor2HardpointSetSmokeValidated;
        private string editor2RootParentSearch = string.Empty;
        private string editor2PartSearch = string.Empty;
        private string editor2TemplateSearch = string.Empty;
        private string editor2WeaponSearch = string.Empty;
        private string editor2VariantSearch = string.Empty;
        private string editor2DoorTimeText = "0";
        private Hardpoint editor2PendingOriginalRemoval;
        private HardpointSet editor2PendingOriginalSetRemoval;
        private Transform editor2TransformTarget;
        private TransformSnapshot editor2TransformClipboard;
        private Editor2TransformSpace editor2TransformSpace = Editor2TransformSpace.Aircraft;
        private Editor2TransformTargetKind editor2TransformTargetKind =
            Editor2TransformTargetKind.Assembly;

        private void DrawEditor2Gui(float virtualWidth, float virtualHeight)
        {
            EnsureEditor2LayoutLoaded();
            DrawManagedWindow(
                ref editor2MainWindowRect,
                Editor2MainWindowId,
                DrawEditor2MainWindow,
                "HPW Editor 2",
                360f,
                520f,
                virtualWidth,
                virtualHeight);

            if (editor2MasterOpen && SelectedSet() != null)
            {
                DrawManagedWindow(
                    ref editor2MasterWindowRect,
                    Editor2MasterWindowId,
                    DrawEditor2MasterWindow,
                    "Master Pylon Configurator",
                    900f,
                    560f,
                    virtualWidth,
                    virtualHeight);
            }

            if (editor2TransformOpen &&
                Editor2SelectedTransformTarget(SelectedHardpoint()) != null)
            {
                DrawManagedWindow(
                    ref editor2TransformWindowRect,
                    Editor2TransformWindowId,
                    DrawEditor2TransformWindow,
                    "Compact Transform",
                    390f,
                    245f,
                    virtualWidth,
                    virtualHeight);
            }
        }

        private void DrawEditor2MainWindow(int windowId)
        {
            GUILayout.Label("Aircraft: " + AircraftLabel());
            GUILayout.BeginHorizontal();
            if (Editor2Button("Export config", Editor2Neutral, GUILayout.Height(30f)))
            {
                PrintAndCopyConfiguration();
            }
            GUI.enabled = loadoutSelector != null;
            if (Editor2Button(
                    "Apply config preview",
                    weaponUiRefreshPending ? Editor2Purple : Editor2Neutral,
                    GUILayout.Height(30f)))
            {
                RefreshSelectorWeaponUi();
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            GUI.enabled = SelectedSet() != null;
            if (Editor2Button(
                    "Open Selected Pylon",
                    Editor2Purple,
                    GUILayout.Height(42f)))
            {
                editor2MasterOpen = true;
                status = "Opened " + SelectedSet().name + " in the master configurator.";
            }
            GUI.enabled = true;

            editor2TreeScroll = GUILayout.BeginScrollView(
                editor2TreeScroll,
                GUI.skin.box,
                GUILayout.ExpandHeight(true));
            DrawEditor2PylonTree();
            GUILayout.EndScrollView();

            DrawEditor2MainHardpointActions();
            DrawEditor2MainHardpointSetActions();
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (Editor2Button(
                    freeCameraEnabled ? "Freecam ON" : "Freecam",
                    freeCameraEnabled ? Editor2Green : Editor2Purple,
                    GUILayout.Width(130f),
                    GUILayout.Height(28f)))
            {
                SetFreeCameraEnabled(!freeCameraEnabled);
            }
            GUILayout.EndHorizontal();
            GUILayout.Label(status, GUI.skin.box);
            DrawWindowChrome(windowId, 360f, 520f);
        }

        private void DrawEditor2PylonTree()
        {
            if (weaponManager?.hardpointSets == null)
            {
                GUILayout.Label("No hardpoint sets are available.");
                return;
            }

            for (int setIndex = 0; setIndex < weaponManager.hardpointSets.Length; setIndex++)
            {
                HardpointSet set = weaponManager.hardpointSets[setIndex];
                if (set == null)
                {
                    continue;
                }
                bool selectedSet = setIndex == selectedSetIndex;
                string marker = selectedSet ? "▼ " : "▶ ";
                if (Editor2Button(
                        marker + (set.name ?? "<unnamed>") +
                        "  [" + (set.hardpoints?.Count ?? 0) + "]",
                        selectedSet ? Editor2Purple : Editor2Neutral,
                        GUILayout.Height(31f)))
                {
                    SelectPylon(setIndex);
                }

                if (!selectedSet || set.hardpoints == null)
                {
                    continue;
                }
                for (int hardpointIndex = 0; hardpointIndex < set.hardpoints.Count; hardpointIndex++)
                {
                    Hardpoint hardpoint = set.hardpoints[hardpointIndex];
                    Editor2HardpointMetadata metadata = GetEditor2Metadata(hardpoint, false);
                    bool selectedHardpoint = hardpointIndex == selectedHardpointIndex;
                    GUILayout.BeginHorizontal();
                    GUILayout.Space(26f);
                    if (Editor2Button(
                            metadata?.DisplayName ?? "Hardpoint " + (hardpointIndex + 1),
                            selectedHardpoint ? Editor2Green : Editor2Neutral,
                            GUILayout.Height(28f)))
                    {
                        SelectHardpoint(hardpointIndex);
                    }
                    GUILayout.EndHorizontal();
                }
            }

        }

        private void DrawEditor2MainHardpointActions()
        {
            HardpointSet set = SelectedSet();
            Hardpoint hardpoint = SelectedHardpoint();
            GUILayout.BeginHorizontal();
            GUI.enabled = set?.hardpoints != null;
            if (Editor2Button("+ hardpoint", Editor2Purple, GUILayout.Height(34f)))
            {
                AddHardpoint(set, false);
            }
            GUI.enabled = hardpoint != null;
            DrawEditor2RemoveHardpointButton(set, hardpoint, GUILayout.Height(34f));
            GUI.enabled = true;
            GUILayout.EndHorizontal();
        }

        private void DrawEditor2MainHardpointSetActions()
        {
            HardpointSet set = SelectedSet();
            GUILayout.BeginHorizontal();
            GUI.enabled = weaponManager != null;
            if (Editor2Button("+ HardpointSet", Editor2Purple, GUILayout.Height(34f)))
            {
                AddEditor2HardpointSet();
            }
            GUI.enabled = set != null;
            DrawEditor2RemoveHardpointSetButton(set, GUILayout.Height(34f));
            GUI.enabled = true;
            GUILayout.EndHorizontal();
        }

        private void DrawEditor2MasterWindow(int windowId)
        {
            HardpointSet set = SelectedSet();
            if (set == null)
            {
                editor2MasterOpen = false;
                return;
            }

            Editor2HardpointSetMetadata setMetadata = GetEditor2HardpointSetMetadata(set, false);
            GUILayout.BeginHorizontal();
            GUILayout.Label(
                AircraftLabel() + "  >  Set " + selectedSetIndex,
                GUILayout.ExpandWidth(true));
            GUILayout.Label(Editor2SetOriginLabel(setMetadata), GUI.skin.box, GUILayout.Width(90f));
            if (Editor2Button("Validate", Editor2Purple, GUILayout.Width(90f)))
            {
                ValidateEditor2Pylon(set);
            }
            if (Editor2Button("Close", Editor2Neutral, GUILayout.Width(70f)))
            {
                editor2MasterOpen = false;
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("HardpointSet name", GUILayout.Width(126f));
            string name = GUILayout.TextField(set.name ?? string.Empty);
            if (!string.Equals(name, set.name, StringComparison.Ordinal))
            {
                set.name = name;
                weaponUiRefreshPending = true;
                status = "Renamed the selected HardpointSet; apply the config preview to rebuild the selector.";
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            DrawEditor2MasterHardpoints(set);
            DrawEditor2MasterConfiguration(set);
            DrawEditor2MasterWeapons(set);
            GUILayout.EndHorizontal();

            GUILayout.Label(
                (weaponUiRefreshPending ? "Draft modified | Preview rebuild required | " :
                    "Preview synchronized | ") + status,
                GUI.skin.box);
            DrawWindowChrome(windowId, 900f, 560f);
        }

        private void DrawEditor2MasterHardpoints(HardpointSet set)
        {
            GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(245f), GUILayout.ExpandHeight(true));
            GUILayout.Label("PHYSICAL HARDPOINTS");
            GUILayout.Label((set.hardpoints?.Count ?? 0) + " mount root(s)");
            editor2HardpointScroll = GUILayout.BeginScrollView(
                editor2HardpointScroll,
                GUILayout.ExpandHeight(true));
            if (set.hardpoints != null)
            {
                for (int index = 0; index < set.hardpoints.Count; index++)
                {
                    Hardpoint hardpoint = set.hardpoints[index];
                    Editor2HardpointMetadata metadata = GetEditor2Metadata(hardpoint, false);
                    string origin = metadata?.Origin == Editor2NodeOrigin.Custom
                        ? "CUSTOM"
                        : "ORIGINAL";
                    if (Editor2Button(
                            (metadata?.DisplayName ?? "Hardpoint " + (index + 1)) +
                            "  ·  " + origin,
                            index == selectedHardpointIndex ? Editor2Green : Editor2Neutral,
                            GUILayout.Height(31f)))
                    {
                        SelectHardpoint(index);
                    }
                }
            }
            GUILayout.EndScrollView();

            if (Editor2Button("+ Hardpoint", Editor2Purple, GUILayout.Height(30f)))
            {
                AddHardpoint(set, false);
            }
            GUI.enabled = SelectedHardpoint() != null;
            if (Editor2Button("+ Mirrored", Editor2Purple, GUILayout.Height(30f)))
            {
                AddHardpoint(set, true);
            }
            DrawEditor2RemoveHardpointButton(set, SelectedHardpoint(), GUILayout.Height(30f));
            if (Editor2Button("Open Transform", Editor2Purple, GUILayout.Height(32f)))
            {
                OpenEditor2TransformPalette();
            }
            GUI.enabled = true;
            GUILayout.EndVertical();
        }

        private void DrawEditor2MasterConfiguration(HardpointSet set)
        {
            GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(455f), GUILayout.ExpandHeight(true));
            editor2MiddleScroll = GUILayout.BeginScrollView(
                editor2MiddleScroll,
                GUILayout.ExpandHeight(true));
            DrawEditor2Relationships(set);
            DrawEditor2SelectedHardpoint();
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        private void DrawEditor2MasterWeapons(HardpointSet set)
        {
            GUILayout.BeginVertical(GUI.skin.box, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            GUILayout.Label("ALLOWED WEAPONS");
            set.weaponOptions = set.weaponOptions ?? new List<WeaponMount>();

            GUILayout.BeginHorizontal();
            if (Editor2Button("Copy list", Editor2Purple, GUILayout.Width(92f)))
            {
                editor2WeaponClipboard.Clear();
                editor2WeaponClipboard.AddRange(set.weaponOptions.FindAll(mount => mount != null));
                status = "Copied " + editor2WeaponClipboard.Count + " allowed weapon(s).";
            }
            GUI.enabled = editor2WeaponClipboard.Count > 0;
            if (Editor2Button("Replace", Editor2Purple, GUILayout.Width(80f)))
            {
                set.weaponOptions = new List<WeaponMount>(editor2WeaponClipboard);
                MarkEditor2PreviewDirty("Replaced the selected pylon's weapon allow-list.");
            }
            if (Editor2Button("Merge", Editor2Purple, GUILayout.Width(70f)))
            {
                foreach (WeaponMount mount in editor2WeaponClipboard)
                {
                    if (mount != null && !set.weaponOptions.Contains(mount))
                    {
                        set.weaponOptions.Add(mount);
                    }
                }
                MarkEditor2PreviewDirty("Merged the copied weapon allow-list.");
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            editor2AllowedScroll = GUILayout.BeginScrollView(
                editor2AllowedScroll,
                GUI.skin.box,
                GUILayout.Height(220f));
            for (int index = set.weaponOptions.Count - 1; index >= 0; index--)
            {
                WeaponMount mount = set.weaponOptions[index];
                if (mount == null)
                {
                    continue;
                }
                GUILayout.BeginHorizontal();
                GUILayout.Label(Editor2CompactLabel(WeaponLabel(mount), 38));
                if (Editor2Button("−", Editor2Neutral, GUILayout.Width(30f)))
                {
                    set.weaponOptions.RemoveAt(index);
                    MarkEditor2PreviewDirty("Removed " + WeaponLabel(mount) + " from the allow-list.");
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Search", GUILayout.Width(48f));
            editor2WeaponSearch = GUILayout.TextField(editor2WeaponSearch ?? string.Empty);
            GUILayout.EndHorizontal();
            editor2AvailableScroll = GUILayout.BeginScrollView(
                editor2AvailableScroll,
                GUI.skin.box,
                GUILayout.ExpandHeight(true));
            int shown = 0;
            foreach (WeaponMount mount in allWeapons)
            {
                string label = WeaponLabel(mount);
                if (mount == null || set.weaponOptions.Contains(mount) ||
                    !MatchesSearch(label, editor2WeaponSearch))
                {
                    continue;
                }
                if (Editor2Button(
                        "+ " + Editor2CompactLabel(label, 42),
                        Editor2Purple,
                        GUILayout.Height(27f)))
                {
                    set.weaponOptions.Add(mount);
                    MarkEditor2PreviewDirty("Allowed " + WeaponLabel(mount) + " on " + set.name + ".");
                }
                if (++shown >= 150)
                {
                    GUILayout.Label("Refine search to show more results.");
                    break;
                }
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        private void DrawEditor2Relationships(HardpointSet set)
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("PYLON CONFIGURATION");
            GUILayout.Label(
                "Set index " + selectedSetIndex + " · " +
                (set.hardpoints?.Count ?? 0) + " physical hardpoint(s)");
            if (selectedSetIndex <= 0)
            {
                GUILayout.Label("Same-store link: unavailable for the first set.");
            }
            else
            {
                HardpointSet previous = weaponManager.hardpointSets[selectedSetIndex - 1];
                if (Editor2Button(
                        (set.SymmetryWithPrev ? "LINKED with previous: " : "Link with previous: ") +
                        (previous?.name ?? "<missing>"),
                        set.SymmetryWithPrev ? Editor2Green : Editor2Purple,
                        GUILayout.Height(29f)))
                {
                    set.SymmetryWithPrev = !set.SymmetryWithPrev;
                    MarkEditor2PreviewDirty(
                        set.SymmetryWithPrev
                            ? "Linked this selector to the immediately previous pylon."
                            : "Removed the native same-store link.");
                }
            }
            GUILayout.BeginHorizontal();
            GUILayout.Label("Combined name", GUILayout.Width(112f));
            string symmetryName = GUILayout.TextField(set.SymmetryName ?? string.Empty);
            if (!string.Equals(symmetryName, set.SymmetryName, StringComparison.Ordinal))
            {
                set.SymmetryName = symmetryName;
                MarkEditor2PreviewDirty("Changed the linked selector name.");
            }
            GUILayout.EndHorizontal();

            GUILayout.Label("Mutual exclusions");
            editor2ExclusionScroll = GUILayout.BeginScrollView(
                editor2ExclusionScroll,
                GUILayout.Height(105f));
            for (int index = 0; index < weaponManager.hardpointSets.Length; index++)
            {
                if (index == selectedSetIndex || index > byte.MaxValue)
                {
                    continue;
                }
                HardpointSet other = weaponManager.hardpointSets[index];
                bool selectedBlocksOther = ContainsPreclusion(set, index);
                bool otherBlocksSelected = ContainsPreclusion(other, selectedSetIndex);
                bool mutual = selectedBlocksOther && otherBlocksSelected;
                Color color = mutual
                    ? Editor2Green
                    : (selectedBlocksOther || otherBlocksSelected ? Editor2Warning : Editor2Neutral);
                string state = mutual
                    ? "EXCLUDES "
                    : (selectedBlocksOther || otherBlocksSelected ? "ONE-WAY " : "Allow with ");
                if (Editor2Button(
                        state + "[" + index + "] " + (other?.name ?? "<missing>"),
                        color,
                        GUILayout.Height(25f)))
                {
                    SetMutualPreclusion(set, selectedSetIndex, other, index, !mutual);
                    MarkEditor2PreviewDirty(
                        mutual ? "Removed the mutual exclusion." : "Added a mutual exclusion.");
                }
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        private void DrawEditor2SelectedHardpoint()
        {
            Hardpoint hardpoint = SelectedHardpoint();
            if (hardpoint == null)
            {
                GUILayout.Label("Select a physical hardpoint.", GUI.skin.box);
                return;
            }
            Editor2HardpointMetadata metadata = GetEditor2Metadata(hardpoint, false);

            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("SELECTED HARDPOINT · " + Editor2OriginLabel(metadata));
            GUILayout.BeginHorizontal();
            GUILayout.Label("Editor name", GUILayout.Width(92f));
            string displayName = GUILayout.TextField(metadata.DisplayName ?? string.Empty);
            if (!string.Equals(displayName, metadata.DisplayName, StringComparison.Ordinal))
            {
                metadata.DisplayName = displayName;
                status = "Changed editor-only hardpoint metadata.";
            }
            GUILayout.EndHorizontal();
            GUILayout.Label("ID: " + Editor2CompactLabel(metadata.StableId, 58));
            GUILayout.EndVertical();

            DrawEditor2AssemblyLayer(hardpoint);
            DrawEditor2TemplatePicker(hardpoint);
            DrawEditor2AnchorLayer(hardpoint);
            DrawEditor2RootParentPicker(hardpoint);
            DrawEditor2PartPicker(hardpoint);
            DrawEditor2Variants(hardpoint);
            DrawEditor2Advanced(hardpoint);
        }

        private void DrawEditor2AssemblyLayer(Hardpoint hardpoint)
        {
            Transform assembly = ResolvePylonAssemblyRoot(hardpoint);
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("PYLON ASSEMBLY");
            GUILayout.Label(Editor2CompactLabel(GetTransformPath(assembly), 62));
            GUILayout.Label(
                assembly == hardpoint.transform
                    ? "Assembly and anchor share one transform."
                    : "Upper layer: model and anchor move together.");
            if (Editor2Button("Edit assembly transform", Editor2Purple, GUILayout.Height(28f)))
            {
                OpenEditor2TransformPalette(Editor2TransformTargetKind.Assembly);
            }
            GUILayout.EndVertical();
        }

        private void DrawEditor2AnchorLayer(Hardpoint hardpoint)
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("WEAPON MOUNT ANCHOR");
            GUILayout.Label(Editor2CompactLabel(GetTransformPath(hardpoint.transform), 62));
            if (Editor2Button("Edit anchor offset", Editor2Purple, GUILayout.Height(28f)))
            {
                OpenEditor2TransformPalette(Editor2TransformTargetKind.Anchor);
            }
            GUILayout.EndVertical();
        }

        private void DrawEditor2RootParentPicker(Hardpoint hardpoint)
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("WEAPON ANCHOR PARENT");
            GUILayout.Label(Editor2CompactLabel(
                GetTransformPath(hardpoint.transform?.parent),
                62));
            if (Editor2Button(
                    editor2RootParentPickerOpen
                        ? "Close root-parent picker"
                        : "Change root parent",
                    Editor2Purple,
                    GUILayout.Height(27f)))
            {
                editor2RootParentPickerOpen = !editor2RootParentPickerOpen;
            }
            if (editor2RootParentPickerOpen)
            {
                editor2RootParentSearch = GUILayout.TextField(
                    editor2RootParentSearch ?? string.Empty);
                editor2RootParentScroll = GUILayout.BeginScrollView(
                    editor2RootParentScroll,
                    GUILayout.Height(140f));
                int shown = 0;
                foreach (Transform candidate in hardpointRootParentCandidates)
                {
                    string label = GetTransformPath(candidate);
                    if (!MatchesSearch(label, editor2RootParentSearch) ||
                        !IsSelectableHardpointRootParent(hardpoint, candidate))
                    {
                        continue;
                    }
                    if (Editor2Button(
                            Editor2CompactLabel(label, 54),
                            candidate == hardpoint.transform.parent
                                ? Editor2Green
                                : Editor2Neutral,
                            GUILayout.Height(24f)))
                    {
                        if (ReparentHardpointRoot(hardpoint, candidate, out string failure))
                        {
                            RefreshEditor2TransformText();
                            status = "Moved " +
                                     GetEditor2Metadata(hardpoint, false).DisplayName +
                                     " under " + label +
                                     " without changing its aircraft-space placement.";
                        }
                        else
                        {
                            status = "Could not change the hardpoint root parent: " +
                                     failure + ".";
                        }
                    }
                    if (++shown >= 250)
                    {
                        GUILayout.Label("Refine search to show more hierarchy nodes.");
                        break;
                    }
                }
                GUILayout.EndScrollView();
            }
            GUILayout.EndVertical();
        }

        private void DrawEditor2PartPicker(Hardpoint hardpoint)
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("DAMAGEABLE UNITPART");
            GUILayout.Label(Editor2CompactLabel(PartLabel(hardpoint.part), 62));
            GUILayout.BeginHorizontal();
            if (Editor2Button("Choose nearest", Editor2Green, GUILayout.Height(27f)))
            {
                ChooseNearestUnitPart(hardpoint);
            }
            if (Editor2Button(
                    editor2PartPickerOpen ? "Close part picker" : "Change attached part",
                    Editor2Purple,
                    GUILayout.Height(27f)))
            {
                editor2PartPickerOpen = !editor2PartPickerOpen;
            }
            GUILayout.EndHorizontal();
            if (editor2PartPickerOpen)
            {
                editor2PartSearch = GUILayout.TextField(editor2PartSearch ?? string.Empty);
                editor2PartScroll = GUILayout.BeginScrollView(editor2PartScroll, GUILayout.Height(120f));
                foreach (UnitPart part in parts)
                {
                    string label = PartLabel(part);
                    if (!MatchesSearch(label, editor2PartSearch))
                    {
                        continue;
                    }
                    if (Editor2Button(
                            Editor2CompactLabel(label, 54),
                            part == hardpoint.part ? Editor2Green : Editor2Neutral,
                            GUILayout.Height(24f)))
                    {
                        hardpoint.part = part;
                        status = "Attached " + GetEditor2Metadata(hardpoint, false).DisplayName +
                                 " to " + label + ".";
                    }
                }
                GUILayout.EndScrollView();
            }
            GUILayout.EndVertical();
        }

        private void DrawEditor2TemplatePicker(Hardpoint hardpoint)
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("PYLON ASSEMBLY MODEL");
            GUILayout.Label(Editor2CompactLabel(DescribeTemplateSource(hardpoint), 62));
            GUILayout.Label("Slot: " + Editor2CompactLabel(
                GetTransformPath(GetOrCreatePylonStationBinding(hardpoint)?.ModelSlot),
                56));
            if (Editor2Button(
                    editor2TemplatePickerOpen ? "Close model library" : "Choose pylon model",
                    Editor2Purple,
                    GUILayout.Height(27f)))
            {
                editor2TemplatePickerOpen = !editor2TemplatePickerOpen;
            }
            if (editor2TemplatePickerOpen)
            {
                editor2TemplateSearch = GUILayout.TextField(editor2TemplateSearch ?? string.Empty);
                editor2TemplateScroll = GUILayout.BeginScrollView(editor2TemplateScroll, GUILayout.Height(145f));
                int shown = 0;
                for (int index = 0; index < hardpointTemplates.Count; index++)
                {
                    HardpointTemplate template = hardpointTemplates[index];
                    if (!MatchesSearch(template.Label, editor2TemplateSearch))
                    {
                        continue;
                    }
                    if (Editor2Button(
                            Editor2CompactLabel(template.Label, 58),
                            index == selectedHardpointTemplateIndex ? Editor2Green : Editor2Neutral,
                            GUILayout.Height(24f)))
                    {
                        selectedHardpointTemplateIndex = index;
                        ApplySelectedTemplateToHardpoint(hardpoint);
                        weaponUiRefreshPending = true;
                    }
                    if (++shown >= 200)
                    {
                        GUILayout.Label("Refine search to show more models.");
                        break;
                    }
                }
                GUILayout.EndScrollView();
            }
            GUILayout.EndVertical();
        }

        private void DrawEditor2Variants(Hardpoint hardpoint)
        {
            Array variants = GetPylonOptions(hardpoint);
            GUILayout.BeginVertical(GUI.skin.box);
            if (Editor2Button(
                    (editor2VariantsOpen ? "▼ " : "▶ ") +
                    "PYLON VISUAL VARIANTS  [" + variants.Length + "]",
                    editor2VariantsOpen ? Editor2Purple : Editor2Neutral,
                    GUILayout.Height(27f)))
            {
                editor2VariantsOpen = !editor2VariantsOpen;
            }
            if (!editor2VariantsOpen)
            {
                GUILayout.EndVertical();
                return;
            }

            editor2VariantScroll = GUILayout.BeginScrollView(editor2VariantScroll, GUILayout.Height(95f));
            for (int index = 0; index < variants.Length; index++)
            {
                object variant = variants.GetValue(index);
                if (Editor2Button(
                        index + ". " + Editor2CompactLabel(VariantLabel(variant), 48),
                        index == selectedVariantIndex ? Editor2Green : Editor2Neutral,
                        GUILayout.Height(24f)))
                {
                    selectedVariantIndex = index;
                    editor2VariantWeaponPickerOpen = false;
                    editor2VariantRendererPickerOpen = false;
                }
            }
            GUILayout.EndScrollView();
            GUILayout.BeginHorizontal();
            if (Editor2Button("+ Variant", Editor2Purple, GUILayout.Height(26f)))
            {
                selectedVariantIndex = AddPylonVariant(hardpoint);
                MarkEditor2PreviewDirty("Added a pylon visual variant.");
            }
            GUI.enabled = selectedVariantIndex >= 0 && selectedVariantIndex < variants.Length;
            if (Editor2Button("− Variant", Editor2Red, GUILayout.Height(26f)))
            {
                RemovePylonVariant(hardpoint, selectedVariantIndex);
                selectedVariantIndex = -1;
                MarkEditor2PreviewDirty("Removed a pylon visual variant.");
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            variants = GetPylonOptions(hardpoint);
            if (selectedVariantIndex < 0 || selectedVariantIndex >= variants.Length)
            {
                GUILayout.Label("Select a variant to edit its matching rule and renderer.");
                GUILayout.EndVertical();
                return;
            }

            object selectedVariant = variants.GetValue(selectedVariantIndex);
            GUILayout.Label("Match: " + VariantLabel(selectedVariant));
            GUILayout.BeginHorizontal();
            if (Editor2Button("Base", Editor2Purple, GUILayout.Height(25f)))
            {
                SetVariantBase(selectedVariant);
                MarkEditor2PreviewDirty("Set the visual variant to Base.");
            }
            if (Editor2Button("Cargo", Editor2Purple, GUILayout.Height(25f)))
            {
                SetVariantCargo(selectedVariant);
                MarkEditor2PreviewDirty("Set the visual variant to Cargo.");
            }
            if (Editor2Button("Specific weapon…", Editor2Purple, GUILayout.Height(25f)))
            {
                editor2VariantWeaponPickerOpen = !editor2VariantWeaponPickerOpen;
                editor2VariantRendererPickerOpen = false;
            }
            GUILayout.EndHorizontal();

            if (editor2VariantWeaponPickerOpen)
            {
                editor2VariantSearch = GUILayout.TextField(editor2VariantSearch ?? string.Empty);
                editor2VariantChoiceScroll = GUILayout.BeginScrollView(
                    editor2VariantChoiceScroll,
                    GUILayout.Height(100f));
                foreach (WeaponMount mount in allWeapons)
                {
                    string label = WeaponLabel(mount);
                    if (!MatchesSearch(label, editor2VariantSearch))
                    {
                        continue;
                    }
                    if (Editor2Button(
                            Editor2CompactLabel(label, 56),
                            Editor2Neutral,
                            GUILayout.Height(23f)))
                    {
                        SetVariantMount(selectedVariant, mount);
                        editor2VariantWeaponPickerOpen = false;
                        MarkEditor2PreviewDirty("Matched the visual variant to " + label + ".");
                    }
                }
                GUILayout.EndScrollView();
            }

            Renderer currentRenderer = PylonOptionRendererField?.GetValue(selectedVariant) as Renderer;
            GUILayout.Label("Renderer: " + Editor2CompactLabel(RendererLabel(currentRenderer), 54));
            GUILayout.BeginHorizontal();
            if (Editor2Button("Choose renderer…", Editor2Purple, GUILayout.Height(25f)))
            {
                editor2VariantRendererPickerOpen = !editor2VariantRendererPickerOpen;
                editor2VariantWeaponPickerOpen = false;
            }
            if (Editor2Button("None", Editor2Neutral, GUILayout.Width(58f), GUILayout.Height(25f)))
            {
                PylonOptionRendererField?.SetValue(selectedVariant, null);
                MarkEditor2PreviewDirty("Cleared the selected pylon variant renderer.");
            }
            if (Editor2Button("Preview", Editor2Purple, GUILayout.Width(72f), GUILayout.Height(25f)))
            {
                PreviewVariantRenderer(hardpoint, selectedVariant);
                status = "Previewing " + VariantLabel(selectedVariant) + ".";
            }
            GUILayout.EndHorizontal();

            if (editor2VariantRendererPickerOpen)
            {
                editor2VariantSearch = GUILayout.TextField(editor2VariantSearch ?? string.Empty);
                editor2VariantChoiceScroll = GUILayout.BeginScrollView(
                    editor2VariantChoiceScroll,
                    GUILayout.Height(100f));
                foreach (Renderer renderer in renderers)
                {
                    string label = RendererLabel(renderer);
                    if (!MatchesSearch(label, editor2VariantSearch))
                    {
                        continue;
                    }
                    if (Editor2Button(
                            Editor2CompactLabel(label, 56),
                            renderer == currentRenderer ? Editor2Green : Editor2Neutral,
                            GUILayout.Height(23f)))
                    {
                        PylonOptionRendererField?.SetValue(selectedVariant, renderer);
                        editor2VariantRendererPickerOpen = false;
                        MarkEditor2PreviewDirty("Changed the selected pylon variant renderer.");
                    }
                }
                GUILayout.EndScrollView();
            }
            GUILayout.EndVertical();
        }

        private void DrawEditor2Advanced(Hardpoint hardpoint)
        {
            GUILayout.BeginVertical(GUI.skin.box);
            if (Editor2Button(
                    (editor2AdvancedOpen ? "▼ " : "▶ ") + "ADVANCED REFERENCES",
                    editor2AdvancedOpen ? Editor2Purple : Editor2Neutral,
                    GUILayout.Height(27f)))
            {
                editor2AdvancedOpen = !editor2AdvancedOpen;
                editor2DoorTimeText = FormatFloat(hardpoint.doorOpenDuration);
            }
            if (editor2AdvancedOpen)
            {
                GUILayout.Label("Transform: " + Editor2CompactLabel(GetTransformPath(hardpoint.transform), 58));
                GUILayout.Label("HardpointIndex: " + hardpoint.HardpointIndex);
                GUILayout.BeginHorizontal();
                GUILayout.Label("Door time", GUILayout.Width(78f));
                editor2DoorTimeText = GUILayout.TextField(editor2DoorTimeText ?? "0");
                if (float.TryParse(
                        editor2DoorTimeText,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out float doorTime))
                {
                    hardpoint.doorOpenDuration = doorTime;
                }
                GUILayout.EndHorizontal();
                GUILayout.Label("Bay doors: " + (hardpoint.bayDoors?.Length ?? 0));
                if (hardpoint.bayDoors != null)
                {
                    foreach (BayDoor door in hardpoint.bayDoors)
                    {
                        GUILayout.Label("· " + Editor2CompactLabel(GetTransformPath(door?.transform), 54));
                    }
                }
                GUILayout.Label("Legacy pylon: " + Editor2CompactLabel(RendererLabel(hardpoint.Pylon), 54));
                GUILayout.Label("Plug: " + Editor2CompactLabel(RendererLabel(hardpoint.Plug), 54));
                GUILayout.Label("Built-in weapons: " + (hardpoint.BuiltInWeapons?.Length ?? 0));
                GUILayout.Label("Built-in turrets: " + (hardpoint.BuiltInTurrets?.Length ?? 0));
            }
            GUILayout.EndVertical();
        }

        private void DrawEditor2RemoveHardpointSetButton(
            HardpointSet set,
            params GUILayoutOption[] options)
        {
            if (set == null)
            {
                Editor2Button("− HardpointSet", Editor2Neutral, options);
                return;
            }
            Editor2HardpointSetMetadata metadata = GetEditor2HardpointSetMetadata(set, false);
            bool original = metadata.Origin == Editor2NodeOrigin.Original;
            bool armed = original && editor2PendingOriginalSetRemoval == set;
            string label = armed ? "CONFIRM − ORIGINAL SET" : "− HardpointSet";
            Color color = original ? Editor2Red : Editor2Purple;
            if (!Editor2Button(label, color, options))
            {
                return;
            }
            if (original && !armed)
            {
                editor2PendingOriginalSetRemoval = set;
                status = "Press the red button again to remove original HardpointSet " +
                         (set.name ?? "<unnamed>") + ".";
                return;
            }
            editor2PendingOriginalSetRemoval = null;
            RemoveEditor2HardpointSet(set);
        }

        private void AddEditor2HardpointSet()
        {
            if (weaponManager == null || previewAircraft == null)
            {
                status = "No aircraft WeaponManager is available for a new HardpointSet.";
                return;
            }
            HardpointSet[] existing = weaponManager.hardpointSets ?? Array.Empty<HardpointSet>();
            if (existing.Length >= byte.MaxValue + 1)
            {
                status = "The aircraft already uses all 256 byte-addressable HardpointSet indexes.";
                return;
            }

            HardpointSet created = new HardpointSet
            {
                name = CreateEditor2HardpointSetName(existing),
                precludingHardpointSets = new List<byte>(),
                SymmetryWithPrev = false,
                SymmetryName = string.Empty,
                weaponOptions = new List<WeaponMount>(),
                hardpoints = new List<Hardpoint>()
            };
            HardpointSet[] expanded = new HardpointSet[existing.Length + 1];
            Array.Copy(existing, expanded, existing.Length);
            expanded[existing.Length] = created;
            weaponManager.hardpointSets = expanded;
            RegisterEditor2HardpointSet(created, true);

            selectedSetIndex = existing.Length;
            selectedHardpointIndex = -1;
            selectedVariantIndex = -1;
            selectedHardpointTemplateIndex = 0;
            AddHardpoint(created, false);
            OnEditor2SelectionChanged();
            editor2MasterOpen = true;
            MarkEditor2PreviewDirty(
                "Added custom HardpointSet " + created.name +
                " with one no-visual hardpoint at the aircraft root.");
        }

        private void RemoveEditor2HardpointSet(HardpointSet set)
        {
            HardpointSet[] existing = weaponManager?.hardpointSets;
            int removedIndex = existing == null ? -1 : Array.IndexOf(existing, set);
            if (removedIndex < 0)
            {
                status = "The selected HardpointSet is no longer part of this aircraft.";
                return;
            }

            if (set.hardpoints != null)
            {
                foreach (Hardpoint hardpoint in new List<Hardpoint>(set.hardpoints))
                {
                    DisposeRemovedHardpoint(hardpoint);
                }
                set.hardpoints.Clear();
            }
            set.weaponMount = null;
            editor2HardpointSetMetadata.Remove(set);

            HardpointSet[] reduced = new HardpointSet[existing.Length - 1];
            if (removedIndex > 0)
            {
                Array.Copy(existing, 0, reduced, 0, removedIndex);
            }
            if (removedIndex + 1 < existing.Length)
            {
                Array.Copy(
                    existing,
                    removedIndex + 1,
                    reduced,
                    removedIndex,
                    existing.Length - removedIndex - 1);
            }
            weaponManager.hardpointSets = reduced;
            RemapEditor2HardpointSetReferences(reduced, removedIndex);

            selectedSetIndex = reduced.Length == 0
                ? -1
                : Mathf.Clamp(removedIndex, 0, reduced.Length - 1);
            HardpointSet selected = SelectedSet();
            selectedHardpointIndex = selected?.hardpoints != null && selected.hardpoints.Count > 0
                ? 0
                : -1;
            selectedVariantIndex = -1;
            editor2PendingOriginalRemoval = null;
            editor2PendingOriginalSetRemoval = null;
            if (selected == null)
            {
                editor2MasterOpen = false;
                editor2TransformOpen = false;
            }
            RebuildHardpointTemplates();
            RefreshStructureSelection();
            OnEditor2SelectionChanged();
            MarkEditor2PreviewDirty(
                "Removed HardpointSet " + (set.name ?? "<unnamed>") +
                " and remapped its indexed relationships.");
        }

        private static void RemapEditor2HardpointSetReferences(
            HardpointSet[] sets,
            int removedIndex)
        {
            for (int setIndex = 0; setIndex < sets.Length; setIndex++)
            {
                HardpointSet set = sets[setIndex];
                if (set == null)
                {
                    continue;
                }
                List<byte> remapped = new List<byte>();
                if (set.precludingHardpointSets != null)
                {
                    foreach (byte encodedIndex in set.precludingHardpointSets)
                    {
                        int index = encodedIndex;
                        if (index == removedIndex)
                        {
                            continue;
                        }
                        if (index > removedIndex)
                        {
                            index--;
                        }
                        if (index >= 0 && index < sets.Length && index != setIndex &&
                            !remapped.Contains((byte)index))
                        {
                            remapped.Add((byte)index);
                        }
                    }
                }
                set.precludingHardpointSets = remapped;
            }

            if (sets.Length > 0)
            {
                sets[0].SymmetryWithPrev = false;
            }
            if (removedIndex >= 0 && removedIndex < sets.Length)
            {
                sets[removedIndex].SymmetryWithPrev = false;
                sets[removedIndex].SymmetryName = string.Empty;
            }
        }

        private static string CreateEditor2HardpointSetName(HardpointSet[] sets)
        {
            const string baseName = "New HardpointSet";
            string candidate = baseName;
            int suffix = 2;
            while (Array.Exists(
                       sets,
                       set => set != null && string.Equals(
                           set.name,
                           candidate,
                           StringComparison.OrdinalIgnoreCase)))
            {
                candidate = baseName + " " + suffix++;
            }
            return candidate;
        }

        private void DrawEditor2RemoveHardpointButton(
            HardpointSet set,
            Hardpoint hardpoint,
            params GUILayoutOption[] options)
        {
            if (hardpoint == null)
            {
                Editor2Button("− hardpoint", Editor2Neutral, options);
                return;
            }
            Editor2HardpointMetadata metadata = GetEditor2Metadata(hardpoint, false);
            bool original = metadata.Origin == Editor2NodeOrigin.Original;
            bool armed = original && editor2PendingOriginalRemoval == hardpoint;
            string label = armed ? "CONFIRM − ORIGINAL" : "− hardpoint";
            Color color = original ? Editor2Red : Editor2Purple;
            if (!Editor2Button(label, color, options))
            {
                return;
            }
            if (original && !armed)
            {
                editor2PendingOriginalRemoval = hardpoint;
                status = "Press the red button again to remove original hardpoint " +
                         metadata.DisplayName + ".";
                return;
            }
            editor2PendingOriginalRemoval = null;
            RemoveSelectedHardpoint(set);
        }

        private void DrawEditor2TransformWindow(int windowId)
        {
            Hardpoint hardpoint = SelectedHardpoint();
            Transform target = Editor2SelectedTransformTarget(hardpoint);
            if (target == null)
            {
                editor2TransformOpen = false;
                return;
            }
            if (editor2TransformTarget != target)
            {
                editor2TransformTarget = target;
                RefreshEditor2TransformText();
            }
            Editor2HardpointMetadata metadata = GetEditor2Metadata(hardpoint, false);
            GUILayout.Label(
                metadata.DisplayName + " · " +
                (editor2TransformTargetKind == Editor2TransformTargetKind.Assembly
                    ? "Pylon Assembly"
                    : "Weapon Anchor") +
                " · " + Editor2OriginLabel(metadata));

            GUILayout.BeginHorizontal();
            DrawEditor2TransformTargetButton(
                Editor2TransformTargetKind.Assembly,
                "Assembly");
            DrawEditor2TransformTargetButton(
                Editor2TransformTargetKind.Anchor,
                "Anchor");
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            DrawEditor2TransformSpaceButton(Editor2TransformSpace.Aircraft, "Aircraft");
            DrawEditor2TransformSpaceButton(Editor2TransformSpace.Parent, "Parent");
            DrawEditor2TransformSpaceButton(Editor2TransformSpace.World, "World");
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label(string.Empty, GUILayout.Width(46f));
            GUILayout.Label("X", GUILayout.Width(94f));
            GUILayout.Label("Y", GUILayout.Width(94f));
            GUILayout.Label("Z", GUILayout.Width(94f));
            GUILayout.EndHorizontal();
            DrawEditor2TransformRow("Pos", editor2TransformPosition);
            DrawEditor2TransformRow("Rot", editor2TransformRotation);
            DrawEditor2TransformRow("Scale", editor2TransformScale);
            ApplyEditor2TransformText(hardpoint, target);

            GUILayout.BeginHorizontal();
            if (Editor2Button("Reset", Editor2Neutral, GUILayout.Height(27f)))
            {
                if (editor2TransformTargetKind == Editor2TransformTargetKind.Assembly)
                {
                    ResetPylonAssemblyTransform(hardpoint);
                }
                else
                {
                    ResetHardpointTransform(hardpoint);
                }
                RefreshEditor2TransformText();
            }
            if (Editor2Button("Copy", Editor2Purple, GUILayout.Height(27f)))
            {
                editor2TransformClipboard = new TransformSnapshot
                {
                    Target = target,
                    Parent = target.parent,
                    Position = target.localPosition,
                    Rotation = target.localRotation,
                    Scale = target.localScale
                };
                editor2TransformClipboardValid = true;
                status = "Copied the selected transform layer.";
            }
            GUI.enabled = editor2TransformClipboardValid;
            if (Editor2Button("Paste", Editor2Purple, GUILayout.Height(27f)))
            {
                MutateEditor2TransformTarget(
                    hardpoint,
                    target,
                    transform =>
                    {
                        transform.localPosition = editor2TransformClipboard.Position;
                        transform.localRotation = editor2TransformClipboard.Rotation;
                        transform.localScale = editor2TransformClipboard.Scale;
                    });
                RefreshEditor2TransformText();
                status = "Pasted the copied transform onto the selected layer.";
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUI.enabled = Editor2MirrorPartner() != null;
            if (Editor2Button("Mirror → partner", Editor2Purple, GUILayout.Height(27f)))
            {
                Hardpoint partner = Editor2MirrorPartner();
                if (editor2TransformTargetKind == Editor2TransformTargetKind.Assembly)
                {
                    MirrorPylonAssemblyTransform(hardpoint, partner);
                }
                else
                {
                    MirrorWeaponAnchorTransform(hardpoint, partner);
                }
                status = "Mirrored the selected transform layer onto its aircraft-space partner.";
            }
            GUI.enabled = true;
            if (Editor2Button("Focus", Editor2Purple, GUILayout.Height(27f)))
            {
                if (!freeCameraEnabled)
                {
                    SetFreeCameraEnabled(true);
                }
                FocusFreeCameraOnTransform(target);
            }
            if (Editor2Button("Close", Editor2Neutral, GUILayout.Height(27f)))
            {
                editor2TransformOpen = false;
            }
            GUILayout.EndHorizontal();
            DrawWindowChrome(windowId, 390f, 245f);
        }

        private void DrawEditor2TransformTargetButton(
            Editor2TransformTargetKind kind,
            string label)
        {
            if (Editor2Button(
                    label,
                    editor2TransformTargetKind == kind ? Editor2Green : Editor2Neutral,
                    GUILayout.Height(25f)))
            {
                editor2TransformTargetKind = kind;
                editor2TransformTarget = Editor2SelectedTransformTarget(SelectedHardpoint());
                RefreshEditor2TransformText();
            }
        }

        private void DrawEditor2TransformSpaceButton(Editor2TransformSpace space, string label)
        {
            if (Editor2Button(
                    label,
                    editor2TransformSpace == space ? Editor2Green : Editor2Neutral,
                    GUILayout.Height(25f)))
            {
                editor2TransformSpace = space;
                RefreshEditor2TransformText();
            }
        }

        private static void DrawEditor2TransformRow(string label, string[] values)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(46f));
            for (int axis = 0; axis < 3; axis++)
            {
                values[axis] = GUILayout.TextField(values[axis] ?? "0", GUILayout.Width(94f));
            }
            GUILayout.EndHorizontal();
        }

        private void ApplyEditor2TransformText(Hardpoint hardpoint, Transform target)
        {
            if (target == null ||
                !TryParseVector(editor2TransformPosition, out Vector3 position) ||
                !TryParseVector(editor2TransformRotation, out Vector3 rotation) ||
                !TryParseVector(editor2TransformScale, out Vector3 scale))
            {
                return;
            }

            MutateEditor2TransformTarget(hardpoint, target, transform =>
            {
                switch (editor2TransformSpace)
                {
                    case Editor2TransformSpace.Parent:
                        transform.localPosition = position;
                        transform.localRotation = Quaternion.Euler(rotation);
                        transform.localScale = scale;
                        break;
                    case Editor2TransformSpace.World:
                        transform.SetPositionAndRotation(position, Quaternion.Euler(rotation));
                        SetWorldScale(transform, scale);
                        break;
                    default:
                        Transform aircraftTransform = previewAircraft != null
                            ? previewAircraft.transform
                            : transform.root;
                        transform.SetPositionAndRotation(
                            aircraftTransform.TransformPoint(position),
                            aircraftTransform.rotation * Quaternion.Euler(rotation));
                        SetWorldScale(
                            transform,
                            MultiplyComponents(aircraftTransform.lossyScale, scale));
                        break;
                }
            });
        }

        private void RefreshEditor2TransformText()
        {
            Hardpoint hardpoint = SelectedHardpoint();
            Transform transform = Editor2SelectedTransformTarget(hardpoint);
            if (transform == null)
            {
                return;
            }
            editor2TransformTarget = transform;
            Vector3 position;
            Quaternion rotation;
            Vector3 scale;
            switch (editor2TransformSpace)
            {
                case Editor2TransformSpace.Parent:
                    position = transform.localPosition;
                    rotation = transform.localRotation;
                    scale = transform.localScale;
                    break;
                case Editor2TransformSpace.World:
                    position = transform.position;
                    rotation = transform.rotation;
                    scale = transform.lossyScale;
                    break;
                default:
                    Transform aircraftTransform = previewAircraft != null
                        ? previewAircraft.transform
                        : transform.root;
                    position = aircraftTransform.InverseTransformPoint(transform.position);
                    rotation = Quaternion.Inverse(aircraftTransform.rotation) * transform.rotation;
                    scale = DivideComponents(transform.lossyScale, aircraftTransform.lossyScale);
                    break;
            }
            Editor2WriteVector(editor2TransformPosition, position, false);
            Editor2WriteVector(editor2TransformRotation, rotation.eulerAngles, true);
            Editor2WriteVector(editor2TransformScale, scale, false);
        }

        private static void Editor2WriteVector(string[] destination, Vector3 value, bool angles)
        {
            destination[0] = FormatFloat(angles ? NormalizeAngle(value.x) : value.x);
            destination[1] = FormatFloat(angles ? NormalizeAngle(value.y) : value.y);
            destination[2] = FormatFloat(angles ? NormalizeAngle(value.z) : value.z);
        }

        private Hardpoint Editor2MirrorPartner()
        {
            HardpointSet set = SelectedSet();
            if (set?.hardpoints == null || selectedHardpointIndex < 0)
            {
                return null;
            }
            int partnerIndex = selectedHardpointIndex % 2 == 0
                ? selectedHardpointIndex + 1
                : selectedHardpointIndex - 1;
            return partnerIndex >= 0 && partnerIndex < set.hardpoints.Count
                ? set.hardpoints[partnerIndex]
                : null;
        }

        private void OpenEditor2TransformPalette()
        {
            OpenEditor2TransformPalette(Editor2TransformTargetKind.Assembly);
        }

        private void OpenEditor2TransformPalette(Editor2TransformTargetKind kind)
        {
            if (SelectedHardpoint()?.transform == null)
            {
                status = "Select a physical hardpoint before opening its transform.";
                return;
            }
            editor2TransformTargetKind = kind;
            editor2TransformOpen = true;
            editor2TransformTarget = Editor2SelectedTransformTarget(SelectedHardpoint());
            RefreshEditor2TransformText();
        }

        private Transform Editor2SelectedTransformTarget(Hardpoint hardpoint)
        {
            if (hardpoint?.transform == null)
            {
                return null;
            }
            return editor2TransformTargetKind == Editor2TransformTargetKind.Assembly
                ? ResolvePylonAssemblyRoot(hardpoint)
                : hardpoint.transform;
        }

        private void MutateEditor2TransformTarget(
            Hardpoint hardpoint,
            Transform target,
            Action<Transform> mutation)
        {
            if (editor2TransformTargetKind == Editor2TransformTargetKind.Assembly)
            {
                MutatePylonAssemblyTransform(hardpoint, target, mutation);
            }
            else
            {
                MutateWeaponAnchorTransform(hardpoint, mutation);
            }
        }

        private void ValidateEditor2Pylon(HardpointSet set)
        {
            List<string> problems = new List<string>();
            if (string.IsNullOrWhiteSpace(set?.name))
            {
                problems.Add("selector name is empty");
            }
            if (set?.hardpoints == null || set.hardpoints.Count == 0)
            {
                problems.Add("no physical hardpoints");
            }
            else
            {
                for (int index = 0; index < set.hardpoints.Count; index++)
                {
                    Hardpoint hardpoint = set.hardpoints[index];
                    if (hardpoint?.transform == null)
                    {
                        problems.Add("HP " + index + " has no mount transform");
                    }
                    else if (hardpoint.transform.parent == null)
                    {
                        problems.Add("HP " + index + " has no root parent");
                    }
                    else if (previewAircraft != null &&
                             hardpoint.transform.parent != previewAircraft.transform &&
                             !hardpoint.transform.parent.IsChildOf(previewAircraft.transform))
                    {
                        problems.Add("HP " + index + " root parent is outside the aircraft");
                    }
                    if (hardpoint?.part == null)
                    {
                        problems.Add("HP " + index + " has no attached UnitPart");
                    }
                }
            }
            if (set != null && set.SymmetryWithPrev && selectedSetIndex <= 0)
            {
                problems.Add("same-store link has no previous pylon");
            }
            if (set?.precludingHardpointSets != null)
            {
                foreach (byte index in set.precludingHardpointSets)
                {
                    if (weaponManager?.hardpointSets == null || index >= weaponManager.hardpointSets.Length)
                    {
                        problems.Add("exclusion index " + index + " is out of range");
                    }
                }
            }
            status = problems.Count == 0
                ? "Validation passed for " + set.name + "."
                : "Validation: " + string.Join("; ", problems.ToArray()) + ".";
        }

        private void ResetEditor2ForAircraft()
        {
            editor2HardpointMetadata.Clear();
            editor2HardpointSetMetadata.Clear();
            editor2PendingOriginalRemoval = null;
            editor2PendingOriginalSetRemoval = null;
            editor2MasterOpen = false;
            editor2TransformOpen = false;
            editor2TransformTarget = null;
            editor2RootParentPickerOpen = false;
            editor2PartPickerOpen = false;
            editor2TemplatePickerOpen = false;
            editor2VariantsOpen = false;
            editor2AdvancedOpen = false;

            if (weaponManager?.hardpointSets == null)
            {
                return;
            }
            for (int setIndex = 0; setIndex < weaponManager.hardpointSets.Length; setIndex++)
            {
                HardpointSet set = weaponManager.hardpointSets[setIndex];
                if (set == null)
                {
                    continue;
                }
                RegisterEditor2HardpointSet(set, false);
                if (set.hardpoints == null)
                {
                    continue;
                }
                for (int hardpointIndex = 0; hardpointIndex < set.hardpoints.Count; hardpointIndex++)
                {
                    RegisterEditor2Hardpoint(set.hardpoints[hardpointIndex], false);
                }
            }

            if (smokeTestRequested && !editor2HardpointSetSmokeValidated)
            {
                editor2HardpointSetSmokeValidated =
                    RunEditor2HardpointSetLiveSmokeTest(out string hardpointSetFailure);
                if (editor2HardpointSetSmokeValidated)
                {
                    log.LogInfo(
                        "HPW Editor 2 HardpointSet live smoke test passed: append-only set " +
                        "creation, custom provenance, initial hardpoint construction, arbitrary " +
                        "renaming, relationship remapping, and deterministic removal were validated.");
                }
                else
                {
                    log.LogError(
                        "HPW Editor 2 HardpointSet live smoke test failed: " +
                        hardpointSetFailure + ".");
                }
            }
            if (weaponManager.hardpointSets.Length > 0)
            {
                selectedSetIndex = 0;
                HardpointSet first = weaponManager.hardpointSets[0];
                selectedHardpointIndex = first?.hardpoints != null && first.hardpoints.Count > 0 ? 0 : -1;
                RefreshStructureSelection();
                OnEditor2SelectionChanged();

                if (smokeTestRequested && SelectedHardpoint()?.transform != null)
                {
                    editor2MasterOpen = true;
                    OpenEditor2TransformPalette();
                    status = "HPW Editor 2 smoke test: navigator, master configurator, and transform palette are open.";
                }
            }
        }

        private Editor2HardpointSetMetadata RegisterEditor2HardpointSet(
            HardpointSet set,
            bool custom)
        {
            if (set == null)
            {
                return null;
            }
            if (editor2HardpointSetMetadata.TryGetValue(
                    set,
                    out Editor2HardpointSetMetadata existing))
            {
                if (custom)
                {
                    existing.Origin = Editor2NodeOrigin.Custom;
                }
                return existing;
            }
            int setIndex = weaponManager?.hardpointSets == null
                ? -1
                : Array.IndexOf(weaponManager.hardpointSets, set);
            Editor2HardpointSetMetadata metadata = new Editor2HardpointSetMetadata
            {
                StableId = custom
                    ? "custom-set:" + Guid.NewGuid().ToString("N")
                    : (previewAircraft?.definition?.jsonKey ?? "aircraft") +
                      ":set:" + setIndex + ":" + (set.name ?? "unnamed"),
                Origin = custom ? Editor2NodeOrigin.Custom : Editor2NodeOrigin.Original
            };
            editor2HardpointSetMetadata.Add(set, metadata);
            return metadata;
        }

        private Editor2HardpointSetMetadata GetEditor2HardpointSetMetadata(
            HardpointSet set,
            bool customIfNew)
        {
            return RegisterEditor2HardpointSet(set, customIfNew);
        }

        private Editor2HardpointMetadata RegisterEditor2Hardpoint(Hardpoint hardpoint, bool custom)
        {
            if (hardpoint == null)
            {
                return null;
            }
            if (editor2HardpointMetadata.TryGetValue(hardpoint, out Editor2HardpointMetadata existing))
            {
                if (custom)
                {
                    existing.Origin = Editor2NodeOrigin.Custom;
                }
                return existing;
            }
            FindEditor2HardpointIndexes(hardpoint, out int setIndex, out int hardpointIndex);
            Editor2HardpointMetadata metadata = new Editor2HardpointMetadata
            {
                StableId = custom
                    ? "custom:" + Guid.NewGuid().ToString("N")
                    : (previewAircraft?.definition?.jsonKey ?? "aircraft") + ":set:" + setIndex +
                      ":hp:" + hardpointIndex + ":" + GetTransformPath(hardpoint.transform),
                DisplayName = CreateEditor2HardpointDisplayName(hardpoint, setIndex, hardpointIndex),
                Origin = custom ? Editor2NodeOrigin.Custom : Editor2NodeOrigin.Original
            };
            editor2HardpointMetadata.Add(hardpoint, metadata);
            return metadata;
        }

        private Editor2HardpointMetadata GetEditor2Metadata(Hardpoint hardpoint, bool customIfNew)
        {
            return RegisterEditor2Hardpoint(hardpoint, customIfNew);
        }

        private void ForgetEditor2Hardpoint(Hardpoint hardpoint)
        {
            if (hardpoint != null)
            {
                editor2HardpointMetadata.Remove(hardpoint);
            }
            if (hardpoint != null &&
                (editor2TransformTarget == hardpoint.transform ||
                 editor2TransformTarget == ResolvePylonAssemblyRoot(hardpoint)))
            {
                editor2TransformTarget = null;
            }
            if (editor2PendingOriginalRemoval == hardpoint)
            {
                editor2PendingOriginalRemoval = null;
            }
        }

        private void FindEditor2HardpointIndexes(
            Hardpoint hardpoint,
            out int setIndex,
            out int hardpointIndex)
        {
            setIndex = -1;
            hardpointIndex = -1;
            if (weaponManager?.hardpointSets == null)
            {
                return;
            }
            for (int candidateSet = 0; candidateSet < weaponManager.hardpointSets.Length; candidateSet++)
            {
                List<Hardpoint> hardpoints = weaponManager.hardpointSets[candidateSet]?.hardpoints;
                int candidateHardpoint = hardpoints?.IndexOf(hardpoint) ?? -1;
                if (candidateHardpoint < 0)
                {
                    continue;
                }
                setIndex = candidateSet;
                hardpointIndex = candidateHardpoint;
                return;
            }
        }

        private string CreateEditor2HardpointDisplayName(
            Hardpoint hardpoint,
            int setIndex,
            int hardpointIndex)
        {
            string transformName = hardpoint?.transform?.name ?? string.Empty;
            string lower = transformName.ToLowerInvariant();
            if (lower.Contains("left") || lower.EndsWith("_l") || lower.EndsWith(" l"))
            {
                return "Hardpoint left";
            }
            if (lower.Contains("right") || lower.EndsWith("_r") || lower.EndsWith(" r"))
            {
                return "Hardpoint right";
            }
            if (hardpoint?.transform != null && previewAircraft != null)
            {
                float aircraftX = previewAircraft.transform
                    .InverseTransformPoint(hardpoint.transform.position).x;
                HardpointSet set = setIndex >= 0 && setIndex < weaponManager.hardpointSets.Length
                    ? weaponManager.hardpointSets[setIndex]
                    : null;
                if (set?.hardpoints?.Count == 2 && Mathf.Abs(aircraftX) > 0.01f)
                {
                    return aircraftX < 0f ? "Hardpoint left" : "Hardpoint right";
                }
                if (set?.hardpoints?.Count == 1 && Mathf.Abs(aircraftX) <= 0.01f)
                {
                    return "Hardpoint center";
                }
            }
            return "Hardpoint " + (Math.Max(0, hardpointIndex) + 1);
        }

        private static string Editor2OriginLabel(Editor2HardpointMetadata metadata)
        {
            return metadata?.Origin == Editor2NodeOrigin.Custom ? "CUSTOM" : "ORIGINAL";
        }

        private static string Editor2SetOriginLabel(Editor2HardpointSetMetadata metadata)
        {
            return metadata?.Origin == Editor2NodeOrigin.Custom ? "CUSTOM" : "ORIGINAL";
        }

        private void OnEditor2SelectionChanged()
        {
            editor2PendingOriginalRemoval = null;
            editor2PendingOriginalSetRemoval = null;
            editor2RootParentPickerOpen = false;
            Hardpoint hardpoint = SelectedHardpoint();
            if (hardpoint != null)
            {
                GetEditor2Metadata(hardpoint, createdHardpoints.Contains(hardpoint));
                editor2DoorTimeText = FormatFloat(hardpoint.doorOpenDuration);
            }
            if (editor2TransformOpen)
            {
                editor2TransformTarget = Editor2SelectedTransformTarget(hardpoint);
                RefreshEditor2TransformText();
            }
        }

        private void AppendEditor2HardpointMetadata(StringBuilder output, Hardpoint hardpoint)
        {
            Editor2HardpointMetadata metadata = GetEditor2Metadata(
                hardpoint,
                createdHardpoints.Contains(hardpoint));
            output.AppendLine("    EditorId: " + (metadata?.StableId ?? "<none>"));
            output.AppendLine("    EditorName: " + (metadata?.DisplayName ?? "<none>"));
            output.AppendLine("    EditorOrigin: " + Editor2OriginLabel(metadata));
        }

        private void AppendEditor2HardpointSetMetadata(
            StringBuilder output,
            HardpointSet set)
        {
            Editor2HardpointSetMetadata metadata = GetEditor2HardpointSetMetadata(set, false);
            output.AppendLine("  EditorSetId: " + (metadata?.StableId ?? "<none>"));
            output.AppendLine("  EditorSetOrigin: " + Editor2SetOriginLabel(metadata));
        }

        private void EnsureEditor2LayoutLoaded()
        {
            if (!editor2LayoutLoaded)
            {
                LoadEditor2WindowLayouts();
            }
        }

        private void LoadEditor2WindowLayouts()
        {
            editor2MainWindowRect = LoadRect("Editor2Main", editor2MainWindowRect);
            editor2MasterWindowRect = LoadRect("Editor2Master", editor2MasterWindowRect);
            editor2TransformWindowRect = LoadRect("Editor2Transform", editor2TransformWindowRect);
            editor2LayoutLoaded = true;
        }

        private void SaveEditor2WindowLayouts()
        {
            SaveRect("Editor2Main", editor2MainWindowRect);
            SaveRect("Editor2Master", editor2MasterWindowRect);
            SaveRect("Editor2Transform", editor2TransformWindowRect);
        }

        private void DisposeEditor2()
        {
            editor2HardpointMetadata.Clear();
            editor2HardpointSetMetadata.Clear();
            editor2WeaponClipboard.Clear();
            editor2PendingOriginalRemoval = null;
            editor2PendingOriginalSetRemoval = null;
            editor2TransformTarget = null;
            editor2RootParentPickerOpen = false;
            editor2MasterOpen = false;
            editor2TransformOpen = false;
        }

        private bool RunEditor2HardpointSetLiveSmokeTest(out string failure)
        {
            failure = string.Empty;
            HardpointSet[] originalSets = weaponManager?.hardpointSets;
            if (originalSets == null || originalSets.Length == 0)
            {
                failure = "the preview aircraft has no original HardpointSet array";
                return false;
            }

            HardpointSet[] originalReferences = (HardpointSet[])originalSets.Clone();
            List<byte>[] originalExclusions = new List<byte>[originalSets.Length];
            bool[] originalSymmetry = new bool[originalSets.Length];
            string[] originalSymmetryNames = new string[originalSets.Length];
            for (int index = 0; index < originalSets.Length; index++)
            {
                originalExclusions[index] = originalSets[index]?.precludingHardpointSets == null
                    ? new List<byte>()
                    : new List<byte>(originalSets[index].precludingHardpointSets);
                originalSymmetry[index] = originalSets[index]?.SymmetryWithPrev ?? false;
                originalSymmetryNames[index] = originalSets[index]?.SymmetryName;
            }

            int previousSetIndex = selectedSetIndex;
            int previousHardpointIndex = selectedHardpointIndex;
            int previousTemplateIndex = selectedHardpointTemplateIndex;
            bool previousMasterOpen = editor2MasterOpen;
            bool previousTransformOpen = editor2TransformOpen;
            bool previousRefreshPending = weaponUiRefreshPending;
            string previousStatus = status;

            AddEditor2HardpointSet();
            HardpointSet created = weaponManager.hardpointSets.Length == originalSets.Length + 1
                ? weaponManager.hardpointSets[weaponManager.hardpointSets.Length - 1]
                : null;
            Editor2HardpointSetMetadata createdMetadata =
                GetEditor2HardpointSetMetadata(created, false);
            Hardpoint createdHardpoint = created?.hardpoints != null && created.hardpoints.Count > 0
                ? created.hardpoints[0]
                : null;
            bool creationPassed = weaponManager.hardpointSets.Length == originalSets.Length + 1 &&
                                  created != null &&
                                  createdMetadata?.Origin == Editor2NodeOrigin.Custom &&
                                  created.hardpoints?.Count == 1 &&
                                  createdHardpoint?.transform != null &&
                                  GetEditor2Metadata(createdHardpoint, false)?.Origin ==
                                  Editor2NodeOrigin.Custom;
            if (created != null)
            {
                created.name = "TGBR Arbitrary Renamed HardpointSet";
                if (originalSets.Length <= byte.MaxValue &&
                    originalSets[0]?.precludingHardpointSets != null)
                {
                    originalSets[0].precludingHardpointSets.Add((byte)originalSets.Length);
                }
                RemoveEditor2HardpointSet(created);
            }

            HardpointSet[] restoredSets = weaponManager.hardpointSets;
            bool removalPassed = restoredSets.Length == originalReferences.Length;
            for (int index = 0; removalPassed && index < restoredSets.Length; index++)
            {
                removalPassed = ReferenceEquals(restoredSets[index], originalReferences[index]) &&
                                Editor2ByteListsEqual(
                                    restoredSets[index]?.precludingHardpointSets,
                                    originalExclusions[index]) &&
                                (restoredSets[index]?.SymmetryWithPrev ?? false) ==
                                originalSymmetry[index] &&
                                string.Equals(
                                    restoredSets[index]?.SymmetryName,
                                    originalSymmetryNames[index],
                                    StringComparison.Ordinal);
            }
            bool cleanupPassed = created == null ||
                                 (!editor2HardpointSetMetadata.ContainsKey(created) &&
                                  (createdHardpoint == null ||
                                   (!createdHardpoints.Contains(createdHardpoint) &&
                                    !editor2HardpointMetadata.ContainsKey(createdHardpoint))));

            selectedSetIndex = previousSetIndex;
            selectedHardpointIndex = previousHardpointIndex;
            selectedHardpointTemplateIndex = previousTemplateIndex;
            editor2MasterOpen = previousMasterOpen;
            editor2TransformOpen = previousTransformOpen;
            weaponUiRefreshPending = previousRefreshPending;
            status = previousStatus;
            RefreshStructureSelection();
            OnEditor2SelectionChanged();

            if (!creationPassed || !removalPassed || !cleanupPassed)
            {
                failure = !creationPassed
                    ? "custom set creation, provenance, or initial hardpoint construction failed"
                    : (!removalPassed
                        ? "set removal did not restore array order, exclusions, or symmetry"
                        : "custom set metadata or hardpoint cleanup remained registered");
                return false;
            }
            return true;
        }

        private static bool Editor2ByteListsEqual(List<byte> left, List<byte> right)
        {
            int leftCount = left?.Count ?? 0;
            int rightCount = right?.Count ?? 0;
            if (leftCount != rightCount)
            {
                return false;
            }
            for (int index = 0; index < leftCount; index++)
            {
                if (left[index] != right[index])
                {
                    return false;
                }
            }
            return true;
        }

        private static bool RunEditor2SelfTest()
        {
            HardpointSet preceding = new HardpointSet
            {
                precludingHardpointSets = new List<byte> { 1, 2 }
            };
            HardpointSet downstream = new HardpointSet
            {
                precludingHardpointSets = new List<byte> { 0 },
                SymmetryWithPrev = true,
                SymmetryName = "Combined"
            };
            RemapEditor2HardpointSetReferences(
                new[] { preceding, downstream },
                removedIndex: 1);
            bool remapPassed = preceding.precludingHardpointSets.Count == 1 &&
                               preceding.precludingHardpointSets[0] == 1 &&
                               !downstream.SymmetryWithPrev &&
                               string.IsNullOrEmpty(downstream.SymmetryName);
            bool namingPassed = CreateEditor2HardpointSetName(new[]
            {
                new HardpointSet { name = "New HardpointSet" },
                new HardpointSet { name = "New HardpointSet 2" }
            }) == "New HardpointSet 3";
            return remapPassed &&
                   namingPassed &&
                   Editor2MainWindowId != Editor2MasterWindowId &&
                   Editor2MasterWindowId != Editor2TransformWindowId &&
                   Editor2Purple.b > Editor2Purple.r &&
                   Editor2Green.g > Editor2Green.r &&
                   Editor2Red.r > Editor2Red.g &&
                   Enum.IsDefined(typeof(Editor2TransformSpace), Editor2TransformSpace.Aircraft);
        }

        private static bool Editor2Button(string label, Color color, params GUILayoutOption[] options)
        {
            Color previous = GUI.backgroundColor;
            GUI.backgroundColor = color;
            bool clicked = GUILayout.Button(label, options);
            GUI.backgroundColor = previous;
            return clicked;
        }

        private static string Editor2CompactLabel(string value, int maximumLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maximumLength)
            {
                return value ?? string.Empty;
            }
            return value.Substring(0, Math.Max(1, maximumLength - 1)) + "…";
        }

        private void MarkEditor2PreviewDirty(string message)
        {
            weaponUiRefreshPending = true;
            status = message + " Apply the config preview to rebuild selector stores.";
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using BepInEx.Logging;
using HarmonyLib;
using TheGreatBigRebalancing.Balance;
using UnityEngine;

namespace TheGreatBigRebalancing.Tools
{
    internal sealed partial class AircraftSelectorPylonEditor
    {
        private const float UiScale = 0.85f;
        private const string LayoutKeyPrefix = "TGBR.PylonEditor.Layout.";
        private const string SmokeTestEnvironmentVariable = "TGBR_GUI_SMOKE_TEST";
        private const string SmokeTestCommandLineArgument = "--tgbr-gui-smoke-test";
        private const CursorFlags SmokeCursorFlag = (CursorFlags)0x40000000;

        private static readonly FieldInfo PreviewAircraftField =
            AccessTools.Field(typeof(AircraftSelectionMenu), "previewAircraft");

        private static readonly FieldInfo LoadoutSelectorField =
            AccessTools.Field(typeof(AircraftSelectionMenu), "loadoutSelector");

        private static readonly FieldInfo PylonOptionsField =
            AccessTools.Field(typeof(Hardpoint), "pylonOptions");

        private static readonly Type PylonOptionType = PylonOptionsField?.FieldType.GetElementType();
        private static readonly FieldInfo PylonOptionCargoField =
            PylonOptionType == null ? null : AccessTools.Field(PylonOptionType, "cargo");
        private static readonly FieldInfo PylonOptionMountField =
            PylonOptionType == null ? null : AccessTools.Field(PylonOptionType, "mount");
        private static readonly FieldInfo PylonOptionRendererField =
            PylonOptionType == null ? null : AccessTools.Field(PylonOptionType, "renderer");

        private static AircraftSelectionMenu activeSelectorHint;

        private readonly ManualLogSource log;
        private readonly bool smokeTestRequested;
        private readonly Dictionary<Hardpoint, Vector3> resetPositions =
            new Dictionary<Hardpoint, Vector3>();
        private readonly List<UnitPart> parts = new List<UnitPart>();
        private readonly List<WeaponMount> allWeapons = new List<WeaponMount>();
        private readonly List<Renderer> renderers = new List<Renderer>();
        private readonly WeaponMountWorkshop weaponWorkshop;

        private AircraftSelectionMenu selector;
        private LoadoutSelector loadoutSelector;
        private Aircraft previewAircraft;
        private WeaponManager weaponManager;
        private int selectedSetIndex = -1;
        private int selectedHardpointIndex = -1;
        private int selectedVariantIndex = -1;
        private float nextTargetRefresh;
        private bool layoutDirty;
        private float nextLayoutSave;
        private int resizingWindowId = -1;
        private Vector2 pendingResizeDelta;
        private Rect drawingWindowRect;
        private bool weaponUiRefreshPending;
        private bool smokeGuiRendered;
        private bool smokeModelValidated;
        private bool weaponWorkshopSmokeValidated;
        private bool initialSelectorDiscoveryPending = true;
        private string status = "Select an aircraft to begin editing its pylons.";

        internal AircraftSelectorPylonEditor(ManualLogSource log)
        {
            this.log = log;
            activeInstance = this;
            weaponWorkshop = new WeaponMountWorkshop(log);
            LoadWindowLayout();
            smokeTestRequested = IsSmokeTestRequested();

            if (smokeTestRequested)
            {
                log.LogInfo("Aircraft-selector pylon editor GUI smoke test requested.");
            }

            if (RunSelfTest())
            {
                log.LogInfo("Aircraft-selector pylon editor self-test passed.");
            }
            else
            {
                log.LogError("Aircraft-selector pylon editor self-test failed.");
            }
        }

        internal void Update()
        {
            UpdateNearestPartFeedback();
            if (layoutDirty && Time.unscaledTime >= nextLayoutSave)
            {
                SaveWindowLayout();
            }

            if (Time.unscaledTime < nextTargetRefresh)
            {
                return;
            }

            nextTargetRefresh = Time.unscaledTime + 0.25f;
            RefreshSelectionTarget();
        }

        internal void OnGUI()
        {
            if (!ShouldDraw())
            {
                return;
            }

            Matrix4x4 previousMatrix = GUI.matrix;
            try
            {
                GUI.matrix = Matrix4x4.Scale(new Vector3(UiScale, UiScale, 1f));
                float virtualWidth = Screen.width / UiScale;
                float virtualHeight = Screen.height / UiScale;

                DrawEditor2Gui(virtualWidth, virtualHeight);
            }
            finally
            {
                GUI.matrix = previousMatrix;
            }

            if (smokeTestRequested && !smokeGuiRendered)
            {
                smokeGuiRendered = true;
                log.LogInfo("Aircraft-selector pylon editor GUI smoke test rendered successfully.");
            }
        }

        internal void Dispose()
        {
            DisposeNearestPartFeedback();
            DisposeStructureEditor();
            SaveWindowLayout();
            weaponWorkshop.Dispose();
            CursorManager.SetFlag(SmokeCursorFlag, false);
            selector = null;
            activeSelectorHint = null;
            previewAircraft = null;
            weaponManager = null;
            if (activeInstance == this)
            {
                activeInstance = null;
            }
        }

        private void RefreshSelectionTarget()
        {
            AircraftSelectionMenu nextSelector = activeSelectorHint;
            if (nextSelector != null && !nextSelector.isActiveAndEnabled)
            {
                activeSelectorHint = null;
                nextSelector = null;
            }
            if (nextSelector == null && initialSelectorDiscoveryPending)
            {
                initialSelectorDiscoveryPending = false;
                nextSelector = FindActiveSelector();
                activeSelectorHint = nextSelector;
            }
            Aircraft nextAircraft = nextSelector == null
                ? null
                : PreviewAircraftField?.GetValue(nextSelector) as Aircraft;

            if (nextAircraft == null && smokeTestRequested)
            {
                nextAircraft = FindSmokeTestAircraft();
                CursorManager.SetFlag(SmokeCursorFlag, nextAircraft != null);
            }
            else if (smokeTestRequested)
            {
                CursorManager.SetFlag(SmokeCursorFlag, false);
            }

            if (nextAircraft == previewAircraft && nextSelector == selector)
            {
                return;
            }

            selector = nextSelector;
            loadoutSelector = selector == null
                ? null
                : LoadoutSelectorField?.GetValue(selector) as LoadoutSelector;
            SetTarget(nextAircraft);
        }

        private static Aircraft FindSmokeTestAircraft()
        {
            WeaponMount workshopSource = Encyclopedia.i?.weaponMounts?.Find(
                mount => mount != null && string.Equals(
                    mount.jsonKey,
                    "TGBR_F16VX_bomb_250_triple",
                    StringComparison.OrdinalIgnoreCase));
            if (workshopSource?.prefab == null)
            {
                return null;
            }
            AircraftDefinition ternion = Encyclopedia.i?.aircraft?.Find(
                definition => definition != null && string.Equals(
                    definition.jsonKey,
                    "P_Trisurface1",
                    StringComparison.OrdinalIgnoreCase));
            GameObject prefab = ternion?.unitPrefab;
            return prefab?.GetComponent<Aircraft>() ??
                   prefab?.GetComponentInChildren<Aircraft>(true) ??
                   F99PylonExpansion.PrefabAircraft;
        }

        private void SetTarget(Aircraft aircraft)
        {
            StopNearestPartFeedback();
            previewAircraft = aircraft;
            weaponManager = previewAircraft?.weaponManager;
            selectedSetIndex = -1;
            selectedHardpointIndex = -1;
            selectedVariantIndex = -1;
            weaponUiRefreshPending = false;
            resetPositions.Clear();
            parts.Clear();
            renderers.Clear();
            allWeapons.Clear();

            if (weaponManager?.hardpointSets == null)
            {
                status = "The selected aircraft has no editable WeaponManager.";
                return;
            }

            foreach (HardpointSet set in weaponManager.hardpointSets)
            {
                if (set?.hardpoints == null)
                {
                    continue;
                }

                foreach (Hardpoint hardpoint in set.hardpoints)
                {
                    if (hardpoint?.transform != null)
                    {
                        resetPositions[hardpoint] = hardpoint.transform.localPosition;
                    }
                }
            }

            parts.AddRange(previewAircraft.GetComponentsInChildren<UnitPart>(true));
            parts.Sort((left, right) => string.Compare(
                GetTransformPath(left?.transform),
                GetTransformPath(right?.transform),
                StringComparison.OrdinalIgnoreCase));

            renderers.AddRange(previewAircraft.GetComponentsInChildren<Renderer>(true));
            renderers.Sort((left, right) => string.Compare(
                GetTransformPath(left?.transform),
                GetTransformPath(right?.transform),
                StringComparison.OrdinalIgnoreCase));

            Encyclopedia encyclopedia = Encyclopedia.i;
            if (encyclopedia?.weaponMounts != null)
            {
                foreach (WeaponMount mount in encyclopedia.weaponMounts)
                {
                    if (mount != null &&
                        !GlobalScimitarRemoval.IsScimitar(mount) &&
                        !allWeapons.Contains(mount))
                    {
                        allWeapons.Add(mount);
                    }
                }
            }

            foreach (HardpointSet set in weaponManager.hardpointSets)
            {
                if (set?.weaponOptions == null)
                {
                    continue;
                }

                foreach (WeaponMount mount in set.weaponOptions)
                {
                    if (mount != null &&
                        !GlobalScimitarRemoval.IsScimitar(mount) &&
                        !allWeapons.Contains(mount))
                    {
                        allWeapons.Add(mount);
                    }
                }
            }

            allWeapons.Sort((left, right) => string.Compare(
                WeaponLabel(left),
                WeaponLabel(right),
                StringComparison.OrdinalIgnoreCase));

            ResetStructureEditorForAircraft();

            if (smokeTestRequested && !weaponWorkshopSmokeValidated)
            {
                HardpointSet smokeSet = Array.Find(
                    weaponManager.hardpointSets,
                    candidate => candidate != null);
                weaponWorkshopSmokeValidated = weaponWorkshop.RunLiveSmokeTest(
                    smokeSet,
                    allWeapons,
                    out string workshopFailure);
                if (weaponWorkshopSmokeValidated)
                {
                    log.LogInfo(
                        "Mount & rack workshop live smoke test passed: the Viper II GBU-38 " +
                        "three-store assembly became two complete rack assemblies with six " +
                        "stores and also became one genuine individual-store mount; source-store " +
                        "selection, prototype-registry reopening, rack/adapter handling, live " +
                        "hierarchy transforms, " +
                        "encyclopedia identity, pylon allow-list, and cleanup were validated.");
                }
                else
                {
                    log.LogError(
                        "Mount & rack workshop live smoke test failed: " +
                        workshopFailure + ".");
                }
            }

            status = "Editing " + AircraftLabel() + ". Changes are live on the selector preview.";
            ValidateSmokeModel();
        }

        private void SelectPylon(int setIndex)
        {
            selectedSetIndex = setIndex;
            HardpointSet set = SelectedSet();
            selectedHardpointIndex = set?.hardpoints != null && set.hardpoints.Count > 0 ? 0 : -1;
            selectedVariantIndex = -1;
            RefreshStructureSelection();
            OnEditor2SelectionChanged();
        }

        private void SelectHardpoint(int hardpointIndex)
        {
            selectedHardpointIndex = hardpointIndex;
            selectedVariantIndex = -1;
            RefreshStructureSelection();
            OnEditor2SelectionChanged();
        }

        private HardpointSet SelectedSet()
        {
            if (weaponManager?.hardpointSets == null ||
                selectedSetIndex < 0 ||
                selectedSetIndex >= weaponManager.hardpointSets.Length)
            {
                return null;
            }
            return weaponManager.hardpointSets[selectedSetIndex];
        }

        private Hardpoint SelectedHardpoint()
        {
            HardpointSet set = SelectedSet();
            if (set?.hardpoints == null ||
                selectedHardpointIndex < 0 ||
                selectedHardpointIndex >= set.hardpoints.Count)
            {
                return null;
            }
            return set.hardpoints[selectedHardpointIndex];
        }

        private void RefreshSelectorWeaponUi()
        {
            if (loadoutSelector == null)
            {
                status = "No active selector LoadoutSelector was available to refresh.";
                return;
            }
            loadoutSelector.ShowHardpoints();
            loadoutSelector.UpdateWeapons(respawnWeapons: true);
            weaponUiRefreshPending = false;
            status = "Weapon limits applied; selector dropdowns and preview stores were rebuilt.";
        }

        private void PrintAndCopyConfiguration()
        {
            string output = FormatConfiguration();
            log.LogInfo(output);
            try
            {
                GUIUtility.systemCopyBuffer = output;
                status = "Printed the selected aircraft configuration to BepInEx and copied it to the clipboard.";
            }
            catch (Exception exception)
            {
                status = "Printed configuration; clipboard copy failed: " + exception.Message;
                log.LogWarning(status);
            }
        }

        private string FormatConfiguration()
        {
            StringBuilder output = new StringBuilder();
            output.AppendLine("TGBR AIRCRAFT PYLON CONFIGURATION");
            output.AppendLine("Aircraft: " + AircraftLabel());
            output.AppendLine("JsonKey: " + (previewAircraft?.definition?.jsonKey ?? "<none>"));
            if (weaponManager?.hardpointSets == null)
            {
                return output.ToString().TrimEnd();
            }

            for (int setIndex = 0; setIndex < weaponManager.hardpointSets.Length; setIndex++)
            {
                HardpointSet set = weaponManager.hardpointSets[setIndex];
                if (set == null)
                {
                    continue;
                }
                output.AppendLine("PylonSet[" + setIndex + "]: " + set.name);
                AppendEditor2HardpointSetMetadata(output, set);
                AppendPylonStructure(output, set, setIndex);
                output.Append("  AllowedWeapons: ");
                bool wroteWeapon = false;
                if (set.weaponOptions != null)
                {
                    foreach (WeaponMount mount in set.weaponOptions)
                    {
                        if (mount == null)
                        {
                            continue;
                        }
                        if (wroteWeapon)
                        {
                            output.Append(", ");
                        }
                        output.Append(mount.jsonKey);
                        wroteWeapon = true;
                    }
                }
                output.AppendLine(wroteWeapon ? string.Empty : "<none>");

                if (set.hardpoints == null)
                {
                    continue;
                }
                for (int hardpointIndex = 0; hardpointIndex < set.hardpoints.Count; hardpointIndex++)
                {
                    Hardpoint hardpoint = set.hardpoints[hardpointIndex];
                    Vector3 position = hardpoint?.transform == null
                        ? Vector3.zero
                        : hardpoint.transform.localPosition;
                    output.AppendLine("  HP[" + hardpointIndex + "] Position: (" +
                                      FormatFloat(position.x) + ", " +
                                      FormatFloat(position.y) + ", " +
                                      FormatFloat(position.z) + ")");
                    AppendHardpointStructure(output, hardpoint, hardpointIndex);
                    output.AppendLine("    Part: " + PartLabel(hardpoint?.part));
                    Array variants = GetPylonOptions(hardpoint);
                    if (variants.Length == 0)
                    {
                        output.AppendLine("    PylonVisual: <none>");
                    }
                    for (int variantIndex = 0; variantIndex < variants.Length; variantIndex++)
                    {
                        object variant = variants.GetValue(variantIndex);
                        output.AppendLine("    PylonVisual[" + variantIndex + "]: " +
                                          VariantLabel(variant) + " | " +
                                          RendererLabel(PylonOptionRendererField?.GetValue(variant) as Renderer));
                    }
                }
            }
            return output.ToString().TrimEnd();
        }

        private void ValidateSmokeModel()
        {
            if (!smokeTestRequested || smokeModelValidated || weaponManager?.hardpointSets == null)
            {
                return;
            }
            string formatted = FormatConfiguration();
            smokeModelValidated = formatted.Contains("AllowedWeapons:") &&
                                  formatted.Contains("Part:") &&
                                  formatted.Contains("PylonVisual:") &&
                                  formatted.Contains("HardpointCount:") &&
                                  formatted.Contains("Rotation:") &&
                                  formatted.Contains("Scale:") &&
                                  formatted.Contains("RootParent:") &&
                                  formatted.Contains("AssemblyRoot:") &&
                                  formatted.Contains("ModelSlot:") &&
                                  formatted.Contains("WeaponAnchor:") &&
                                  formatted.Contains("EditorSetOrigin:") &&
                                   formatted.Contains("EditorOrigin:") &&
                                  formatted.Contains("TemplateSource:");
            if (smokeModelValidated)
            {
                log.LogInfo(
                    "Aircraft-selector pylon editor live model passed: all pylons, positions, " +
                    "stable pylon assembly roots, isolated model slots, weapon anchors, " +
                    "damage links, weapon limits, " +
                    "Base/visual variants, HPW Editor 2 metadata and " +
                    "master-configurator export, and mount/rack hierarchy workshop validated.");
            }
            else
            {
                log.LogError("Aircraft-selector pylon editor live model validation failed.");
            }
        }

        private bool ShouldDraw()
        {
            return weaponManager != null &&
                   ((selector != null && selector.isActiveAndEnabled) || smokeTestRequested);
        }

        private static AircraftSelectionMenu FindActiveSelector()
        {
            AircraftSelectionMenu[] menus = UnityEngine.Object.FindObjectsOfType<AircraftSelectionMenu>();
            foreach (AircraftSelectionMenu menu in menus)
            {
                if (menu != null && menu.isActiveAndEnabled)
                {
                    return menu;
                }
            }
            return null;
        }

        internal static void NotifySelectorEnabled(AircraftSelectionMenu menu)
        {
            if (menu != null)
            {
                activeSelectorHint = menu;
            }
        }

        internal static void NotifySelectorDestroyed(AircraftSelectionMenu menu)
        {
            if (activeSelectorHint == menu)
            {
                activeSelectorHint = null;
            }
        }

        private static bool IsSmokeTestRequested()
        {
            if (string.Equals(
                    Environment.GetEnvironmentVariable(SmokeTestEnvironmentVariable),
                    "1",
                    StringComparison.Ordinal))
            {
                return true;
            }

            foreach (string argument in Environment.GetCommandLineArgs())
            {
                if (string.Equals(argument, SmokeTestCommandLineArgument, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private string AircraftLabel()
        {
            if (previewAircraft?.definition != null)
            {
                return previewAircraft.definition.unitName + " (" + previewAircraft.definition.jsonKey + ")";
            }
            return "none";
        }

        private static string PartLabel(UnitPart part)
        {
            return part == null ? "<none>" : GetTransformPath(part.transform);
        }

        private static string RendererLabel(Renderer renderer)
        {
            return renderer == null ? "<none>" : GetTransformPath(renderer.transform);
        }

        private static string WeaponLabel(WeaponMount mount)
        {
            if (mount == null)
            {
                return "<none>";
            }
            string name = string.IsNullOrEmpty(mount.mountName) ? mount.name : mount.mountName;
            return name + " [" + mount.jsonKey + "]";
        }

        private static string GetTransformPath(Transform transform)
        {
            if (transform == null)
            {
                return "<none>";
            }
            Stack<string> names = new Stack<string>();
            Transform current = transform;
            while (current != null)
            {
                names.Push(current.name);
                current = current.parent;
            }
            return string.Join("/", names.ToArray());
        }

        private static Array GetPylonOptions(Hardpoint hardpoint)
        {
            if (hardpoint == null || PylonOptionsField == null || PylonOptionType == null)
            {
                return Array.CreateInstance(typeof(object), 0);
            }
            Array variants = PylonOptionsField.GetValue(hardpoint) as Array;
            return variants ?? Array.CreateInstance(PylonOptionType, 0);
        }

        private static int AddPylonVariant(Hardpoint hardpoint)
        {
            Array existing = GetPylonOptions(hardpoint);
            Array expanded = Array.CreateInstance(PylonOptionType, existing.Length + 1);
            Array.Copy(existing, expanded, existing.Length);
            object variant = Activator.CreateInstance(PylonOptionType, nonPublic: true);
            PylonOptionCargoField?.SetValue(variant, false);
            PylonOptionMountField?.SetValue(variant, null);
            PylonOptionRendererField?.SetValue(variant, null);
            expanded.SetValue(variant, existing.Length);
            PylonOptionsField.SetValue(hardpoint, expanded);
            return existing.Length;
        }

        private static void RemovePylonVariant(Hardpoint hardpoint, int index)
        {
            Array existing = GetPylonOptions(hardpoint);
            if (index < 0 || index >= existing.Length)
            {
                return;
            }
            Array reduced = Array.CreateInstance(PylonOptionType, existing.Length - 1);
            int destination = 0;
            for (int source = 0; source < existing.Length; source++)
            {
                if (source != index)
                {
                    reduced.SetValue(existing.GetValue(source), destination++);
                }
            }
            PylonOptionsField.SetValue(hardpoint, reduced);
        }

        private static string VariantLabel(object variant)
        {
            if (variant == null)
            {
                return "<invalid>";
            }
            bool cargo = (bool)(PylonOptionCargoField?.GetValue(variant) ?? false);
            WeaponMount mount = PylonOptionMountField?.GetValue(variant) as WeaponMount;
            if (cargo)
            {
                return "Cargo";
            }
            return mount == null ? "Base" : "Weapon: " + WeaponLabel(mount);
        }

        private static void SetVariantBase(object variant)
        {
            PylonOptionCargoField?.SetValue(variant, false);
            PylonOptionMountField?.SetValue(variant, null);
        }

        private static void SetVariantCargo(object variant)
        {
            PylonOptionCargoField?.SetValue(variant, true);
            PylonOptionMountField?.SetValue(variant, null);
        }

        private static void SetVariantMount(object variant, WeaponMount mount)
        {
            PylonOptionCargoField?.SetValue(variant, false);
            PylonOptionMountField?.SetValue(variant, mount);
        }

        private static void PreviewVariantRenderer(Hardpoint hardpoint, object selectedVariant)
        {
            Array variants = GetPylonOptions(hardpoint);
            foreach (object variant in variants)
            {
                Renderer renderer = PylonOptionRendererField?.GetValue(variant) as Renderer;
                if (renderer != null)
                {
                    renderer.enabled = ReferenceEquals(variant, selectedVariant);
                }
            }
        }

        private static bool MatchesSearch(string value, string search)
        {
            return string.IsNullOrWhiteSpace(search) ||
                   (!string.IsNullOrEmpty(value) &&
                    value.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool TryParseVector(string[] text, out Vector3 value)
        {
            value = Vector3.zero;
            if (text == null || text.Length != 3 ||
                !float.TryParse(text[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) ||
                !float.TryParse(text[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) ||
                !float.TryParse(text[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
            {
                return false;
            }
            value = new Vector3(x, y, z);
            return true;
        }

        private static string FormatFloat(float value)
        {
            return value.ToString("0.######", CultureInfo.InvariantCulture);
        }

        private void DrawManagedWindow(
            ref Rect rect,
            int windowId,
            GUI.WindowFunction drawer,
            string title,
            float minimumWidth,
            float minimumHeight,
            float virtualWidth,
            float virtualHeight)
        {
            Rect before = rect;
            drawingWindowRect = rect;
            pendingResizeDelta = Vector2.zero;
            Rect next = GUI.Window(windowId, rect, drawer, title);
            if (resizingWindowId == windowId && pendingResizeDelta != Vector2.zero)
            {
                next.width += pendingResizeDelta.x;
                next.height += pendingResizeDelta.y;
            }

            float maximumWidth = Mathf.Max(100f, virtualWidth - 4f);
            float maximumHeight = Mathf.Max(100f, virtualHeight - 4f);
            next.width = Mathf.Clamp(next.width, Mathf.Min(minimumWidth, maximumWidth), maximumWidth);
            next.height = Mathf.Clamp(next.height, Mathf.Min(minimumHeight, maximumHeight), maximumHeight);
            next.x = Mathf.Clamp(next.x, 0f, Mathf.Max(0f, virtualWidth - next.width));
            next.y = Mathf.Clamp(next.y, 0f, Mathf.Max(0f, virtualHeight - 28f));
            rect = next;

            if (!Approximately(before, next))
            {
                layoutDirty = true;
                nextLayoutSave = Time.unscaledTime + 0.5f;
            }
        }

        private void DrawWindowChrome(int windowId, float minimumWidth, float minimumHeight)
        {
            const float handleSize = 18f;
            Rect resizeHandle = new Rect(
                drawingWindowRect.width - handleSize - 3f,
                drawingWindowRect.height - handleSize - 3f,
                handleSize,
                handleSize);
            GUI.Box(resizeHandle, "↘");

            Event current = Event.current;
            if (current.type == EventType.MouseDown &&
                current.button == 0 &&
                resizeHandle.Contains(current.mousePosition))
            {
                resizingWindowId = windowId;
                current.Use();
            }
            else if (current.type == EventType.MouseDrag && resizingWindowId == windowId)
            {
                pendingResizeDelta += current.delta;
                current.Use();
            }
            else if (current.type == EventType.MouseUp && resizingWindowId == windowId)
            {
                resizingWindowId = -1;
                layoutDirty = true;
                nextLayoutSave = Time.unscaledTime + 0.1f;
                current.Use();
            }

            GUI.DragWindow(new Rect(
                0f,
                0f,
                Mathf.Max(minimumWidth, drawingWindowRect.width - 24f),
                24f));
        }

        private void LoadWindowLayout()
        {
            LoadEditor2WindowLayouts();
        }

        private void SaveWindowLayout()
        {
            SaveEditor2WindowLayouts();
            PlayerPrefs.Save();
            layoutDirty = false;
        }

        private static Rect LoadRect(string name, Rect fallback)
        {
            string key = LayoutKeyPrefix + name;
            if (!PlayerPrefs.HasKey(key + ".x"))
            {
                return fallback;
            }

            Rect loaded = new Rect(
                PlayerPrefs.GetFloat(key + ".x", fallback.x),
                PlayerPrefs.GetFloat(key + ".y", fallback.y),
                PlayerPrefs.GetFloat(key + ".w", fallback.width),
                PlayerPrefs.GetFloat(key + ".h", fallback.height));
            return IsFinite(loaded.x) && IsFinite(loaded.y) &&
                   IsFinite(loaded.width) && IsFinite(loaded.height) &&
                   loaded.width >= 100f && loaded.height >= 80f
                ? loaded
                : fallback;
        }

        private static void SaveRect(string name, Rect rect)
        {
            string key = LayoutKeyPrefix + name;
            PlayerPrefs.SetFloat(key + ".x", rect.x);
            PlayerPrefs.SetFloat(key + ".y", rect.y);
            PlayerPrefs.SetFloat(key + ".w", rect.width);
            PlayerPrefs.SetFloat(key + ".h", rect.height);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool Approximately(Rect left, Rect right)
        {
            return Mathf.Abs(left.x - right.x) < 0.01f &&
                   Mathf.Abs(left.y - right.y) < 0.01f &&
                   Mathf.Abs(left.width - right.width) < 0.01f &&
                   Mathf.Abs(left.height - right.height) < 0.01f;
        }

        private static bool RunSelfTest()
        {
            string[] vector = { "1.25", "-0.5", "3" };
            return PreviewAircraftField != null &&
                   LoadoutSelectorField != null &&
                   PylonOptionsField != null &&
                   PylonOptionType != null &&
                   PylonOptionCargoField != null &&
                   PylonOptionMountField != null &&
                   PylonOptionRendererField != null &&
                   Mathf.Approximately(UiScale, 0.85f) &&
                   RunEditor2SelfTest() &&
                   RunStructureEditorSelfTest() &&
                   WeaponMountWorkshop.RunSelfTest() &&
                   TryParseVector(vector, out Vector3 parsed) &&
                   Mathf.Approximately(parsed.x, 1.25f) &&
                   Mathf.Approximately(parsed.y, -0.5f) &&
                   Mathf.Approximately(parsed.z, 3f);
        }
    }

    [HarmonyPatch]
    internal static class AircraftSelectorPylonEditorMenuEnabledPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(AircraftSelectionMenu), "OnEnable", Type.EmptyTypes);
        }

        private static void Postfix(AircraftSelectionMenu __instance)
        {
            AircraftSelectorPylonEditor.NotifySelectorEnabled(__instance);
        }
    }

    [HarmonyPatch]
    internal static class AircraftSelectorPylonEditorMenuDestroyedPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(AircraftSelectionMenu), "OnDestroy", Type.EmptyTypes);
        }

        private static void Prefix(AircraftSelectionMenu __instance)
        {
            AircraftSelectorPylonEditor.NotifySelectorDestroyed(__instance);
        }
    }
}

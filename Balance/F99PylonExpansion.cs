using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using NuclearOption.SavedMission;
using UnityEngine;

namespace TheGreatBigRebalancing.Balance
{
    internal static class F99PylonExpansion
    {
        private const string F99DefinitionKey = "Aryx_LightFighter1";
        private const string OriginalCentrelinePylonName = "Centreline Pylon";
        private const string LegacyForwardCentrelinePylonName = "Forward centerline";
        private const string ForeCentrelinePylonName = "Fore Centerline Pylon";
        private const string LegacyRearCentrelinePylonName = "Rear centerline";
        private const string AftCentrelinePylonName = "Aft Centerline Pylon";
        private const string CombinedCentrelinePylonName = "Centerline Pylon";
        private const string LegacyAdditionalPylonOneName = "Additional Pylon 1";
        private const string LegacyOuterPylonName = "Outer Pylon";
        private const string OuterPylonName = "Outer Wing Pylon";
        private const string OriginalOuterWingPylonsName = "Outer Wing Pylons";
        private const string MidWingPylonName = "Mid Wing Pylon";
        private const string LegacyAdditionalPylonTwoName = "Additional Pylon 2";
        private const string AdditionalPylonOneMountKey = "AAM3_single";
        private const string TailHookPylonName = "Tail Hook";
        private const string TargetingPodPylonName = "TGP";
        private const string LegacyTargetingPodPylonName = "New HardpointSet";
        private const string TargetingPodMountKey = "Aryx_ExternalTargetingPod_Trainer";
        private const string Vt7DefinitionKey = "VTOLTrainer1";
        private const string Vt7OuterWingPylonName = "Outer wing pylons";
        private const string Vt7InnerWingPylonName = "Inner wing pylons";
        private const int AdditionalPylonOneHardpoints = 2;
        private const int AdditionalPylonTwoHardpoints = 1;

        private static readonly string[] CentrelineMountKeys =
        {
            "AGM_heavy_single",                 // AGM-68
            "aryx_lf1_bomb_cluster1_single",   // CB-400
            "bomb_500_single"                   // GPO-500
        };

        private static readonly Vector3[] AdditionalPylonOnePositions =
        {
            new Vector3(1.525016f, -0.32555f, -0.218517f),
            new Vector3(-1.524869f, -0.327192f, -0.218348f)
        };

        private static readonly Vector3[] AdditionalPylonOneRotations =
        {
            new Vector3(0.000001f, -0.000004f, -0.000004f),
            new Vector3(-0.008728f, 0.00214f, 0.02837f)
        };

        private static readonly Vector3[] AdditionalPylonOneScales =
        {
            new Vector3(1.000001f, 1f, 1f),
            new Vector3(0.999999f, 1f, 1f)
        };

        private static readonly Vector3 CentrelinePylonPosition =
            new Vector3(0f, -0.295f, 0.688f);

        private static readonly Vector3 AdditionalPylonTwoPosition =
            new Vector3(0f, -0.215f, -2.155f);

        private static readonly Quaternion CentrelinePylonRotation =
            Quaternion.Euler(2.471623f, 0f, 0f);

        private static readonly Vector3 TailHookPosition =
            new Vector3(0f, -0.466f, 1.02f);

        private static readonly Quaternion TailHookRotation =
            Quaternion.Euler(5.000002f, 0f, 0f);

        private static readonly Vector3 TargetingPodPosition =
            new Vector3(0.1f, -0.02f, 2.5f);

        private static readonly Quaternion TargetingPodRotation =
            Quaternion.Euler(0f, -0.000002f, 27f);

        private static readonly FieldInfo PylonOptionsField =
            AccessTools.Field(typeof(Hardpoint), "pylonOptions");

        private static readonly Type PylonOptionType =
            PylonOptionsField?.FieldType.GetElementType();

        private static readonly FieldInfo PylonOptionCargoField =
            PylonOptionType == null ? null : AccessTools.Field(PylonOptionType, "cargo");

        private static readonly FieldInfo PylonOptionMountField =
            PylonOptionType == null ? null : AccessTools.Field(PylonOptionType, "mount");

        private static readonly FieldInfo PylonOptionRendererField =
            PylonOptionType == null ? null : AccessTools.Field(PylonOptionType, "renderer");

        private static Encyclopedia pendingEncyclopedia;
        private static float nextPendingAttempt;
        private static float pendingDeadline;
        private static bool loggedSuccess;
        private static bool loggedValidation;
        private static bool loggedTargetingPodSuccess;
        private static bool loggedTargetingPodValidation;
        private static Aircraft prefabAircraft;
        private static WeaponManager prefabWeaponManager;

        internal static Aircraft PrefabAircraft => prefabAircraft;
        internal static WeaponManager PrefabWeaponManager => prefabWeaponManager;

        internal static bool IsF99Definition(AircraftDefinition definition)
        {
            return IsF99(definition);
        }

        internal static bool TryGetAdditionalPylons(
            WeaponManager weaponManager,
            out HardpointSet first,
            out HardpointSet second)
        {
            first = null;
            second = null;
            if (weaponManager?.hardpointSets == null)
            {
                return false;
            }

            foreach (HardpointSet hardpointSet in weaponManager.hardpointSets)
            {
                if (hardpointSet == null)
                {
                    continue;
                }

                if (IsAddedOuterPylonName(hardpointSet.name))
                {
                    first = hardpointSet;
                }
                else if (IsRearCentrelineName(hardpointSet.name))
                {
                    second = hardpointSet;
                }
            }

            return first?.hardpoints?.Count == AdditionalPylonOneHardpoints &&
                   second?.hardpoints?.Count == AdditionalPylonTwoHardpoints &&
                   second.SymmetryWithPrev;
        }

        internal static void RegisterEncyclopedia(Encyclopedia encyclopedia)
        {
            pendingEncyclopedia = encyclopedia;
            nextPendingAttempt = 0f;
            pendingDeadline = Time.unscaledTime + 60f;
            TryApplyPending();
        }

        internal static void TryApplyPending()
        {
            if (pendingEncyclopedia == null || Time.unscaledTime < nextPendingAttempt)
            {
                return;
            }

            bool baseApplied = TryApplyToEncyclopedia(pendingEncyclopedia);
            bool targetingPodRegistered = FindRegisteredMount(
                pendingEncyclopedia,
                prefabWeaponManager?.hardpointSets,
                TargetingPodMountKey) != null;
            bool targetingPodApplied = prefabWeaponManager != null &&
                                       HasTargetingPodPylon(prefabWeaponManager);
            if (baseApplied && targetingPodRegistered && targetingPodApplied)
            {
                pendingEncyclopedia = null;
                return;
            }

            if (Time.unscaledTime >= pendingDeadline)
            {
                if (!baseApplied)
                {
                    Plugin.ModLogger?.LogWarning(
                        "F-99 Shrike pylon expansion was not applied because the Aryx Shrike definition was not loaded.");
                }
                else if (targetingPodRegistered && !targetingPodApplied)
                {
                    Plugin.ModLogger?.LogError(
                        "F-99 Shrike TGP pylon was not applied even though the Aryx targeting pod was registered.");
                }
                pendingEncyclopedia = null;
                return;
            }

            nextPendingAttempt = Time.unscaledTime + 0.5f;
        }

        internal static void ApplyToWeaponManager(WeaponManager weaponManager)
        {
            if (weaponManager == null)
            {
                return;
            }

            Aircraft aircraft = weaponManager.GetComponentInParent<Aircraft>();
            if (aircraft == null || !IsF99(aircraft.definition))
            {
                return;
            }

            Encyclopedia encyclopedia = Encyclopedia.i;
            ApplyToDefinition(aircraft.definition, encyclopedia);
            int originalHardpointSetCount = weaponManager.hardpointSets?.Length ?? 0;
            int originalCentrelineIndex = FindCentrelineIndex(weaponManager);
            bool expanded = ExpandWeaponManager(weaponManager);
            int baseHardpointSetCount = weaponManager.hardpointSets?.Length ?? 0;
            ApplyPylonNames(weaponManager);
            ApplyCentrelineWeaponLimits(weaponManager);
            ApplyExportedGeometry(weaponManager, aircraft, encyclopedia);
            if (expanded)
            {
                MigrateLoadout(
                    aircraft.loadout,
                    originalHardpointSetCount,
                    baseHardpointSetCount,
                    originalCentrelineIndex);
            }
            else
            {
                EnsureLoadoutLength(aircraft.loadout, baseHardpointSetCount);
            }
            EnsureTargetingPodPylon(
                weaponManager,
                aircraft,
                encyclopedia,
                out _);
            EnsureLoadoutLength(aircraft.loadout, weaponManager.hardpointSets?.Length ?? 0);
            SynchronizeCentrelineStore(weaponManager, aircraft.loadout);
        }

        private static bool TryApplyToEncyclopedia(Encyclopedia encyclopedia)
        {
            if (encyclopedia?.aircraft == null)
            {
                return false;
            }

            AircraftDefinition definition = encyclopedia.aircraft.Find(IsF99);
            if (definition == null)
            {
                return false;
            }

            return ApplyToDefinition(definition, encyclopedia);
        }

        private static bool ApplyToDefinition(
            AircraftDefinition definition,
            Encyclopedia encyclopedia = null)
        {
            if (!TryGetWeaponManager(definition, out WeaponManager weaponManager))
            {
                return false;
            }
            if (weaponManager.hardpointSets == null)
            {
                return false;
            }

            prefabAircraft = definition.unitPrefab.GetComponent<Aircraft>() ??
                             definition.unitPrefab.GetComponentInChildren<Aircraft>(true);
            prefabWeaponManager = weaponManager;

            int originalHardpointSetCount = weaponManager.hardpointSets.Length;
            int originalCentrelineIndex = FindCentrelineIndex(weaponManager);
            bool expanded = ExpandWeaponManager(weaponManager);
            int baseHardpointSetCount = weaponManager.hardpointSets.Length;
            ApplyPylonNames(weaponManager);
            ApplyCentrelineWeaponLimits(weaponManager);
            ApplyExportedGeometry(
                weaponManager,
                prefabAircraft,
                encyclopedia ?? Encyclopedia.i);
            int migrated = expanded
                ? MigrateParameters(
                    definition.aircraftParameters,
                    originalHardpointSetCount,
                    baseHardpointSetCount,
                    originalCentrelineIndex)
                : 0;

            bool targetingPodConfigured = EnsureTargetingPodPylon(
                weaponManager,
                prefabAircraft,
                encyclopedia ?? Encyclopedia.i,
                out bool targetingPodAdded);
            if (targetingPodConfigured)
            {
                int extended = EnsureParameterLoadoutLengths(
                    definition.aircraftParameters,
                    weaponManager.hardpointSets.Length);
                if (!loggedTargetingPodSuccess)
                {
                    loggedTargetingPodSuccess = true;
                    Plugin.ModLogger?.LogInfo(
                        "F-99 Aryx Weapon Pack compatibility active: dedicated TGP hardpoint added; " +
                        extended + " stock/AI loadout(s) were extended." +
                        (targetingPodAdded ? string.Empty : " Existing TGP pylon was normalized."));
                }
            }

            if (!loggedValidation)
            {
                if (ValidateLinkedCentrelineConfiguration(weaponManager, out string validationFailure))
                {
                    loggedValidation = true;
                    Plugin.ModLogger?.LogInfo(
                        "F-99 linked-centreline validation passed: relocated primary and rearward partner " +
                        "positions, AGM-68/CB-400/GPO-500 limits, matching stores/bay exclusion, " +
                        "and exported geometry verified.");
                }
                else
                {
                    Plugin.ModLogger?.LogError(
                        "F-99 linked-centreline validation failed: " + validationFailure);
                }
            }

            if (!loggedSuccess && (expanded || HasAdditionalPylons(weaponManager.hardpointSets)))
            {
                loggedSuccess = true;
                Plugin.ModLogger?.LogInfo(
                    "F-99 Shrike expansion active: Outer Wing Pylon has two IRM-S2 hardpoints; " +
                    "Aft Centerline Pylon adds one native-symmetry hardpoint to the bay-gated " +
                    "Centerline Pylon pair; " +
                    migrated + " stock/AI loadout(s) were extended.");
            }

            if (targetingPodConfigured && !loggedTargetingPodValidation)
            {
                if (ValidateTargetingPodConfiguration(
                        weaponManager,
                        prefabAircraft,
                        out string targetingPodFailure))
                {
                    loggedTargetingPodValidation = true;
                    Plugin.ModLogger?.LogInfo(
                        "F-99 TGP validation passed: one root-parented hardpoint, exported position, " +
                        "targeting-pod-only weapon list, and no pylon visual.");
                }
                else
                {
                    Plugin.ModLogger?.LogError(
                        "F-99 TGP validation failed: " + targetingPodFailure);
                }
            }

            return HasAdditionalPylons(weaponManager.hardpointSets);
        }

        private static bool ExpandWeaponManager(WeaponManager weaponManager)
        {
            if (weaponManager?.hardpointSets == null || HasAdditionalPylons(weaponManager.hardpointSets))
            {
                return false;
            }

            HardpointSet template = FindTemplatePylon(weaponManager.hardpointSets);
            int centrelineIndex = FindCentrelineIndex(weaponManager);
            HardpointSet centreline = centrelineIndex < 0
                ? null
                : weaponManager.hardpointSets[centrelineIndex];
            WeaponMount s2Mount = FindMount(weaponManager.hardpointSets, AdditionalPylonOneMountKey);
            if (template == null || centreline?.hardpoints == null || centreline.hardpoints.Count == 0 || s2Mount == null)
            {
                Plugin.ModLogger?.LogError(
                    "F-99 Shrike expansion requires a cloneable pylon, the Centreline Pylon, and AAM3_single.");
                return false;
            }

            ShiftPreclusionIndexesForInsertion(weaponManager.hardpointSets, centrelineIndex + 1);
            centreline.name = ForeCentrelinePylonName;
            centreline.SymmetryName = CombinedCentrelinePylonName;

            HardpointSet firstPylon = CreatePylon(
                template,
                OuterPylonName,
                AdditionalPylonOneHardpoints,
                new List<WeaponMount> { s2Mount });
            for (int i = 0; i < firstPylon.hardpoints.Count; i++)
            {
                firstPylon.hardpoints[i].transform.localPosition = AdditionalPylonOnePositions[i];
                firstPylon.hardpoints[i].part = centreline.hardpoints[0].part;
            }

            HardpointSet secondPylon = CreatePylon(
                centreline,
                AftCentrelinePylonName,
                AdditionalPylonTwoHardpoints,
                centreline.weaponOptions != null
                    ? new List<WeaponMount>(centreline.weaponOptions)
                    : new List<WeaponMount>());
            secondPylon.hardpoints[0].transform.localPosition = AdditionalPylonTwoPosition;
            secondPylon.precludingHardpointSets = centreline.precludingHardpointSets != null
                ? new List<byte>(centreline.precludingHardpointSets)
                : new List<byte>();
            secondPylon.SymmetryWithPrev = true;
            secondPylon.SymmetryName = CombinedCentrelinePylonName;

            List<HardpointSet> expandedSets = new List<HardpointSet>(weaponManager.hardpointSets);
            expandedSets.Insert(centrelineIndex + 1, secondPylon);
            expandedSets.Add(firstPylon);
            weaponManager.hardpointSets = expandedSets.ToArray();
            return true;
        }

        private static HardpointSet FindTemplatePylon(HardpointSet[] hardpointSets)
        {
            HardpointSet best = null;
            int bestScore = -1;

            foreach (HardpointSet candidate in hardpointSets)
            {
                if (candidate?.hardpoints == null || candidate.hardpoints.Count == 0)
                {
                    continue;
                }

                int optionCount = candidate.weaponOptions?.Count ?? 0;
                int score = optionCount * 10 +
                            (candidate.hardpoints.Count == AdditionalPylonOneHardpoints ? 5 : 0);
                if (score > bestScore)
                {
                    best = candidate;
                    bestScore = score;
                }
            }

            return best;
        }

        private static HardpointSet CreatePylon(
            HardpointSet template,
            string name,
            int hardpointCount,
            List<WeaponMount> weaponOptions)
        {
            HardpointSet pylon = new HardpointSet
            {
                name = name,
                precludingHardpointSets = new List<byte>(),
                SymmetryWithPrev = false,
                SymmetryName = string.Empty,
                weaponOptions = weaponOptions,
                weaponMount = null,
                hardpoints = new List<Hardpoint>(hardpointCount)
            };

            for (int i = 0; i < hardpointCount; i++)
            {
                Hardpoint source = template.hardpoints[i % template.hardpoints.Count];
                pylon.hardpoints.Add(CloneHardpoint(source, name, i + 1));
            }

            return pylon;
        }

        private static WeaponMount FindMount(HardpointSet[] hardpointSets, string jsonKey)
        {
            if (hardpointSets == null)
            {
                return null;
            }
            foreach (HardpointSet set in hardpointSets)
            {
                if (set?.weaponOptions == null)
                {
                    continue;
                }
                WeaponMount match = set.weaponOptions.Find(
                    mount => mount != null &&
                             string.Equals(mount.jsonKey, jsonKey, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    return match;
                }
            }
            return null;
        }

        private static WeaponMount FindRegisteredMount(
            Encyclopedia encyclopedia,
            HardpointSet[] hardpointSets,
            string jsonKey)
        {
            WeaponMount registered = encyclopedia?.weaponMounts?.Find(
                mount => mount != null &&
                         string.Equals(mount.jsonKey, jsonKey, StringComparison.OrdinalIgnoreCase));
            if (registered == null && Encyclopedia.i != encyclopedia)
            {
                registered = Encyclopedia.i?.weaponMounts?.Find(
                    mount => mount != null &&
                             string.Equals(mount.jsonKey, jsonKey, StringComparison.OrdinalIgnoreCase));
            }
            return registered ?? FindMount(hardpointSets, jsonKey);
        }

        private static bool EnsureTargetingPodPylon(
            WeaponManager weaponManager,
            Aircraft aircraft,
            Encyclopedia encyclopedia,
            out bool added)
        {
            added = false;
            if (weaponManager?.hardpointSets == null || aircraft == null)
            {
                return false;
            }

            WeaponMount targetingPod = FindRegisteredMount(
                encyclopedia,
                weaponManager.hardpointSets,
                TargetingPodMountKey);
            if (targetingPod == null)
            {
                return false;
            }

            int index = FindTargetingPodPylonIndex(weaponManager);
            HardpointSet targetingPodSet;
            if (index < 0)
            {
                HardpointSet template = FindTemplatePylon(weaponManager.hardpointSets);
                if (template == null)
                {
                    Plugin.ModLogger?.LogError(
                        "F-99 TGP compatibility found the Aryx targeting pod but no hardpoint template.");
                    return false;
                }

                targetingPodSet = CreatePylon(
                    template,
                    TargetingPodPylonName,
                    1,
                    new List<WeaponMount> { targetingPod });
                List<HardpointSet> sets = new List<HardpointSet>(weaponManager.hardpointSets)
                {
                    targetingPodSet
                };
                weaponManager.hardpointSets = sets.ToArray();
                added = true;
            }
            else
            {
                targetingPodSet = weaponManager.hardpointSets[index];
                if (targetingPodSet?.hardpoints == null || targetingPodSet.hardpoints.Count == 0)
                {
                    HardpointSet template = FindTemplatePylon(weaponManager.hardpointSets);
                    if (template == null)
                    {
                        return false;
                    }
                    targetingPodSet.hardpoints = new List<Hardpoint>
                    {
                        CloneHardpoint(template.hardpoints[0], TargetingPodPylonName, 1)
                    };
                }
                else if (targetingPodSet.hardpoints.Count > 1)
                {
                    targetingPodSet.hardpoints = new List<Hardpoint>
                    {
                        targetingPodSet.hardpoints[0]
                    };
                }
            }

            targetingPodSet.name = TargetingPodPylonName;
            targetingPodSet.SymmetryWithPrev = false;
            targetingPodSet.SymmetryName = string.Empty;
            targetingPodSet.precludingHardpointSets = new List<byte>();
            targetingPodSet.weaponOptions = new List<WeaponMount> { targetingPod };
            targetingPodSet.weaponMount = null;

            Hardpoint hardpoint = targetingPodSet.hardpoints[0];
            hardpoint.transform.SetParent(aircraft.transform, false);
            hardpoint.transform.localPosition = TargetingPodPosition;
            hardpoint.transform.localRotation = TargetingPodRotation;
            hardpoint.transform.localScale = Vector3.one;
            hardpoint.part = FindRootUnitPart(aircraft) ?? hardpoint.part;
            hardpoint.bayDoors = Array.Empty<BayDoor>();
            hardpoint.doorOpenDuration = 0f;
            hardpoint.BuiltInWeapons = Array.Empty<Weapon>();
            hardpoint.BuiltInTurrets = Array.Empty<Turret>();
            hardpoint.HardpointIndex = -1;
            ClearPylonVisuals(hardpoint);
            return true;
        }

        private static int FindTargetingPodPylonIndex(WeaponManager weaponManager)
        {
            int index = FindSetIndex(weaponManager, TargetingPodPylonName);
            if (index >= 0)
            {
                return index;
            }

            index = FindSetIndex(weaponManager, LegacyTargetingPodPylonName);
            if (index < 0)
            {
                return -1;
            }

            HardpointSet candidate = weaponManager.hardpointSets[index];
            return candidate?.hardpoints?.Count == 1 &&
                   candidate.weaponOptions?.Count == 1 &&
                   string.Equals(
                       candidate.weaponOptions[0]?.jsonKey,
                       TargetingPodMountKey,
                       StringComparison.OrdinalIgnoreCase)
                ? index
                : -1;
        }

        private static UnitPart FindRootUnitPart(Aircraft aircraft)
        {
            if (aircraft == null)
            {
                return null;
            }

            UnitPart direct = aircraft.GetComponent<UnitPart>();
            if (direct != null)
            {
                return direct;
            }

            foreach (UnitPart part in aircraft.GetComponentsInChildren<UnitPart>(true))
            {
                if (part != null && part.transform == aircraft.transform)
                {
                    return part;
                }
            }
            return null;
        }

        private static void ClearPylonVisuals(Hardpoint hardpoint)
        {
            if (hardpoint == null)
            {
                return;
            }

            hardpoint.Pylon = null;
            hardpoint.Plug = null;
            if (PylonOptionsField == null)
            {
                return;
            }

            Type elementType = PylonOptionsField.FieldType.GetElementType();
            if (elementType != null)
            {
                PylonOptionsField.SetValue(hardpoint, Array.CreateInstance(elementType, 0));
            }
        }

        private static void ShiftPreclusionIndexesForInsertion(HardpointSet[] sets, int insertionIndex)
        {
            foreach (HardpointSet set in sets)
            {
                if (set?.precludingHardpointSets == null)
                {
                    continue;
                }
                for (int i = 0; i < set.precludingHardpointSets.Count; i++)
                {
                    if (set.precludingHardpointSets[i] >= insertionIndex)
                    {
                        set.precludingHardpointSets[i]++;
                    }
                }
            }
        }

        private static void ApplyExportedGeometry(
            WeaponManager weaponManager,
            Aircraft aircraft,
            Encyclopedia encyclopedia)
        {
            if (weaponManager?.hardpointSets == null)
            {
                return;
            }

            int centrelineIndex = FindCentrelineIndex(weaponManager);
            int firstIndex = FindAddedOuterPylonIndex(weaponManager);
            int secondIndex = FindRearCentrelineIndex(weaponManager);
            int tailHookIndex = FindSetIndex(weaponManager, TailHookPylonName);
            if (centrelineIndex >= 0 &&
                weaponManager.hardpointSets[centrelineIndex].hardpoints?.Count > 0)
            {
                Transform transform =
                    weaponManager.hardpointSets[centrelineIndex].hardpoints[0].transform;
                transform.localPosition = CentrelinePylonPosition;
                transform.localRotation = CentrelinePylonRotation;
                transform.localScale = Vector3.one;
            }
            if (firstIndex >= 0 &&
                weaponManager.hardpointSets[firstIndex].hardpoints?.Count == AdditionalPylonOneHardpoints)
            {
                for (int i = 0; i < AdditionalPylonOneHardpoints; i++)
                {
                    Hardpoint hardpoint = weaponManager.hardpointSets[firstIndex].hardpoints[i];
                    Transform exportedParent = FindAircraftTransform(
                        aircraft,
                        i == 0
                            ? "Aryx_LightFighter1_Wing_R/Pylon_R"
                            : "Aryx_LightFighter1_Wing_L/Pylon_L");
                    if (exportedParent != null && hardpoint.transform.parent != exportedParent)
                    {
                        hardpoint.transform.SetParent(exportedParent, false);
                    }
                    hardpoint.transform.localPosition = AdditionalPylonOnePositions[i];
                    hardpoint.transform.localRotation =
                        Quaternion.Euler(AdditionalPylonOneRotations[i]);
                    hardpoint.transform.localScale = AdditionalPylonOneScales[i];
                    ApplyExportedOuterPylonVisual(hardpoint, encyclopedia, i);
                }
            }
            if (secondIndex >= 0 &&
                weaponManager.hardpointSets[secondIndex].hardpoints?.Count == AdditionalPylonTwoHardpoints)
            {
                Transform transform =
                    weaponManager.hardpointSets[secondIndex].hardpoints[0].transform;
                transform.localPosition = AdditionalPylonTwoPosition;
                transform.localRotation = CentrelinePylonRotation;
                transform.localScale = Vector3.one;
            }
            if (tailHookIndex >= 0 &&
                weaponManager.hardpointSets[tailHookIndex].hardpoints?.Count > 0)
            {
                Transform transform =
                    weaponManager.hardpointSets[tailHookIndex].hardpoints[0].transform;
                transform.localPosition = TailHookPosition;
                transform.localRotation = TailHookRotation;
                transform.localScale = Vector3.one;
            }
        }

        private static Transform FindAircraftTransform(Aircraft aircraft, string relativePath)
        {
            if (aircraft?.transform == null || string.IsNullOrEmpty(relativePath))
            {
                return null;
            }

            string[] expected = relativePath.Split('/');
            Transform[] transforms = aircraft.GetComponentsInChildren<Transform>(true);
            foreach (Transform candidate in transforms)
            {
                Transform current = candidate;
                int expectedIndex = expected.Length - 1;
                while (current != null && expectedIndex >= 0 &&
                       string.Equals(
                           current.name,
                           expected[expectedIndex],
                           StringComparison.OrdinalIgnoreCase))
                {
                    current = current.parent;
                    expectedIndex--;
                }
                if (expectedIndex < 0)
                {
                    return candidate;
                }
            }
            return null;
        }

        private static void ApplyExportedOuterPylonVisual(
            Hardpoint destination,
            Encyclopedia encyclopedia,
            int sideIndex)
        {
            if (destination?.transform == null || sideIndex < 0 || sideIndex > 1)
            {
                return;
            }

            string expectedModelName = sideIndex == 0
                ? "TGBR Pylon Model - pylon2R"
                : "TGBR Pylon Model - pylon1L";
            Renderer existing = FindRendererByName(destination.transform, expectedModelName);
            if (existing != null)
            {
                SetBasePylonVisual(destination, existing);
                return;
            }

            if (!TryGetVt7PylonTemplate(
                    encyclopedia,
                    sideIndex,
                    out Hardpoint sourceHardpoint,
                    out MeshRenderer sourceRenderer))
            {
                return;
            }

            Transform modelSlot = destination.transform.Find("TGBR Pylon Model Slot");
            if (modelSlot == null)
            {
                GameObject slot = new GameObject("TGBR Pylon Model Slot");
                slot.layer = destination.transform.gameObject.layer;
                slot.hideFlags = destination.transform.gameObject.hideFlags;
                slot.transform.SetParent(destination.transform, false);
                slot.transform.localPosition = Vector3.zero;
                slot.transform.localRotation = Quaternion.identity;
                slot.transform.localScale = Vector3.one;
                modelSlot = slot.transform;
            }

            MeshFilter sourceFilter = sourceRenderer.GetComponent<MeshFilter>();
            if (sourceFilter?.sharedMesh == null)
            {
                return;
            }

            GameObject clone = new GameObject(expectedModelName);
            clone.layer = sourceRenderer.gameObject.layer;
            clone.hideFlags = sourceRenderer.gameObject.hideFlags;
            MeshFilter clonedFilter = clone.AddComponent<MeshFilter>();
            clonedFilter.sharedMesh = sourceFilter.sharedMesh;
            MeshRenderer clonedRenderer = clone.AddComponent<MeshRenderer>();
            CopyPassiveRendererSettings(sourceRenderer, clonedRenderer);
            clone.transform.SetParent(modelSlot, false);

            Vector3 anchorPosition = sourceHardpoint.transform.InverseTransformPoint(
                sourceRenderer.transform.position);
            Quaternion anchorRotation = Quaternion.Inverse(sourceHardpoint.transform.rotation) *
                                        sourceRenderer.transform.rotation;
            Vector3 anchorScale = DivideComponents(
                sourceRenderer.transform.lossyScale,
                sourceHardpoint.transform.lossyScale);
            clone.transform.SetPositionAndRotation(
                destination.transform.TransformPoint(anchorPosition),
                destination.transform.rotation * anchorRotation);
            SetWorldScale(
                clone.transform,
                MultiplyComponents(destination.transform.lossyScale, anchorScale));
            clonedRenderer.enabled = true;
            SetBasePylonVisual(destination, clonedRenderer);
        }

        private static bool TryGetVt7PylonTemplate(
            Encyclopedia encyclopedia,
            int sideIndex,
            out Hardpoint hardpoint,
            out MeshRenderer renderer)
        {
            hardpoint = null;
            renderer = null;
            Encyclopedia source = encyclopedia ?? Encyclopedia.i;
            AircraftDefinition definition = source?.aircraft?.Find(candidate =>
                candidate != null && string.Equals(
                    candidate.jsonKey,
                    Vt7DefinitionKey,
                    StringComparison.OrdinalIgnoreCase));
            if (!TryGetWeaponManager(definition, out WeaponManager weaponManager))
            {
                return false;
            }

            string setName = sideIndex == 0
                ? Vt7OuterWingPylonName
                : Vt7InnerWingPylonName;
            int hardpointIndex = sideIndex == 0 ? 1 : 0;
            HardpointSet set = Array.Find(
                weaponManager.hardpointSets,
                candidate => candidate != null && string.Equals(
                    candidate.name,
                    setName,
                    StringComparison.OrdinalIgnoreCase));
            if (set?.hardpoints == null || hardpointIndex >= set.hardpoints.Count)
            {
                return false;
            }

            hardpoint = set.hardpoints[hardpointIndex];
            renderer = GetBasePylonRenderer(hardpoint) as MeshRenderer;
            return hardpoint?.transform != null && renderer != null;
        }

        private static Renderer FindRendererByName(Transform root, string name)
        {
            if (root == null)
            {
                return null;
            }
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer != null && string.Equals(
                        renderer.gameObject.name,
                        name,
                        StringComparison.Ordinal))
                {
                    return renderer;
                }
            }
            return null;
        }

        private static Renderer GetBasePylonRenderer(Hardpoint hardpoint)
        {
            if (hardpoint != null && PylonOptionsField?.GetValue(hardpoint) is Array options)
            {
                foreach (object option in options)
                {
                    if (option == null)
                    {
                        continue;
                    }
                    bool cargo = (bool)(PylonOptionCargoField?.GetValue(option) ?? false);
                    WeaponMount mount = PylonOptionMountField?.GetValue(option) as WeaponMount;
                    if (!cargo && mount == null)
                    {
                        return PylonOptionRendererField?.GetValue(option) as Renderer;
                    }
                }
            }
            return hardpoint?.Pylon;
        }

        private static void SetBasePylonVisual(Hardpoint hardpoint, Renderer renderer)
        {
            if (hardpoint == null || renderer == null ||
                PylonOptionsField == null || PylonOptionType == null)
            {
                return;
            }

            object option = Activator.CreateInstance(PylonOptionType, nonPublic: true);
            PylonOptionCargoField?.SetValue(option, false);
            PylonOptionMountField?.SetValue(option, null);
            PylonOptionRendererField?.SetValue(option, renderer);
            Array options = Array.CreateInstance(PylonOptionType, 1);
            options.SetValue(option, 0);
            PylonOptionsField.SetValue(hardpoint, options);
            hardpoint.Pylon = renderer;
            hardpoint.Plug = null;
        }

        private static void CopyPassiveRendererSettings(
            MeshRenderer source,
            MeshRenderer destination)
        {
            destination.sharedMaterials = source.sharedMaterials;
            destination.shadowCastingMode = source.shadowCastingMode;
            destination.receiveShadows = source.receiveShadows;
            destination.lightProbeUsage = source.lightProbeUsage;
            destination.reflectionProbeUsage = source.reflectionProbeUsage;
            destination.probeAnchor = source.probeAnchor;
            destination.allowOcclusionWhenDynamic = source.allowOcclusionWhenDynamic;
            destination.sortingLayerID = source.sortingLayerID;
            destination.sortingOrder = source.sortingOrder;
        }

        private static Vector3 DivideComponents(Vector3 value, Vector3 divisor)
        {
            return new Vector3(
                Mathf.Abs(divisor.x) < 0.000001f ? value.x : value.x / divisor.x,
                Mathf.Abs(divisor.y) < 0.000001f ? value.y : value.y / divisor.y,
                Mathf.Abs(divisor.z) < 0.000001f ? value.z : value.z / divisor.z);
        }

        private static Vector3 MultiplyComponents(Vector3 left, Vector3 right)
        {
            return new Vector3(left.x * right.x, left.y * right.y, left.z * right.z);
        }

        private static void SetWorldScale(Transform transform, Vector3 worldScale)
        {
            Vector3 parentScale = transform.parent == null
                ? Vector3.one
                : transform.parent.lossyScale;
            transform.localScale = DivideComponents(worldScale, parentScale);
        }

        private static Hardpoint CloneHardpoint(Hardpoint source, string pylonName, int hardpointNumber)
        {
            GameObject mountPoint = new GameObject(pylonName + " Hardpoint " + hardpointNumber);
            Transform sourceTransform = source.transform;
            if (sourceTransform != null)
            {
                mountPoint.layer = sourceTransform.gameObject.layer;
                mountPoint.hideFlags = sourceTransform.gameObject.hideFlags;
                mountPoint.transform.SetParent(sourceTransform.parent, false);
                mountPoint.transform.localPosition = sourceTransform.localPosition;
                mountPoint.transform.localRotation = sourceTransform.localRotation;
                mountPoint.transform.localScale = sourceTransform.localScale;
            }

            Hardpoint clone = new Hardpoint
            {
                transform = mountPoint.transform,
                part = source.part,
                bayDoors = Array.Empty<BayDoor>(),
                doorOpenDuration = source.doorOpenDuration,
                Pylon = null,
                Plug = null,
                BuiltInWeapons = Array.Empty<Weapon>(),
                BuiltInTurrets = Array.Empty<Turret>(),
                HardpointIndex = -1
            };
            ClearPylonVisuals(clone);
            return clone;
        }

        private static bool TryGetWeaponManager(
            AircraftDefinition definition,
            out WeaponManager weaponManager)
        {
            weaponManager = null;
            if (definition?.unitPrefab == null)
            {
                return false;
            }

            Aircraft prefabAircraft = definition.unitPrefab.GetComponent<Aircraft>();
            weaponManager = prefabAircraft != null
                ? prefabAircraft.weaponManager
                : definition.unitPrefab.GetComponentInChildren<WeaponManager>(true);
            return weaponManager != null;
        }

        private static int MigrateParameters(
            AircraftParameters parameters,
            int originalHardpointSetCount,
            int hardpointSetCount,
            int centrelineIndex)
        {
            if (parameters == null)
            {
                return 0;
            }

            int migrated = 0;
            if (parameters.loadouts != null)
            {
                foreach (Loadout loadout in parameters.loadouts)
                {
                    migrated += MigrateLoadout(
                        loadout,
                        originalHardpointSetCount,
                        hardpointSetCount,
                        centrelineIndex);
                    SynchronizeCentrelineStore(prefabWeaponManager, loadout);
                }
            }

            if (parameters.StandardLoadouts != null)
            {
                foreach (StandardLoadout standardLoadout in parameters.StandardLoadouts)
                {
                    if (standardLoadout != null)
                    {
                        migrated += MigrateLoadout(
                            standardLoadout.loadout,
                            originalHardpointSetCount,
                            hardpointSetCount,
                            centrelineIndex);
                        SynchronizeCentrelineStore(prefabWeaponManager, standardLoadout.loadout);
                    }
                }
            }

            return migrated;
        }

        private static int MigrateLoadout(
            Loadout loadout,
            int originalHardpointSetCount,
            int hardpointSetCount,
            int centrelineIndex)
        {
            if (loadout?.weapons == null || originalHardpointSetCount <= 0)
            {
                return 0;
            }

            if (centrelineIndex < 0 || centrelineIndex >= originalHardpointSetCount)
            {
                return 0;
            }

            WeaponMount centrelineMount = centrelineIndex < loadout.weapons.Count
                ? loadout.weapons[centrelineIndex]
                : null;
            WeaponMount previousAdditionalPylonOne = originalHardpointSetCount < loadout.weapons.Count
                ? loadout.weapons[originalHardpointSetCount]
                : null;

            List<WeaponMount> migrated = new List<WeaponMount>(hardpointSetCount);
            for (int i = 0; i < originalHardpointSetCount; i++)
            {
                migrated.Add(i < loadout.weapons.Count ? loadout.weapons[i] : null);
                if (i == centrelineIndex)
                {
                    migrated.Add(centrelineMount);
                }
            }
            migrated.Add(previousAdditionalPylonOne);
            while (migrated.Count < hardpointSetCount)
            {
                migrated.Add(null);
            }
            if (migrated.Count > hardpointSetCount)
            {
                migrated.RemoveRange(hardpointSetCount, migrated.Count - hardpointSetCount);
            }

            loadout.weapons.Clear();
            loadout.weapons.AddRange(migrated);

            return 1;
        }

        private static void EnsureLoadoutLength(Loadout loadout, int hardpointSetCount)
        {
            if (loadout?.weapons == null)
            {
                return;
            }
            while (loadout.weapons.Count < hardpointSetCount)
            {
                loadout.weapons.Add(null);
            }
        }

        private static int EnsureParameterLoadoutLengths(
            AircraftParameters parameters,
            int hardpointSetCount)
        {
            if (parameters == null)
            {
                return 0;
            }

            int extended = 0;
            if (parameters.loadouts != null)
            {
                foreach (Loadout loadout in parameters.loadouts)
                {
                    int previousCount = loadout?.weapons?.Count ?? -1;
                    EnsureLoadoutLength(loadout, hardpointSetCount);
                    if (previousCount >= 0 && previousCount < hardpointSetCount)
                    {
                        extended++;
                    }
                }
            }

            if (parameters.StandardLoadouts != null)
            {
                foreach (StandardLoadout standardLoadout in parameters.StandardLoadouts)
                {
                    Loadout loadout = standardLoadout?.loadout;
                    int previousCount = loadout?.weapons?.Count ?? -1;
                    EnsureLoadoutLength(loadout, hardpointSetCount);
                    if (previousCount >= 0 && previousCount < hardpointSetCount)
                    {
                        extended++;
                    }
                }
            }
            return extended;
        }

        private static void SynchronizeCentrelineStore(WeaponManager weaponManager, Loadout loadout)
        {
            if (weaponManager?.hardpointSets == null || loadout?.weapons == null)
            {
                return;
            }
            int centrelineIndex = FindCentrelineIndex(weaponManager);
            int partnerIndex = FindRearCentrelineIndex(weaponManager);
            if (centrelineIndex < 0 || partnerIndex < 0)
            {
                return;
            }
            EnsureLoadoutLength(loadout, weaponManager.hardpointSets.Length);
            WeaponMount mount = loadout.weapons[centrelineIndex];
            if (!IsCentrelineMountAllowed(mount) ||
                weaponManager.hardpointSets[centrelineIndex].BlockedByOtherHardpoint(loadout))
            {
                mount = null;
                loadout.weapons[centrelineIndex] = null;
            }
            loadout.weapons[partnerIndex] = mount;
        }

        private static int FindSetIndex(WeaponManager weaponManager, string setName)
        {
            return weaponManager?.hardpointSets == null
                ? -1
                : Array.FindIndex(
                    weaponManager.hardpointSets,
                    set => set != null &&
                           string.Equals(set.name, setName, StringComparison.OrdinalIgnoreCase));
        }

        private static int FindCentrelineIndex(WeaponManager weaponManager)
        {
            int index = FindSetIndex(weaponManager, ForeCentrelinePylonName);
            if (index < 0)
            {
                index = FindSetIndex(weaponManager, LegacyForwardCentrelinePylonName);
            }
            return index >= 0 ? index : FindSetIndex(weaponManager, OriginalCentrelinePylonName);
        }

        private static int FindRearCentrelineIndex(WeaponManager weaponManager)
        {
            int index = FindSetIndex(weaponManager, AftCentrelinePylonName);
            if (index < 0)
            {
                index = FindSetIndex(weaponManager, LegacyRearCentrelinePylonName);
            }
            return index >= 0 ? index : FindSetIndex(weaponManager, LegacyAdditionalPylonTwoName);
        }

        private static int FindAddedOuterPylonIndex(WeaponManager weaponManager)
        {
            int index = FindSetIndex(weaponManager, OuterPylonName);
            if (index < 0)
            {
                index = FindSetIndex(weaponManager, LegacyOuterPylonName);
            }
            return index >= 0 ? index : FindSetIndex(weaponManager, LegacyAdditionalPylonOneName);
        }

        private static int FindMidWingPylonIndex(WeaponManager weaponManager)
        {
            int index = FindSetIndex(weaponManager, MidWingPylonName);
            return index >= 0 ? index : FindSetIndex(weaponManager, OriginalOuterWingPylonsName);
        }

        private static void ApplyPylonNames(WeaponManager weaponManager)
        {
            int centrelineIndex = FindCentrelineIndex(weaponManager);
            int rearIndex = FindRearCentrelineIndex(weaponManager);
            int outerIndex = FindAddedOuterPylonIndex(weaponManager);
            int midWingIndex = FindMidWingPylonIndex(weaponManager);
            if (centrelineIndex >= 0)
            {
                HardpointSet forward = weaponManager.hardpointSets[centrelineIndex];
                forward.name = ForeCentrelinePylonName;
                forward.SymmetryName = CombinedCentrelinePylonName;
            }
            if (rearIndex >= 0)
            {
                HardpointSet rear = weaponManager.hardpointSets[rearIndex];
                rear.name = AftCentrelinePylonName;
                rear.SymmetryName = CombinedCentrelinePylonName;
            }
            if (outerIndex >= 0)
            {
                weaponManager.hardpointSets[outerIndex].name = OuterPylonName;
            }
            if (midWingIndex >= 0)
            {
                weaponManager.hardpointSets[midWingIndex].name = MidWingPylonName;
            }
        }

        private static bool ApplyCentrelineWeaponLimits(WeaponManager weaponManager)
        {
            int centrelineIndex = FindCentrelineIndex(weaponManager);
            int rearIndex = FindRearCentrelineIndex(weaponManager);
            if (centrelineIndex < 0 || rearIndex < 0)
            {
                return false;
            }

            HardpointSet forward = weaponManager.hardpointSets[centrelineIndex];
            HardpointSet rear = weaponManager.hardpointSets[rearIndex];
            if (MountKeysMatchCentrelinePolicy(forward.weaponOptions) &&
                MountListsMatch(forward.weaponOptions, rear.weaponOptions))
            {
                return true;
            }

            List<WeaponMount> allowed = new List<WeaponMount>(CentrelineMountKeys.Length);
            foreach (string mountKey in CentrelineMountKeys)
            {
                WeaponMount mount = FindMount(weaponManager.hardpointSets, mountKey);
                if (mount == null)
                {
                    Plugin.ModLogger?.LogError(
                        "F-99 centerline weapon limit could not find mount " + mountKey + ".");
                    return false;
                }
                allowed.Add(mount);
            }

            forward.weaponOptions = new List<WeaponMount>(allowed);
            rear.weaponOptions = new List<WeaponMount>(allowed);
            return true;
        }

        private static bool ValidateTargetingPodConfiguration(
            WeaponManager weaponManager,
            Aircraft aircraft,
            out string failure)
        {
            failure = string.Empty;
            int index = FindTargetingPodPylonIndex(weaponManager);
            if (index < 0)
            {
                failure = "TGP hardpoint set is missing";
                return false;
            }

            HardpointSet set = weaponManager.hardpointSets[index];
            if (!string.Equals(set.name, TargetingPodPylonName, StringComparison.Ordinal) ||
                set.hardpoints?.Count != 1)
            {
                failure = "hardpoint-set name or count is incorrect";
                return false;
            }
            if (set.SymmetryWithPrev ||
                !string.IsNullOrEmpty(set.SymmetryName) ||
                (set.precludingHardpointSets?.Count ?? 0) != 0)
            {
                failure = "unexpected symmetry or pylon exclusions remain";
                return false;
            }
            if (set.weaponOptions?.Count != 1 ||
                !string.Equals(
                    set.weaponOptions[0]?.jsonKey,
                    TargetingPodMountKey,
                    StringComparison.OrdinalIgnoreCase))
            {
                failure = "weapon list is not limited to Aryx_ExternalTargetingPod_Trainer";
                return false;
            }

            Hardpoint hardpoint = set.hardpoints[0];
            if (hardpoint?.transform == null || aircraft == null ||
                hardpoint.transform.parent != aircraft.transform)
            {
                failure = "weapon anchor is not parented to the aircraft root";
                return false;
            }
            if (!Approximately(hardpoint.transform.localPosition, TargetingPodPosition) ||
                !Approximately(hardpoint.transform.localScale, Vector3.one) ||
                Quaternion.Angle(hardpoint.transform.localRotation, TargetingPodRotation) > 0.01f)
            {
                failure = "weapon-anchor transform does not match the exported configuration";
                return false;
            }
            if (hardpoint.part == null)
            {
                failure = "hardpoint has no UnitPart attachment";
                return false;
            }
            if (hardpoint.Pylon != null || hardpoint.Plug != null)
            {
                failure = "pylon or plug visual was not cleared";
                return false;
            }
            if (PylonOptionsField != null &&
                PylonOptionsField.GetValue(hardpoint) is Array pylonOptions &&
                pylonOptions.Length != 0)
            {
                failure = "pylon visual options were not cleared";
                return false;
            }
            return true;
        }

        private static bool ValidateLinkedCentrelineConfiguration(
            WeaponManager weaponManager,
            out string failure)
        {
            failure = string.Empty;
            if (!TryGetAdditionalPylons(weaponManager, out HardpointSet first, out HardpointSet second))
            {
                failure = "added pylon counts or symmetry flag are incorrect";
                return false;
            }

            int centrelineIndex = FindCentrelineIndex(weaponManager);
            int partnerIndex = FindRearCentrelineIndex(weaponManager);
            if (centrelineIndex < 0 || partnerIndex != centrelineIndex + 1)
            {
                failure = "Aft Centerline Pylon is not immediately after Fore Centerline Pylon";
                return false;
            }

            HardpointSet centreline = weaponManager.hardpointSets[centrelineIndex];
            if (!string.Equals(centreline.name, ForeCentrelinePylonName, StringComparison.Ordinal) ||
                !string.Equals(second.name, AftCentrelinePylonName, StringComparison.Ordinal) ||
                !string.Equals(centreline.SymmetryName, CombinedCentrelinePylonName, StringComparison.Ordinal))
            {
                failure = "combined or split centerline names are incorrect";
                return false;
            }
            if (!Approximately(centreline.hardpoints[0].transform.localPosition, CentrelinePylonPosition) ||
                !Approximately(first.hardpoints[0].transform.localPosition, AdditionalPylonOnePositions[0]) ||
                !Approximately(first.hardpoints[1].transform.localPosition, AdditionalPylonOnePositions[1]) ||
                !Approximately(second.hardpoints[0].transform.localPosition, AdditionalPylonTwoPosition))
            {
                failure = "one or more exported hardpoint positions do not match";
                return false;
            }
            if (Quaternion.Angle(
                    centreline.hardpoints[0].transform.localRotation,
                    CentrelinePylonRotation) > 0.01f ||
                Quaternion.Angle(
                    second.hardpoints[0].transform.localRotation,
                    CentrelinePylonRotation) > 0.01f ||
                Quaternion.Angle(
                    first.hardpoints[0].transform.localRotation,
                    Quaternion.Euler(AdditionalPylonOneRotations[0])) > 0.01f ||
                Quaternion.Angle(
                    first.hardpoints[1].transform.localRotation,
                    Quaternion.Euler(AdditionalPylonOneRotations[1])) > 0.01f ||
                !Approximately(first.hardpoints[0].transform.localScale, AdditionalPylonOneScales[0]) ||
                !Approximately(first.hardpoints[1].transform.localScale, AdditionalPylonOneScales[1]))
            {
                failure = "one or more exported hardpoint rotations or scales do not match";
                return false;
            }
            if (first.hardpoints[0].Pylon == null || first.hardpoints[1].Pylon == null ||
                GetBasePylonRenderer(first.hardpoints[0]) == null ||
                GetBasePylonRenderer(first.hardpoints[1]) == null)
            {
                failure = "exported VT-7 outer-pylon visuals are missing";
                return false;
            }

            if (first.weaponOptions == null || first.weaponOptions.Count != 1 ||
                !string.Equals(
                    first.weaponOptions[0]?.jsonKey,
                    AdditionalPylonOneMountKey,
                    StringComparison.OrdinalIgnoreCase))
            {
                failure = "Outer Wing Pylon is not limited to AAM3_single";
                return false;
            }

            int midWingIndex = FindMidWingPylonIndex(weaponManager);
            if (!string.Equals(first.name, OuterPylonName, StringComparison.Ordinal) ||
                midWingIndex < 0 ||
                !string.Equals(
                    weaponManager.hardpointSets[midWingIndex].name,
                    MidWingPylonName,
                    StringComparison.Ordinal))
            {
                failure = "Outer Wing Pylon or Mid Wing Pylon name is incorrect";
                return false;
            }

            if (!MountListsMatch(centreline.weaponOptions, second.weaponOptions) ||
                !ByteListsMatch(centreline.precludingHardpointSets, second.precludingHardpointSets))
            {
                failure = "centreline partner options or bay-exclusion indexes differ";
                return false;
            }

            if (!MountKeysMatchCentrelinePolicy(centreline.weaponOptions))
            {
                failure = "centerline is not limited to AGM-68, CB-400, and GPO-500";
                return false;
            }

            if (second.hardpoints[0].part != centreline.hardpoints[0].part)
            {
                failure = "centreline partner is attached to a different UnitPart";
                return false;
            }

            return true;
        }

        private static bool Approximately(Vector3 left, Vector3 right)
        {
            return Mathf.Approximately(left.x, right.x) &&
                   Mathf.Approximately(left.y, right.y) &&
                   Mathf.Approximately(left.z, right.z);
        }

        private static bool MountListsMatch(List<WeaponMount> left, List<WeaponMount> right)
        {
            if (left == null || right == null || left.Count != right.Count)
            {
                return false;
            }
            for (int i = 0; i < left.Count; i++)
            {
                if (left[i] != right[i])
                {
                    return false;
                }
            }
            return true;
        }

        private static bool MountKeysMatchCentrelinePolicy(List<WeaponMount> mounts)
        {
            if (mounts == null || mounts.Count != CentrelineMountKeys.Length)
            {
                return false;
            }
            for (int i = 0; i < CentrelineMountKeys.Length; i++)
            {
                if (!string.Equals(
                        mounts[i]?.jsonKey,
                        CentrelineMountKeys[i],
                        StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool IsCentrelineMountAllowed(WeaponMount mount)
        {
            if (mount == null)
            {
                return true;
            }
            foreach (string mountKey in CentrelineMountKeys)
            {
                if (string.Equals(mount.jsonKey, mountKey, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool ByteListsMatch(List<byte> left, List<byte> right)
        {
            if (left == null || right == null || left.Count != right.Count)
            {
                return false;
            }
            for (int i = 0; i < left.Count; i++)
            {
                if (left[i] != right[i])
                {
                    return false;
                }
            }
            return true;
        }

        private static bool HasAdditionalPylons(HardpointSet[] hardpointSets)
        {
            if (hardpointSets == null)
            {
                return false;
            }

            bool firstFound = false;
            bool secondFound = false;
            foreach (HardpointSet hardpointSet in hardpointSets)
            {
                if (hardpointSet == null)
                {
                    continue;
                }

                if (IsAddedOuterPylonName(hardpointSet.name))
                {
                    firstFound = hardpointSet.hardpoints?.Count == AdditionalPylonOneHardpoints;
                }
                else if (IsRearCentrelineName(hardpointSet.name))
                {
                    secondFound = hardpointSet.hardpoints?.Count == AdditionalPylonTwoHardpoints &&
                                  hardpointSet.SymmetryWithPrev;
                }
            }

            return firstFound && secondFound;
        }

        private static bool HasTargetingPodPylon(WeaponManager weaponManager)
        {
            return FindTargetingPodPylonIndex(weaponManager) >= 0;
        }

        private static bool IsRearCentrelineName(string name)
        {
            return string.Equals(name, AftCentrelinePylonName, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(name, LegacyRearCentrelinePylonName, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(name, LegacyAdditionalPylonTwoName, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsAddedOuterPylonName(string name)
        {
            return string.Equals(name, OuterPylonName, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(name, LegacyOuterPylonName, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(name, LegacyAdditionalPylonOneName, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsF99(AircraftDefinition definition)
        {
            if (definition == null)
            {
                return false;
            }

            return string.Equals(definition.jsonKey, F99DefinitionKey, StringComparison.OrdinalIgnoreCase) ||
                   ContainsIgnoreCase(definition.jsonKey, "LightFighter1") ||
                   ContainsIgnoreCase(definition.unitName, "F-99") ||
                   ContainsIgnoreCase(definition.unitName, "Shrike") ||
                   string.Equals(definition.code, "F-99", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ContainsIgnoreCase(string value, string expected)
        {
            return value != null &&
                   value.IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    [HarmonyPatch]
    internal static class F99EncyclopediaAfterLoadPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Encyclopedia), "AfterLoad", Type.EmptyTypes);
        }

        private static void Postfix(Encyclopedia __instance)
        {
            F99PylonExpansion.RegisterEncyclopedia(__instance);
        }
    }

    [HarmonyPatch(typeof(WeaponManager), "Awake")]
    internal static class F99WeaponManagerAwakePatch
    {
        private static void Prefix(WeaponManager __instance)
        {
            F99PylonExpansion.ApplyToWeaponManager(__instance);
        }
    }

    [HarmonyPatch(typeof(WeaponManager), nameof(WeaponManager.SpawnWeapons))]
    internal static class F99SpawnWeaponsPatch
    {
        private static void Prefix(WeaponManager __instance)
        {
            F99PylonExpansion.ApplyToWeaponManager(__instance);
        }
    }
}

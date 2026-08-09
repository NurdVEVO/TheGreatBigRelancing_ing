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
            new Vector3(1.525f, -0.115f, 0.055f),
            new Vector3(-1.525f, -0.115f, -0.055f)
        };

        private static readonly Vector3 CentrelinePylonPosition =
            new Vector3(0f, -0.295f, 0.688f);

        private static readonly Vector3 AdditionalPylonTwoPosition =
            new Vector3(0f, -0.215f, -2.155f);

        private static readonly FieldInfo PylonOptionsField =
            AccessTools.Field(typeof(Hardpoint), "pylonOptions");

        private static Encyclopedia pendingEncyclopedia;
        private static float nextPendingAttempt;
        private static float pendingDeadline;
        private static bool loggedSuccess;
        private static bool loggedValidation;
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

            if (TryApplyToEncyclopedia(pendingEncyclopedia))
            {
                pendingEncyclopedia = null;
                return;
            }

            if (Time.unscaledTime >= pendingDeadline)
            {
                pendingEncyclopedia = null;
                Plugin.ModLogger?.LogWarning(
                    "F-99 Shrike pylon expansion was not applied because the Aryx Shrike definition was not loaded.");
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

            ApplyToDefinition(aircraft.definition);
            int originalHardpointSetCount = weaponManager.hardpointSets?.Length ?? 0;
            bool expanded = ExpandWeaponManager(weaponManager);
            ApplyPylonNames(weaponManager);
            ApplyCentrelineWeaponLimits(weaponManager);
            ApplyExportedGeometry(weaponManager);
            if (expanded)
            {
                MigrateLoadout(aircraft.loadout, originalHardpointSetCount, weaponManager.hardpointSets.Length);
            }
            else
            {
                EnsureLoadoutLength(aircraft.loadout, weaponManager.hardpointSets?.Length ?? 0);
            }
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

            return ApplyToDefinition(definition);
        }

        private static bool ApplyToDefinition(AircraftDefinition definition)
        {
            if (!TryGetWeaponManager(definition, out WeaponManager weaponManager))
            {
                return false;
            }

            prefabAircraft = definition.unitPrefab.GetComponent<Aircraft>() ??
                             definition.unitPrefab.GetComponentInChildren<Aircraft>(true);
            prefabWeaponManager = weaponManager;

            int originalHardpointSetCount = weaponManager.hardpointSets.Length;
            bool expanded = ExpandWeaponManager(weaponManager);
            ApplyPylonNames(weaponManager);
            ApplyCentrelineWeaponLimits(weaponManager);
            ApplyExportedGeometry(weaponManager);
            int hardpointSetCount = weaponManager.hardpointSets.Length;
            int migrated = expanded
                ? MigrateParameters(definition.aircraftParameters, originalHardpointSetCount, hardpointSetCount)
                : 0;

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

        private static void ApplyExportedGeometry(WeaponManager weaponManager)
        {
            if (weaponManager?.hardpointSets == null)
            {
                return;
            }

            int centrelineIndex = FindCentrelineIndex(weaponManager);
            int firstIndex = FindAddedOuterPylonIndex(weaponManager);
            int secondIndex = FindRearCentrelineIndex(weaponManager);
            if (centrelineIndex >= 0 &&
                weaponManager.hardpointSets[centrelineIndex].hardpoints?.Count > 0)
            {
                weaponManager.hardpointSets[centrelineIndex].hardpoints[0].transform.localPosition =
                    CentrelinePylonPosition;
            }
            if (firstIndex >= 0 &&
                weaponManager.hardpointSets[firstIndex].hardpoints?.Count == AdditionalPylonOneHardpoints)
            {
                for (int i = 0; i < AdditionalPylonOneHardpoints; i++)
                {
                    weaponManager.hardpointSets[firstIndex].hardpoints[i].transform.localPosition =
                        AdditionalPylonOnePositions[i];
                }
            }
            if (secondIndex >= 0 &&
                weaponManager.hardpointSets[secondIndex].hardpoints?.Count == AdditionalPylonTwoHardpoints)
            {
                weaponManager.hardpointSets[secondIndex].hardpoints[0].transform.localPosition =
                    AdditionalPylonTwoPosition;
            }
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

            if (PylonOptionsField != null)
            {
                Type elementType = PylonOptionsField.FieldType.GetElementType();
                PylonOptionsField.SetValue(clone, Array.CreateInstance(elementType, 0));
            }

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
            int hardpointSetCount)
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
                    migrated += MigrateLoadout(loadout, originalHardpointSetCount, hardpointSetCount);
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
                            hardpointSetCount);
                        SynchronizeCentrelineStore(prefabWeaponManager, standardLoadout.loadout);
                    }
                }
            }

            return migrated;
        }

        private static int MigrateLoadout(
            Loadout loadout,
            int originalHardpointSetCount,
            int hardpointSetCount)
        {
            if (loadout?.weapons == null || originalHardpointSetCount <= 0)
            {
                return 0;
            }

            int centrelineIndex = FindCentrelineIndex(prefabWeaponManager);
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

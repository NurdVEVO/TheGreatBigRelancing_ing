using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using NuclearOption.SavedMission;
using UnityEngine;

namespace TheGreatBigRebalancing.Balance
{
    internal static class Ab4Rebalanacing
    {
        private const string Ab4DefinitionKey = "FastBomber1";
        private const string Ab4VanillaDisplayName = "Alkyon AB-4";
        private const string Ab4DisplayName = "AB-4 \"Buford\"";
        private const string Ab4Description =
            "The Alkyon Bernard Design Bureau AB-4 (NATO Reporting name \"Buford\") is an " +
            "advanced supersonic bomber and electronic warfare support platform, designed to " +
            "stealthily deliver nuclear or conventional payloads onto high value targets. 4 " +
            "afterburning engines combined with variable geometry wings provide incredible top " +
            "speed and enough maneuverability to evade aerial threats. If critically damaged, " +
            "a crew escape capsule allows for safe ejection at any altitude or airspeed.";
        private const string S2MountKey = "AAM3_single_internal";
        private const string JammerMountKey = "JammingPod1";
        private const string CombinedWingPylonName = "Wing Pylons";
        private const string LeftWingPylonName = "Left Wing Pylon";
        private const string RightWingPylonName = "Right Wing Pylon";
        private static readonly HashSet<string> ForbiddenMountKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "AAM2x8", // AAM-29 Scythe x8
            "AAM4x8"  // AAM-36 Scimitar x8
        };

        private static bool loggedRuntimeFallback;
        private static bool loggedDualChannelJammer;
        private static bool loggedEncyclopediaApply;

        private static readonly FieldInfo[] JammingPodChannelFields =
        {
            AccessTools.Field(typeof(JammingPod), "power"),
            AccessTools.Field(typeof(JammingPod), "effectiveness"),
            AccessTools.Field(typeof(JammingPod), "rangeFalloff"),
            AccessTools.Field(typeof(JammingPod), "directionTransform")
        };

        internal static void ApplyToEncyclopedia(Encyclopedia encyclopedia)
        {
            if (encyclopedia == null || encyclopedia.aircraft == null)
            {
                return;
            }

            AircraftDefinition definition = encyclopedia.aircraft.Find(IsAb4);
            if (definition == null)
            {
                Plugin.ModLogger?.LogError("AB-4 rebalance could not find the FastBomber1 aircraft definition.");
                return;
            }
            definition.description = Ab4Description;

            if (!TryGetWeaponManager(definition, out WeaponManager weaponManager))
            {
                return;
            }

            int removedOptions = RemoveForbiddenOptions(weaponManager.hardpointSets);
            bool splitWingPylons = SplitWingPylons(weaponManager, out int wingPylonIndex);
            int clearedLoadoutSlots = SanitizeParameters(definition.aircraftParameters);
            int migratedLoadouts = MigrateParameters(
                definition.aircraftParameters,
                wingPylonIndex,
                weaponManager.hardpointSets?.Length ?? 0);

            if (!loggedEncyclopediaApply && HasSplitWingPylons(weaponManager.hardpointSets))
            {
                loggedEncyclopediaApply = true;
                Plugin.ModLogger?.LogInfo(
                    "AB-4 rebalance active: removed " + removedOptions +
                    " offensive air-to-air hardpoint option(s) and cleared " + clearedLoadoutSlots +
                    " default loadout slot(s). Wing pylons " +
                    (splitWingPylons ? "were split" : "are split") + " and " +
                    migratedLoadouts + " loadout(s) were migrated. " +
                    "Each jammer pod supports two targets (four with both pods equipped). " +
                    "IRM-S2 remains available in the heater bays.");
            }
        }

        internal static void ApplyToWeaponManager(WeaponManager weaponManager)
        {
            if (weaponManager == null)
            {
                return;
            }

            Aircraft aircraft = weaponManager.GetComponentInParent<Aircraft>();
            if (aircraft == null || !IsAb4(aircraft.definition))
            {
                return;
            }

            bool splitWingPylons = SplitWingPylons(weaponManager, out int wingPylonIndex);
            int migratedLoadouts = HasSplitWingPylons(weaponManager.hardpointSets)
                ? MigrateLoadout(
                    aircraft.loadout,
                    wingPylonIndex,
                    weaponManager.hardpointSets?.Length ?? 0)
                : 0;
            int removedOptions = RemoveForbiddenOptions(weaponManager.hardpointSets);
            int clearedLoadoutSlots = SanitizeLoadout(aircraft.loadout);

            if (!loggedRuntimeFallback &&
                (splitWingPylons || migratedLoadouts > 0 || removedOptions > 0 || clearedLoadoutSlots > 0))
            {
                loggedRuntimeFallback = true;
                Plugin.ModLogger?.LogInfo(
                    "Applied AB-4 runtime rebalance: independent wing pylons, two targets per jammer pod, " +
                    "and IRM-S2-only air-to-air armament.");
            }
        }

        internal static void AddSecondJammerChannel(
            WeaponManager weaponManager,
            Weapon weapon,
            WeaponMount weaponMount,
            Hardpoint hardpoint)
        {
            JammingPod primaryChannel = weapon as JammingPod;
            if (primaryChannel == null ||
                weaponManager == null ||
                weaponMount == null ||
                !string.Equals(weaponMount.jsonKey, JammerMountKey, StringComparison.OrdinalIgnoreCase) ||
                primaryChannel.GetComponent<Ab4DualChannelJammerMarker>() != null)
            {
                return;
            }

            Aircraft aircraft = weaponManager.GetComponentInParent<Aircraft>();
            if (aircraft == null || !IsAb4(aircraft.definition))
            {
                return;
            }

            WeaponStation station = aircraft.weaponStations.Find(candidate => candidate.Weapons.Contains(primaryChannel));
            if (station == null || Array.Exists(JammingPodChannelFields, field => field == null))
            {
                Plugin.ModLogger?.LogError("Could not provision the AB-4 jammer's second target channel.");
                return;
            }

            primaryChannel.gameObject.AddComponent<Ab4DualChannelJammerMarker>();
            JammingPod secondaryChannel = primaryChannel.gameObject.AddComponent<JammingPod>();
            CopyJammerChannel(primaryChannel, secondaryChannel);
            station.RegisterWeapon(secondaryChannel, aircraft, weaponMount, hardpoint);

            if (!loggedDualChannelJammer)
            {
                loggedDualChannelJammer = true;
                Plugin.ModLogger?.LogInfo(
                    "AB-4 jammer enhancement active: each physical pod now has two independent target channels.");
            }
        }

        private static void CopyJammerChannel(JammingPod source, JammingPod destination)
        {
            foreach (FieldInfo field in JammingPodChannelFields)
            {
                field.SetValue(destination, field.GetValue(source));
            }

            destination.info = source.info;
            destination.ammo = source.ammo;
            destination.priority = source.priority;
            destination.Rearmable = source.Rearmable;
            destination.RequestRearmLevel = source.RequestRearmLevel;
            destination.Safety = source.Safety;
        }

        private static bool TryGetWeaponManager(AircraftDefinition definition, out WeaponManager weaponManager)
        {
            weaponManager = null;
            if (definition.unitPrefab == null)
            {
                Plugin.ModLogger?.LogError("AB-4 definition has no aircraft prefab; hardpoint options were not changed.");
                return false;
            }

            Aircraft prefabAircraft = definition.unitPrefab.GetComponent<Aircraft>();
            weaponManager = prefabAircraft != null
                ? prefabAircraft.weaponManager
                : definition.unitPrefab.GetComponentInChildren<WeaponManager>(true);
            if (weaponManager == null)
            {
                Plugin.ModLogger?.LogError("AB-4 prefab has no WeaponManager; hardpoint options were not changed.");
                return false;
            }

            return true;
        }

        private static bool SplitWingPylons(
            WeaponManager weaponManager,
            out int leftPylonIndex)
        {
            leftPylonIndex = FindSetIndex(weaponManager?.hardpointSets, LeftWingPylonName);
            if (weaponManager?.hardpointSets == null)
            {
                return false;
            }
            if (HasSplitWingPylons(weaponManager.hardpointSets))
            {
                return false;
            }

            int combinedIndex = Array.FindIndex(
                weaponManager.hardpointSets,
                set => set != null &&
                       string.Equals(set.name, CombinedWingPylonName, StringComparison.OrdinalIgnoreCase));

            if (combinedIndex < 0)
            {
                Plugin.ModLogger?.LogError("AB-4 combined wing-pylon set was not found.");
                return false;
            }

            HardpointSet combined = weaponManager.hardpointSets[combinedIndex];
            if (combined.hardpoints == null || combined.hardpoints.Count != 2)
            {
                Plugin.ModLogger?.LogError(
                    "AB-4 combined wing-pylon set did not contain the expected two physical hardpoints.");
                return false;
            }

            ShiftPreclusionIndexesForInsertion(
                weaponManager.hardpointSets,
                combinedIndex + 1);
            HardpointSet left = CreateSplitPylon(combined, combined.hardpoints[0], LeftWingPylonName);
            HardpointSet right = CreateSplitPylon(combined, combined.hardpoints[1], RightWingPylonName);
            List<HardpointSet> splitSets = new List<HardpointSet>(weaponManager.hardpointSets);
            splitSets[combinedIndex] = left;
            splitSets.Insert(combinedIndex + 1, right);
            weaponManager.hardpointSets = splitSets.ToArray();
            leftPylonIndex = combinedIndex;
            return true;
        }

        private static int FindSetIndex(HardpointSet[] hardpointSets, string name)
        {
            return hardpointSets == null
                ? -1
                : Array.FindIndex(
                    hardpointSets,
                    set => set != null &&
                           string.Equals(set.name, name, StringComparison.OrdinalIgnoreCase));
        }

        private static void ShiftPreclusionIndexesForInsertion(
            HardpointSet[] hardpointSets,
            int insertionIndex)
        {
            foreach (HardpointSet set in hardpointSets)
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

        private static HardpointSet CreateSplitPylon(HardpointSet source, Hardpoint hardpoint, string name)
        {
            return new HardpointSet
            {
                name = name,
                precludingHardpointSets = source.precludingHardpointSets != null
                    ? new List<byte>(source.precludingHardpointSets)
                    : new List<byte>(),
                SymmetryWithPrev = false,
                SymmetryName = string.Empty,
                weaponOptions = source.weaponOptions != null
                    ? new List<WeaponMount>(source.weaponOptions)
                    : new List<WeaponMount>(),
                weaponMount = null,
                hardpoints = new List<Hardpoint> { hardpoint }
            };
        }

        private static bool HasSplitWingPylons(HardpointSet[] hardpointSets)
        {
            if (hardpointSets == null)
            {
                return false;
            }

            int leftIndex = FindSetIndex(hardpointSets, LeftWingPylonName);
            int rightIndex = FindSetIndex(hardpointSets, RightWingPylonName);
            return leftIndex >= 0 && rightIndex == leftIndex + 1 &&
                   hardpointSets[leftIndex].hardpoints?.Count == 1 &&
                   hardpointSets[rightIndex].hardpoints?.Count == 1;
        }

        private static int RemoveForbiddenOptions(HardpointSet[] hardpointSets)
        {
            if (hardpointSets == null)
            {
                return 0;
            }

            int removed = 0;
            foreach (HardpointSet hardpointSet in hardpointSets)
            {
                if (hardpointSet?.weaponOptions == null)
                {
                    continue;
                }

                removed += hardpointSet.weaponOptions.RemoveAll(IsForbiddenMount);
            }

            return removed;
        }

        private static int SanitizeParameters(AircraftParameters parameters)
        {
            if (parameters == null)
            {
                return 0;
            }

            int cleared = 0;
            if (parameters.loadouts != null)
            {
                foreach (Loadout loadout in parameters.loadouts)
                {
                    cleared += SanitizeLoadout(loadout);
                }
            }

            if (parameters.StandardLoadouts != null)
            {
                foreach (StandardLoadout standardLoadout in parameters.StandardLoadouts)
                {
                    if (standardLoadout != null)
                    {
                        cleared += SanitizeLoadout(standardLoadout.loadout);
                    }
                }
            }

            return cleared;
        }

        private static int MigrateParameters(
            AircraftParameters parameters,
            int wingPylonIndex,
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
                    migrated += MigrateLoadout(loadout, wingPylonIndex, hardpointSetCount);
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
                            wingPylonIndex,
                            hardpointSetCount);
                    }
                }
            }

            return migrated;
        }

        private static int MigrateLoadout(
            Loadout loadout,
            int wingPylonIndex,
            int hardpointSetCount)
        {
            if (loadout?.weapons == null ||
                wingPylonIndex < 0 ||
                wingPylonIndex >= loadout.weapons.Count ||
                hardpointSetCount <= 0 ||
                loadout.weapons.Count >= hardpointSetCount)
            {
                return 0;
            }

            WeaponMount formerPairedSelection = loadout.weapons[wingPylonIndex];
            loadout.weapons.Insert(wingPylonIndex + 1, formerPairedSelection);
            while (loadout.weapons.Count < hardpointSetCount)
            {
                loadout.weapons.Add(null);
            }

            return 1;
        }

        private static int SanitizeLoadout(Loadout loadout)
        {
            if (loadout?.weapons == null)
            {
                return 0;
            }

            int cleared = 0;
            for (int i = 0; i < loadout.weapons.Count; i++)
            {
                if (!IsForbiddenMount(loadout.weapons[i]))
                {
                    continue;
                }

                loadout.weapons[i] = null;
                cleared++;
            }

            return cleared;
        }

        private static bool IsForbiddenMount(WeaponMount mount)
        {
            if (mount == null || IsS2Mount(mount))
            {
                return false;
            }

            if (ForbiddenMountKeys.Contains(mount.jsonKey))
            {
                return true;
            }

            WeaponInfo info = mount.info;
            return info != null && info.missile && info.effectiveness.antiAir > 0f;
        }

        private static bool IsS2Mount(WeaponMount mount)
        {
            if (mount == null)
            {
                return false;
            }
            if (string.Equals(mount.jsonKey, S2MountKey, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string shortName = mount.info?.shortName;
            string weaponName = mount.info?.weaponName;
            return string.Equals(shortName, "IRM-S2", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(weaponName, "IRM-S2", StringComparison.OrdinalIgnoreCase) ||
                   (weaponName != null && weaponName.StartsWith(
                       "IRM-S2 ",
                       StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsAb4(AircraftDefinition definition)
        {
            return definition != null &&
                   string.Equals(definition.jsonKey, Ab4DefinitionKey, StringComparison.OrdinalIgnoreCase);
        }

        internal static string ReplacePlayerFacingName(string value)
        {
            if (string.IsNullOrEmpty(value) ||
                value.IndexOf(Ab4VanillaDisplayName, StringComparison.Ordinal) < 0)
            {
                return value;
            }
            return value.Replace(Ab4VanillaDisplayName, Ab4DisplayName);
        }
    }

    [HarmonyPatch]
    internal static class EncyclopediaAfterLoadPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Encyclopedia), "AfterLoad", Type.EmptyTypes);
        }

        private static void Postfix(Encyclopedia __instance)
        {
            Ab4Rebalanacing.ApplyToEncyclopedia(__instance);
        }
    }

    [HarmonyPatch(typeof(WeaponManager), "Awake")]
    internal static class Ab4WeaponManagerAwakePatch
    {
        private static void Prefix(WeaponManager __instance)
        {
            Ab4Rebalanacing.ApplyToWeaponManager(__instance);
        }
    }

    [HarmonyPatch(typeof(WeaponManager), nameof(WeaponManager.SpawnWeapons))]
    internal static class Ab4SpawnWeaponsPatch
    {
        private static void Prefix(WeaponManager __instance)
        {
            Ab4Rebalanacing.ApplyToWeaponManager(__instance);
        }
    }

    [HarmonyPatch(typeof(WeaponManager), nameof(WeaponManager.RegisterWeapon))]
    internal static class Ab4JammerRegistrationPatch
    {
        private static void Postfix(
            WeaponManager __instance,
            Weapon weapon,
            WeaponMount weaponMount,
            Hardpoint hardpoint)
        {
            Ab4Rebalanacing.AddSecondJammerChannel(__instance, weapon, weaponMount, hardpoint);
        }
    }

    internal sealed class Ab4DualChannelJammerMarker : MonoBehaviour
    {
    }

    [HarmonyPatch]
    internal static class Ab4TmpDisplayNamePatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.PropertySetter(typeof(TMPro.TMP_Text), nameof(TMPro.TMP_Text.text));
        }

        private static void Prefix(ref string __0)
        {
            __0 = Ab4Rebalanacing.ReplacePlayerFacingName(__0);
        }
    }

    [HarmonyPatch]
    internal static class Ab4LegacyDisplayNamePatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.PropertySetter(
                typeof(UnityEngine.UI.Text),
                nameof(UnityEngine.UI.Text.text));
        }

        private static void Prefix(ref string __0)
        {
            __0 = Ab4Rebalanacing.ReplacePlayerFacingName(__0);
        }
    }
}

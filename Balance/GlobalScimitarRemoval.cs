using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using NuclearOption.SavedMission;

namespace TheGreatBigRebalancing.Balance
{
    internal static class GlobalScimitarRemoval
    {
        private static readonly HashSet<string> ScimitarMountKeys =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "AAM4x8",
                "AAM4_single",
                "AAM4_double",
                "AAM4_single_internal",
                "AAM4_double_internal",
                "AAM4_double_internal_compact",
                "AAM4_triple_internal"
            };

        private static readonly FieldInfo DisabledField =
            AccessTools.Field(typeof(WeaponMount), "disabled");

        private static readonly FieldInfo UnitDefinitionDisabledField =
            AccessTools.Field(typeof(UnitDefinition), "disabled");

        private static bool loggedEncyclopediaApply;
        private static bool loggedRuntimeFallback;

        internal static void ApplyToEncyclopedia(Encyclopedia encyclopedia)
        {
            if (encyclopedia == null)
            {
                return;
            }

            int disabledMounts = 0;
            int disabledMissileDefinitions = 0;
            int removedOptions = 0;
            int clearedSelections = 0;
            HashSet<string> foundKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (encyclopedia.weaponMounts != null)
            {
                foreach (WeaponMount mount in encyclopedia.weaponMounts)
                {
                    if (!IsScimitar(mount))
                    {
                        continue;
                    }

                    if (!string.IsNullOrEmpty(mount.jsonKey))
                    {
                        foundKeys.Add(mount.jsonKey);
                    }
                    if (DisableMount(mount))
                    {
                        disabledMounts++;
                    }
                }
            }

            bool foundMissileDefinition = false;
            if (encyclopedia.missiles != null)
            {
                foreach (MissileDefinition definition in encyclopedia.missiles)
                {
                    if (definition == null ||
                        (!string.Equals(
                             definition.jsonKey,
                             "AAM4",
                             StringComparison.OrdinalIgnoreCase) &&
                         !string.Equals(
                             definition.unitName,
                             "AAM-36 Scimitar",
                             StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    foundMissileDefinition = true;
                    if (DisableMissileDefinition(definition))
                    {
                        disabledMissileDefinitions++;
                    }
                }
            }

            if (encyclopedia.aircraft != null)
            {
                foreach (AircraftDefinition definition in encyclopedia.aircraft)
                {
                    WeaponManager manager = GetWeaponManager(definition);
                    if (manager != null)
                    {
                        removedOptions += RemoveOptions(manager.hardpointSets);
                    }
                    clearedSelections += SanitizeParameters(definition?.aircraftParameters);
                }
            }

            foreach (string expectedKey in ScimitarMountKeys)
            {
                if (!foundKeys.Contains(expectedKey))
                {
                    Plugin.ModLogger?.LogError(
                        "Global AAM-36 removal could not find registered mount '" +
                        expectedKey + "'.");
                }
            }
            if (!foundMissileDefinition)
            {
                Plugin.ModLogger?.LogError(
                    "Global AAM-36 removal could not find the AAM4 missile definition.");
            }

            if (!loggedEncyclopediaApply)
            {
                loggedEncyclopediaApply = true;
                Plugin.ModLogger?.LogInfo(
                    "AAM-36 Scimitar removed globally: disabled " + disabledMounts +
                    " registered mount variant(s) and " + disabledMissileDefinitions +
                    " missile definition(s), removed " + removedOptions +
                    " aircraft hardpoint option(s), and cleared " + clearedSelections +
                    " default or AI loadout selection(s). Registry entries were retained for " +
                    "stable network definition indexes.");
            }
        }

        internal static void ApplyToWeaponManager(WeaponManager weaponManager)
        {
            if (weaponManager == null)
            {
                return;
            }

            int removedOptions = RemoveOptions(weaponManager.hardpointSets);
            Aircraft aircraft = weaponManager.GetComponentInParent<Aircraft>();
            int clearedSelections = SanitizeLoadout(aircraft?.loadout);
            if (!loggedRuntimeFallback && (removedOptions > 0 || clearedSelections > 0))
            {
                loggedRuntimeFallback = true;
                Plugin.ModLogger?.LogInfo(
                    "Removed an AAM-36 Scimitar option or stale selection from a runtime aircraft.");
            }
        }

        internal static bool IsScimitar(WeaponMount mount)
        {
            if (mount == null)
            {
                return false;
            }

            if (ScimitarMountKeys.Contains(mount.jsonKey ?? string.Empty))
            {
                return true;
            }

            string shortName = mount.info?.shortName;
            string weaponName = mount.info?.weaponName;
            return string.Equals(shortName, "AAM-36", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(weaponName, "AAM-36 Scimitar", StringComparison.OrdinalIgnoreCase) ||
                   (weaponName != null && weaponName.StartsWith(
                       "AAM-36 Scimitar ",
                       StringComparison.OrdinalIgnoreCase));
        }

        private static WeaponManager GetWeaponManager(AircraftDefinition definition)
        {
            if (definition?.unitPrefab == null)
            {
                return null;
            }

            Aircraft aircraft = definition.unitPrefab.GetComponent<Aircraft>();
            return aircraft != null
                ? aircraft.weaponManager
                : definition.unitPrefab.GetComponentInChildren<WeaponManager>(true);
        }

        private static bool DisableMount(WeaponMount mount)
        {
            if (DisabledField == null)
            {
                Plugin.ModLogger?.LogError(
                    "Global AAM-36 removal could not access WeaponMount.disabled.");
                return false;
            }

            bool wasDisabled = (bool)DisabledField.GetValue(mount);
            DisabledField.SetValue(mount, true);
            return !wasDisabled;
        }

        private static bool DisableMissileDefinition(MissileDefinition definition)
        {
            if (UnitDefinitionDisabledField == null)
            {
                Plugin.ModLogger?.LogError(
                    "Global AAM-36 removal could not access UnitDefinition.disabled.");
                return false;
            }

            bool wasDisabled = (bool)UnitDefinitionDisabledField.GetValue(definition);
            UnitDefinitionDisabledField.SetValue(definition, true);
            return !wasDisabled;
        }

        private static int RemoveOptions(HardpointSet[] hardpointSets)
        {
            if (hardpointSets == null)
            {
                return 0;
            }

            int removed = 0;
            foreach (HardpointSet set in hardpointSets)
            {
                if (set?.weaponOptions != null)
                {
                    removed += set.weaponOptions.RemoveAll(IsScimitar);
                }
                if (IsScimitar(set?.weaponMount))
                {
                    set.weaponMount = null;
                }
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
                foreach (StandardLoadout standard in parameters.StandardLoadouts)
                {
                    cleared += SanitizeLoadout(standard?.loadout);
                }
            }
            return cleared;
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
                if (!IsScimitar(loadout.weapons[i]))
                {
                    continue;
                }

                loadout.weapons[i] = null;
                cleared++;
            }
            return cleared;
        }
    }

    [HarmonyPatch]
    internal static class GlobalScimitarEncyclopediaPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Encyclopedia), "AfterLoad", Type.EmptyTypes);
        }

        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Encyclopedia __instance)
        {
            GlobalScimitarRemoval.ApplyToEncyclopedia(__instance);
        }
    }

    [HarmonyPatch(typeof(WeaponManager), "Awake")]
    internal static class GlobalScimitarWeaponManagerAwakePatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Prefix(WeaponManager __instance)
        {
            GlobalScimitarRemoval.ApplyToWeaponManager(__instance);
        }
    }

    [HarmonyPatch(typeof(WeaponManager), nameof(WeaponManager.SpawnWeapons))]
    internal static class GlobalScimitarSpawnWeaponsPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Prefix(WeaponManager __instance)
        {
            GlobalScimitarRemoval.ApplyToWeaponManager(__instance);
        }
    }

    [HarmonyPatch(typeof(WeaponChecker), nameof(WeaponChecker.MountAllowedHQ))]
    internal static class GlobalScimitarAvailabilityPatch
    {
        private static void Postfix(WeaponMount mount, ref bool __result)
        {
            if (GlobalScimitarRemoval.IsScimitar(mount))
            {
                __result = false;
            }
        }
    }

    [HarmonyPatch(typeof(HardpointSet), nameof(HardpointSet.SpawnMounts))]
    internal static class GlobalScimitarSpawnGuardPatch
    {
        private static void Prefix(ref WeaponMount weaponMount)
        {
            if (GlobalScimitarRemoval.IsScimitar(weaponMount))
            {
                weaponMount = null;
            }
        }
    }
}

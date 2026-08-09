using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using NuclearOption.SavedMission;
using UnityEngine;

namespace TheGreatBigRebalancing.Balance
{
    internal static class TernionLoadoutRebalance
    {
        private sealed class HardpointSpec
        {
            internal HardpointSpec(Vector3 position, string partName)
            {
                Position = position;
                PartName = partName;
            }

            internal Vector3 Position { get; }
            internal string PartName { get; }
        }

        private sealed class PylonSpec
        {
            internal PylonSpec(
                string name,
                string[] weaponKeys,
                params HardpointSpec[] hardpoints)
            {
                Name = name;
                WeaponKeys = weaponKeys;
                Hardpoints = hardpoints;
            }

            internal string Name { get; }
            internal string[] WeaponKeys { get; }
            internal HardpointSpec[] Hardpoints { get; }
        }

        private sealed class ResolvedPylon
        {
            internal HardpointSet Set;
            internal List<WeaponMount> WeaponOptions;
            internal UnitPart[] Parts;
        }

        private const string DefinitionKey = "P_Trisurface1";
        private const string DisplayName = "FS-3 Ternion";

        private static readonly PylonSpec[] Configuration =
        {
            new PylonSpec(
                "Internal Cannon",
                new[] { "P_Trisurface1_Gun20mm" },
                new HardpointSpec(new Vector3(0f, 0.660314f, 0.604075f), "fuselage_F")),
            new PylonSpec(
                "Combined Weapon Bay",
                new[]
                {
                    "P_BallisticMissile1_single",
                    "P_BallisticMissile1_tacNuke_single",
                    "bomb_penetrator1_internalx2",
                    "Trisurface1_laser_pod_ventral",
                    "P_ARM1_internalx3",
                    "P_bomb_500_glide_internalx3"
                },
                new HardpointSpec(new Vector3(0f, -0.565f, 3.4f), "fuselage_F")),
            new PylonSpec(
                "Forward Weapon Bay",
                new[]
                {
                    "AAM1_internalx3",
                    "AAM2_triple_internal",
                    "AAM3_quad_internal",
                    "P_AGM_heavy_internalx3",
                    "bomb_glide1_six_inernal",
                    "bomb_250_glide_internalx3",
                    "bomb_125_internalx6",
                    "bomb_250_internalx3",
                    "bomb_500_internalx2",
                    "nuclearBomb1_internal",
                    "nuclearBomb1_internalx2",
                    "nuclearBomb1_strategic_internal",
                    "nuclearBomb1_strategic_internalx2",
                    "P_AShM2_internalx2",
                    "AShM3_internalx2"
                },
                new HardpointSpec(new Vector3(0f, -0.32f, -0.73f), "fuselage_F")),
            new PylonSpec(
                "Rear Weapon Bay",
                new[]
                {
                    "AAM1_internalx3",
                    "AAM2_triple_internal",
                    "AAM3_quad_internal",
                    "P_AGM_heavy_internalx3",
                    "bomb_glide1_six_inernal",
                    "bomb_250_glide_internalx3",
                    "bomb_125_internalx6",
                    "bomb_250_internalx3",
                    "bomb_500_internalx2",
                    "nuclearBomb1_internal",
                    "nuclearBomb1_internalx2",
                    "nuclearBomb1_strategic_internal",
                    "nuclearBomb1_strategic_internalx2",
                    "P_AShM2_internalx2",
                    "AShM3_internalx2"
                },
                new HardpointSpec(new Vector3(0f, -0.4f, 1.244f), "P_Trisurface1")),
            new PylonSpec(
                "Front Fuselage Pylons",
                new[]
                {
                    "AAM2_single",
                    "AGM_heavy_single",
                    "AShM2_single",
                    "bomb_500_single",
                    "bomb_500_glide_single",
                    "P_bomb_250_triple",
                    "bomb_125_quad",
                    "bomb_250_glide_double",
                    "aryx_lf1_bomb_cluster1_single",
                    "bomb_glide1_quad"
                },
                new HardpointSpec(new Vector3(0.000021f, 0.000282f, -0.000384f), "intake_L"),
                new HardpointSpec(new Vector3(-0.000021f, 0.000282f, -0.000384f), "intake_R")),
            new PylonSpec(
                "Rear Fuselage Pylons",
                new[]
                {
                    "AAM2_single",
                    "AGM_heavy_single",
                    "AShM2_single",
                    "bomb_500_single",
                    "bomb_500_glide_single",
                    "P_bomb_250_triple",
                    "bomb_125_quad",
                    "bomb_250_glide_double",
                    "aryx_lf1_bomb_cluster1_single",
                    "bomb_glide1_quad"
                },
                new HardpointSpec(new Vector3(-0.029f, -0.054f, 0.216f), "engine_L_aero"),
                new HardpointSpec(new Vector3(0.029f, -0.054f, 0.216f), "engine_R_aero")),
            new PylonSpec(
                "Inner Wing Pylons",
                new[]
                {
                    "AAM1_single",
                    "AAM1_double",
                    "AAM2_single",
                    "AAM2_double",
                    "ARM1_single",
                    "AGM_heavyx2",
                    "AShM2_single",
                    "AShM2_double",
                    "AShM3_single",
                    "Rocket2_4Pod",
                    "P_bomb_glide1_triple",
                    "bomb_250_glide_double",
                    "bomb_500_glide_single",
                    "bomb_125_quad",
                    "P_bomb_250_triple",
                    "bomb_500_single",
                    "bomb_500_double",
                    "bomb_penetrator1_mount",
                    "P_BallisticMissile1_single2",
                    "P_BallisticMissile1_tacNuke_single2",
                    "AIR-2_Genie_single",
                    "Rocket2_4Podx3"
                },
                new HardpointSpec(new Vector3(0f, -0.14f, 0.881f), "wing_2a_L"),
                new HardpointSpec(new Vector3(0f, -0.14f, 0.901f), "wing_2a_R")),
            new PylonSpec(
                "Middle Wing Pylons",
                new[]
                {
                    "AAM1_single",
                    "AAM1_double",
                    "AAM2_single",
                    "AAM2_double",
                    "ARM1_single",
                    "AGM_heavyx2",
                    "AShM2_single",
                    "AShM3_single",
                    "Rocket2_4Pod",
                    "P_bomb_glide1_triple",
                    "bomb_250_glide_double",
                    "bomb_500_glide_single",
                    "bomb_125_quad",
                    "P_bomb_250_triple",
                    "bomb_500_single",
                    "bomb_500_double",
                    "bomb_penetrator1_mount",
                    "AIR-2_Genie_single"
                },
                new HardpointSpec(new Vector3(0f, -0.11f, 0.592f), "wing_2a_L"),
                new HardpointSpec(new Vector3(0f, -0.11f, 0.612f), "wing_2a_R")),
            new PylonSpec(
                "Outer Wing Pylons",
                new[]
                {
                    "AAM1_single",
                    "AAM2_single",
                    "AGM_heavy_single",
                    "Rocket2_4Pod",
                    "bomb_glide1_double",
                    "bomb_250_glide_single",
                    "bomb_125_double",
                    "bomb_250_single",
                    "bomb_500_single",
                    "AIR-2_Genie_single"
                },
                new HardpointSpec(new Vector3(0.000001f, -0.08f, 0.373f), "wing_3a_L"),
                new HardpointSpec(new Vector3(0f, -0.08f, 0.333f), "wing_3a_R"))
        };

        private static Encyclopedia pendingEncyclopedia;
        private static float nextAttempt;
        private static float deadline;
        private static bool loggedSuccess;
        private static string lastRuntimeFailure;

        internal static void RegisterEncyclopedia(Encyclopedia encyclopedia)
        {
            pendingEncyclopedia = encyclopedia;
            nextAttempt = 0f;
            deadline = Time.unscaledTime + 60f;
            TryApplyPending(force: true);
        }

        internal static void TryApplyPending(bool force = false)
        {
            if (pendingEncyclopedia == null || (!force && Time.unscaledTime < nextAttempt))
            {
                return;
            }

            if (TryApplyToEncyclopedia(pendingEncyclopedia))
            {
                pendingEncyclopedia = null;
                return;
            }

            if (Time.unscaledTime >= deadline)
            {
                pendingEncyclopedia = null;
                Plugin.ModLogger?.LogWarning(
                    "FS-3 Ternion loadout policy was not applied because P_Trisurface1 or its " +
                    "required Blueprinter assets were unavailable.");
                return;
            }

            nextAttempt = Time.unscaledTime + 0.5f;
        }

        internal static void ApplyToWeaponManager(WeaponManager weaponManager)
        {
            Aircraft aircraft = weaponManager?.GetComponentInParent<Aircraft>();
            if (aircraft == null || !IsTernion(aircraft.definition))
            {
                return;
            }

            if (!TryApplyConfiguration(
                    aircraft,
                    weaponManager,
                    Encyclopedia.i,
                    out ResolvedPylon[] resolved,
                    out string failure))
            {
                LogRuntimeFailure(failure);
                return;
            }

            ApplyResolvedConfiguration(resolved);
            SanitizeLoadout(aircraft.loadout, weaponManager);
        }

        private static bool TryApplyToEncyclopedia(Encyclopedia encyclopedia)
        {
            AircraftDefinition definition = encyclopedia?.aircraft?.Find(IsTernion);
            if (definition?.unitPrefab == null)
            {
                return false;
            }

            Aircraft aircraft = definition.unitPrefab.GetComponent<Aircraft>() ??
                                definition.unitPrefab.GetComponentInChildren<Aircraft>(true);
            WeaponManager weaponManager = aircraft?.weaponManager ??
                                          definition.unitPrefab.GetComponentInChildren<WeaponManager>(true);
            if (aircraft == null || weaponManager == null)
            {
                return false;
            }

            if (!TryApplyConfiguration(
                    aircraft,
                    weaponManager,
                    encyclopedia,
                    out ResolvedPylon[] resolved,
                    out string failure))
            {
                Plugin.ModLogger?.LogError("FS-3 Ternion configuration failed: " + failure);
                return false;
            }

            ApplyResolvedConfiguration(resolved);
            int sanitized = SanitizeParameters(definition.aircraftParameters, weaponManager);
            if (!ValidateConfiguration(weaponManager, resolved, out failure))
            {
                Plugin.ModLogger?.LogError("FS-3 Ternion validation failed: " + failure);
                return false;
            }

            if (!loggedSuccess)
            {
                loggedSuccess = true;
                Plugin.ModLogger?.LogInfo(
                    "FS-3 Ternion loadout policy active: all " + Configuration.Length +
                    " pylon sets match the exported weapon allow-lists, hardpoint geometry, and " +
                    "attached parts; " + sanitized + " stale default/AI selection(s) were cleared.");
            }
            return true;
        }

        private static bool TryApplyConfiguration(
            Aircraft aircraft,
            WeaponManager weaponManager,
            Encyclopedia encyclopedia,
            out ResolvedPylon[] resolved,
            out string failure)
        {
            resolved = null;
            failure = null;
            if (weaponManager?.hardpointSets == null)
            {
                failure = "the aircraft WeaponManager has no hardpoint sets";
                return false;
            }

            Dictionary<string, WeaponMount> mounts = BuildMountLookup(weaponManager, encyclopedia);
            ResolvedPylon[] candidates = new ResolvedPylon[Configuration.Length];
            for (int specIndex = 0; specIndex < Configuration.Length; specIndex++)
            {
                PylonSpec spec = Configuration[specIndex];
                HardpointSet set = FindUniqueSet(weaponManager.hardpointSets, spec.Name, out bool duplicate);
                if (duplicate)
                {
                    failure = "multiple pylon sets were named '" + spec.Name + "'";
                    return false;
                }
                if (set == null)
                {
                    failure = "pylon set '" + spec.Name + "' was not found";
                    return false;
                }
                if (set.hardpoints == null || set.hardpoints.Count != spec.Hardpoints.Length)
                {
                    failure = "pylon set '" + spec.Name + "' has " +
                              (set.hardpoints?.Count ?? 0) + " hardpoint(s), expected " +
                              spec.Hardpoints.Length;
                    return false;
                }

                List<WeaponMount> options = new List<WeaponMount>(spec.WeaponKeys.Length);
                foreach (string key in spec.WeaponKeys)
                {
                    if (!mounts.TryGetValue(key, out WeaponMount mount) || mount == null)
                    {
                        failure = "weapon mount '" + key + "' required by '" + spec.Name +
                                  "' was not registered";
                        return false;
                    }
                    options.Add(mount);
                }

                UnitPart[] parts = new UnitPart[spec.Hardpoints.Length];
                for (int hardpointIndex = 0; hardpointIndex < spec.Hardpoints.Length; hardpointIndex++)
                {
                    Hardpoint hardpoint = set.hardpoints[hardpointIndex];
                    if (hardpoint?.transform == null)
                    {
                        failure = "hardpoint " + hardpointIndex + " in '" + spec.Name +
                                  "' has no transform";
                        return false;
                    }
                    string partName = spec.Hardpoints[hardpointIndex].PartName;
                    UnitPart part = FindPart(aircraft, weaponManager, partName, hardpoint.part);
                    if (part == null)
                    {
                        failure = "attached part '" + partName + "' for hardpoint " +
                                  hardpointIndex + " in '" + spec.Name + "' was not found";
                        return false;
                    }
                    parts[hardpointIndex] = part;
                }

                candidates[specIndex] = new ResolvedPylon
                {
                    Set = set,
                    WeaponOptions = options,
                    Parts = parts
                };
            }

            resolved = candidates;
            return true;
        }

        private static Dictionary<string, WeaponMount> BuildMountLookup(
            WeaponManager weaponManager,
            Encyclopedia encyclopedia)
        {
            Dictionary<string, WeaponMount> mounts =
                new Dictionary<string, WeaponMount>(StringComparer.OrdinalIgnoreCase);
            AddMounts(mounts, encyclopedia?.weaponMounts);
            foreach (HardpointSet set in weaponManager.hardpointSets)
            {
                AddMounts(mounts, set?.weaponOptions);
                AddMount(mounts, set?.weaponMount);
            }
            return mounts;
        }

        private static void AddMounts(
            IDictionary<string, WeaponMount> destination,
            IEnumerable<WeaponMount> mounts)
        {
            if (mounts == null)
            {
                return;
            }
            foreach (WeaponMount mount in mounts)
            {
                AddMount(destination, mount);
            }
        }

        private static void AddMount(
            IDictionary<string, WeaponMount> destination,
            WeaponMount mount)
        {
            if (mount == null || string.IsNullOrEmpty(mount.jsonKey) ||
                destination.ContainsKey(mount.jsonKey))
            {
                return;
            }
            destination.Add(mount.jsonKey, mount);
        }

        private static HardpointSet FindUniqueSet(
            IEnumerable<HardpointSet> sets,
            string name,
            out bool duplicate)
        {
            duplicate = false;
            HardpointSet match = null;
            foreach (HardpointSet set in sets)
            {
                if (set == null || !string.Equals(set.name, name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (match != null)
                {
                    duplicate = true;
                    return null;
                }
                match = set;
            }
            return match;
        }

        private static UnitPart FindPart(
            Aircraft aircraft,
            WeaponManager weaponManager,
            string expectedName,
            UnitPart current)
        {
            if (PartMatches(current, expectedName))
            {
                return current;
            }

            foreach (HardpointSet set in weaponManager.hardpointSets)
            {
                if (set?.hardpoints == null)
                {
                    continue;
                }
                foreach (Hardpoint hardpoint in set.hardpoints)
                {
                    if (PartMatches(hardpoint?.part, expectedName))
                    {
                        return hardpoint.part;
                    }
                }
            }

            UnitPart[] parts = aircraft.GetComponentsInChildren<UnitPart>(true);
            foreach (UnitPart part in parts)
            {
                if (PartMatches(part, expectedName))
                {
                    return part;
                }
            }
            return null;
        }

        private static bool PartMatches(UnitPart part, string expectedName)
        {
            if (part?.transform == null)
            {
                return false;
            }
            return string.Equals(
                       NormalizeCloneName(part.transform.name),
                       expectedName,
                       StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(
                       NormalizeCloneName(GetTransformPath(part.transform)),
                       expectedName,
                       StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeCloneName(string value)
        {
            const string suffix = "(Clone)";
            return value != null && value.EndsWith(suffix, StringComparison.Ordinal)
                ? value.Substring(0, value.Length - suffix.Length)
                : value;
        }

        private static string GetTransformPath(Transform transform)
        {
            Stack<string> names = new Stack<string>();
            Transform current = transform;
            while (current != null)
            {
                names.Push(current.name);
                current = current.parent;
            }
            return string.Join("/", names.ToArray());
        }

        private static void ApplyResolvedConfiguration(ResolvedPylon[] resolved)
        {
            for (int specIndex = 0; specIndex < Configuration.Length; specIndex++)
            {
                PylonSpec spec = Configuration[specIndex];
                ResolvedPylon target = resolved[specIndex];
                target.Set.weaponOptions = new List<WeaponMount>(target.WeaponOptions);
                target.Set.weaponMount = CanonicalMount(target.Set.weaponMount, target.WeaponOptions);
                for (int hardpointIndex = 0; hardpointIndex < spec.Hardpoints.Length; hardpointIndex++)
                {
                    Hardpoint hardpoint = target.Set.hardpoints[hardpointIndex];
                    hardpoint.transform.localPosition = spec.Hardpoints[hardpointIndex].Position;
                    hardpoint.part = target.Parts[hardpointIndex];
                }
            }
        }

        private static WeaponMount CanonicalMount(
            WeaponMount selected,
            IEnumerable<WeaponMount> options)
        {
            if (selected == null)
            {
                return null;
            }
            foreach (WeaponMount option in options)
            {
                if (option != null && string.Equals(
                        option.jsonKey,
                        selected.jsonKey,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return option;
                }
            }
            return null;
        }

        private static int SanitizeParameters(
            AircraftParameters parameters,
            WeaponManager weaponManager)
        {
            if (parameters == null)
            {
                return 0;
            }
            int sanitized = 0;
            if (parameters.loadouts != null)
            {
                foreach (Loadout loadout in parameters.loadouts)
                {
                    sanitized += SanitizeLoadout(loadout, weaponManager);
                }
            }
            if (parameters.StandardLoadouts != null)
            {
                foreach (StandardLoadout standard in parameters.StandardLoadouts)
                {
                    if (standard != null)
                    {
                        sanitized += SanitizeLoadout(standard.loadout, weaponManager);
                    }
                }
            }
            return sanitized;
        }

        private static int SanitizeLoadout(Loadout loadout, WeaponManager weaponManager)
        {
            if (loadout?.weapons == null || weaponManager?.hardpointSets == null)
            {
                return 0;
            }
            int sanitized = 0;
            int count = Math.Min(loadout.weapons.Count, weaponManager.hardpointSets.Length);
            for (int index = 0; index < count; index++)
            {
                WeaponMount selected = loadout.weapons[index];
                if (selected == null)
                {
                    continue;
                }
                WeaponMount canonical = CanonicalMount(
                    selected,
                    weaponManager.hardpointSets[index]?.weaponOptions);
                if (canonical == null)
                {
                    loadout.weapons[index] = null;
                    sanitized++;
                }
                else if (canonical != selected)
                {
                    loadout.weapons[index] = canonical;
                }
            }
            return sanitized;
        }

        private static bool ValidateConfiguration(
            WeaponManager weaponManager,
            ResolvedPylon[] resolved,
            out string failure)
        {
            for (int specIndex = 0; specIndex < Configuration.Length; specIndex++)
            {
                PylonSpec spec = Configuration[specIndex];
                ResolvedPylon target = resolved[specIndex];
                HardpointSet set = target.Set;
                if (set.weaponOptions == null || set.weaponOptions.Count != spec.WeaponKeys.Length)
                {
                    failure = "'" + spec.Name + "' has the wrong weapon-option count";
                    return false;
                }
                for (int optionIndex = 0; optionIndex < spec.WeaponKeys.Length; optionIndex++)
                {
                    if (!string.Equals(
                            set.weaponOptions[optionIndex]?.jsonKey,
                            spec.WeaponKeys[optionIndex],
                            StringComparison.OrdinalIgnoreCase))
                    {
                        failure = "'" + spec.Name + "' weapon option " + optionIndex +
                                  " does not match " + spec.WeaponKeys[optionIndex];
                        return false;
                    }
                }
                for (int hardpointIndex = 0; hardpointIndex < spec.Hardpoints.Length; hardpointIndex++)
                {
                    Hardpoint hardpoint = set.hardpoints[hardpointIndex];
                    if (Vector3.Distance(
                            hardpoint.transform.localPosition,
                            spec.Hardpoints[hardpointIndex].Position) > 0.00001f)
                    {
                        failure = "'" + spec.Name + "' hardpoint " + hardpointIndex +
                                  " has the wrong position";
                        return false;
                    }
                    if (hardpoint.part != target.Parts[hardpointIndex])
                    {
                        failure = "'" + spec.Name + "' hardpoint " + hardpointIndex +
                                  " has the wrong attached part";
                        return false;
                    }
                }
            }
            failure = null;
            return true;
        }

        private static bool IsTernion(AircraftDefinition definition)
        {
            return definition != null && string.Equals(
                definition.jsonKey,
                DefinitionKey,
                StringComparison.OrdinalIgnoreCase);
        }

        private static void LogRuntimeFailure(string failure)
        {
            if (string.Equals(lastRuntimeFailure, failure, StringComparison.Ordinal))
            {
                return;
            }
            lastRuntimeFailure = failure;
            Plugin.ModLogger?.LogError(DisplayName + " runtime configuration failed: " + failure);
        }
    }

    [HarmonyPatch]
    internal static class TernionEncyclopediaAfterLoadPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Encyclopedia), "AfterLoad", Type.EmptyTypes);
        }

        private static void Postfix(Encyclopedia __instance)
        {
            TernionLoadoutRebalance.RegisterEncyclopedia(__instance);
        }
    }

    [HarmonyPatch(typeof(WeaponManager), "Awake")]
    internal static class TernionWeaponManagerAwakePatch
    {
        private static void Prefix(WeaponManager __instance)
        {
            TernionLoadoutRebalance.ApplyToWeaponManager(__instance);
        }
    }

    [HarmonyPatch(typeof(WeaponManager), nameof(WeaponManager.SpawnWeapons))]
    internal static class TernionSpawnWeaponsPatch
    {
        private static void Prefix(WeaponManager __instance)
        {
            TernionLoadoutRebalance.ApplyToWeaponManager(__instance);
        }
    }
}

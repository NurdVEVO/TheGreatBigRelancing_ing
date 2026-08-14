using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using NuclearOption.SavedMission;
using UnityEngine;

namespace TheGreatBigRebalancing.Balance
{
    internal static class Fs41TargetingPodPylon
    {
        private const string Fs41DefinitionKey = "Aryx_Interceptor1";
        private const string TargetingPodPylonName = "TGP";
        private const string EtsMountKey = "Aryx_ExternalTargetingPod";
        private const string WingGloveOutboardPylonName = "Wing Glove Outboard";
        private const string LegacyWingGloveOutboardPylonName = "New HardpointSet";
        private const string WingGlovePylonName = "Wing Glove Pylons";
        private const string WingPylonName = "Wing Pylons";
        private const string DropTankPylonName = "Drop Tanks";
        private const string RemovedWingGloveMountKey =
            "Aryx_Interceptor1_AAM2_Triple_Compact";
        private const string RemovedWingGloveArmMountKey = "ARM1_single";
        private const string WingGloveOutboardMountKey = "AAM1_single";
        private const string DropTank450MountKey = "Aryx_DropTank450";
        private const string DropTank1200MountKey = "Aryx_DropTank1200";
        private static readonly string[] WingPylonMountKeys =
        {
            "AAM2_single",
            "AAM1_single"
        };
        private static readonly Vector3 TargetingPodPosition = new Vector3(0.6f, -0.67f, 5.2f);
        private static readonly Vector3 TargetingPodRotation = new Vector3(0f, -0.000002f, 30f);

        private static readonly Vector3[] WingGlovePositions =
        {
            new Vector3(-0.493165f, -0.625216f, -0.327983f),
            new Vector3(0.493213f, -0.625001f, -0.328003f)
        };

        private static readonly Vector3[] WingGloveRotations =
        {
            new Vector3(-0.000234f, -0.000326f, -0.000109f),
            new Vector3(-0.000113f, 0.00012f, 0.004813f)
        };

        private static readonly Vector3[] WingPylonPositions =
        {
            new Vector3(0f, 0.170003f, -0.254f),
            new Vector3(0.00038f, -0.169808f, 0.254742f)
        };

        private static readonly Vector3[] WingPylonRotations =
        {
            new Vector3(0f, 180f, -180f),
            new Vector3(-0.005859f, 0.316312f, 0.509317f)
        };

        private static readonly Vector3[] WingPylonScales =
        {
            new Vector3(1f, 0.999999f, 0.999999f),
            Vector3.one
        };

        private static readonly Vector3[] WingGloveOutboardPositions =
        {
            new Vector3(2.900001f, -0.5f, 0.999999f),
            new Vector3(-2.9f, -0.500001f, 0.999999f)
        };

        private static readonly Vector3[] WingGloveOutboardRotations =
        {
            new Vector3(-0.000003f, 0.000002f, 90f),
            new Vector3(-0.000004f, 0.000001f, -90f)
        };

        private static readonly Vector3[] DropTankPositions =
        {
            new Vector3(1.299999f, -1.03f, -0.2f),
            new Vector3(-1.3f, -1.000001f, 0f)
        };

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

        private static readonly Dictionary<HardpointSet, WeaponManager> WingGloveSetOwners =
            new Dictionary<HardpointSet, WeaponManager>();

        private static Encyclopedia pendingEncyclopedia;
        private static float nextPendingAttempt;
        private static float pendingDeadline;
        private static bool loggedSuccess;
        private static bool loggedValidation;

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
                Plugin.ModLogger?.LogWarning(
                    "FS-41 pylon configuration was not applied because the Eclipse, ETS, " +
                    "AAM1, or 450kg drop-tank mount was not registered.");
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
            if (aircraft == null || !IsFs41(aircraft.definition))
            {
                return;
            }

            ApplyToDefinition(aircraft.definition, Encyclopedia.i);
            WeaponMount ets = FindEtsMount(Encyclopedia.i, weaponManager.hardpointSets);
            WeaponMount outboardMount = FindMountByJsonKey(
                Encyclopedia.i,
                weaponManager.hardpointSets,
                WingGloveOutboardMountKey);
            WeaponMount dropTankMount = Find450KgDropTankMount(
                Encyclopedia.i,
                weaponManager.hardpointSets);
            WeaponMount largeDropTankMount = FindMountByJsonKey(
                Encyclopedia.i,
                weaponManager.hardpointSets,
                DropTank1200MountKey);
            if (ets == null || outboardMount == null || dropTankMount == null ||
                largeDropTankMount == null ||
                !ApplyExportedStockPylonConfiguration(weaponManager, Encyclopedia.i) ||
                !EnsureTargetingPodPylon(
                    weaponManager,
                    aircraft,
                    ets,
                    out _) ||
                !EnsureWingGloveOutboardPylon(
                    weaponManager,
                    aircraft,
                    outboardMount,
                    out _) ||
                !EnsureDropTankPylon(
                    weaponManager,
                    aircraft,
                    dropTankMount,
                    largeDropTankMount,
                    out _))
            {
                return;
            }
            EnsureLoadoutLength(aircraft.loadout, weaponManager.hardpointSets.Length);
        }

        private static bool TryApplyToEncyclopedia(Encyclopedia encyclopedia)
        {
            AircraftDefinition definition = encyclopedia?.aircraft?.Find(IsFs41);
            return definition != null && ApplyToDefinition(definition, encyclopedia);
        }

        private static bool ApplyToDefinition(
            AircraftDefinition definition,
            Encyclopedia encyclopedia)
        {
            if (!TryGetWeaponManager(
                    definition,
                    out Aircraft aircraft,
                    out WeaponManager weaponManager))
            {
                return false;
            }

            WeaponMount ets = FindEtsMount(encyclopedia, weaponManager.hardpointSets);
            WeaponMount outboardMount = FindMountByJsonKey(
                encyclopedia,
                weaponManager.hardpointSets,
                WingGloveOutboardMountKey);
            WeaponMount dropTankMount = Find450KgDropTankMount(
                encyclopedia,
                weaponManager.hardpointSets);
            WeaponMount largeDropTankMount = FindMountByJsonKey(
                encyclopedia,
                weaponManager.hardpointSets,
                DropTank1200MountKey);
            if (ets == null || outboardMount == null || dropTankMount == null ||
                largeDropTankMount == null ||
                !ApplyExportedStockPylonConfiguration(weaponManager, encyclopedia) ||
                !EnsureTargetingPodPylon(
                    weaponManager,
                    aircraft,
                    ets,
                    out bool targetingPodAdded) ||
                !EnsureWingGloveOutboardPylon(
                    weaponManager,
                    aircraft,
                    outboardMount,
                    out bool wingGloveAdded) ||
                !EnsureDropTankPylon(
                    weaponManager,
                    aircraft,
                    dropTankMount,
                    largeDropTankMount,
                    out bool dropTankAdded))
            {
                return false;
            }

            int extended = EnsureParameterLoadoutLengths(
                definition.aircraftParameters,
                weaponManager.hardpointSets.Length);
            if (!loggedSuccess)
            {
                loggedSuccess = true;
                Plugin.ModLogger?.LogInfo(
                    "FS-41 exported pylon configuration active: revised stock wing stores " +
                    "and transforms, exported TGP geometry, and two-hardpoint " +
                    WingGloveOutboardPylonName + " set linked to the native wing-glove visual; " +
                    "ETS mount " + ets.jsonKey + ", outboard mount " + outboardMount.jsonKey +
                    ", drop-tank mounts " + dropTankMount.jsonKey + " and " +
                    largeDropTankMount.jsonKey + "; " +
                    extended + " stock/AI loadout(s) were extended." +
                    (targetingPodAdded ? string.Empty : " Existing TGP set was normalized.") +
                    (wingGloveAdded
                        ? string.Empty
                        : " Existing " + WingGloveOutboardPylonName + " set was normalized.") +
                    (dropTankAdded
                        ? string.Empty
                        : " Existing " + DropTankPylonName + " set was normalized."));
            }

            if (!loggedValidation)
            {
                if (ValidateFs41PylonConfiguration(
                        weaponManager,
                        aircraft,
                        ets,
                        outboardMount,
                        dropTankMount,
                        largeDropTankMount,
                        out string failure))
                {
                    loggedValidation = true;
                    Plugin.ModLogger?.LogInfo(
                        "FS-41 pylon validation passed: stock wing restrictions and transforms, " +
                        "exported TGP transform and mount, and two AAM1 outboard hardpoints " +
                        "with no visual of their own and linked stock wing-glove visibility; " +
                        "Drop Tanks has two renderer-free hardpoints with the exported tank options.");
                }
                else
                {
                    Plugin.ModLogger?.LogError("FS-41 pylon validation failed: " + failure);
                }
            }
            return true;
        }

        private static bool EnsureTargetingPodPylon(
            WeaponManager weaponManager,
            Aircraft aircraft,
            WeaponMount ets,
            out bool added)
        {
            added = false;
            if (weaponManager?.hardpointSets == null || aircraft == null || ets == null)
            {
                return false;
            }

            int index = FindSetIndex(weaponManager, TargetingPodPylonName);
            HardpointSet set;
            if (index < 0)
            {
                Hardpoint template = FindTemplateHardpoint(weaponManager.hardpointSets);
                if (template == null)
                {
                    return false;
                }

                set = new HardpointSet
                {
                    name = TargetingPodPylonName,
                    precludingHardpointSets = new List<byte>(),
                    SymmetryWithPrev = false,
                    SymmetryName = string.Empty,
                    weaponOptions = new List<WeaponMount> { ets },
                    weaponMount = null,
                    hardpoints = new List<Hardpoint>
                    {
                        CreateTargetingPodHardpoint(template, aircraft)
                    }
                };
                List<HardpointSet> expanded = new List<HardpointSet>(weaponManager.hardpointSets)
                {
                    set
                };
                weaponManager.hardpointSets = expanded.ToArray();
                added = true;
            }
            else
            {
                set = weaponManager.hardpointSets[index];
                if (set == null)
                {
                    return false;
                }
                if (set.hardpoints == null || set.hardpoints.Count == 0)
                {
                    Hardpoint template = FindTemplateHardpoint(weaponManager.hardpointSets);
                    if (template == null)
                    {
                        return false;
                    }
                    set.hardpoints = new List<Hardpoint>
                    {
                        CreateTargetingPodHardpoint(template, aircraft)
                    };
                }
                else if (set.hardpoints.Count > 1)
                {
                    set.hardpoints = new List<Hardpoint> { set.hardpoints[0] };
                }
            }

            set.name = TargetingPodPylonName;
            set.SymmetryWithPrev = false;
            set.SymmetryName = string.Empty;
            set.precludingHardpointSets = new List<byte>();
            set.weaponOptions = new List<WeaponMount> { ets };
            set.weaponMount = null;
            ConfigureTargetingPodHardpoint(set.hardpoints[0], aircraft);
            return true;
        }

        private static bool EnsureWingGloveOutboardPylon(
            WeaponManager weaponManager,
            Aircraft aircraft,
            WeaponMount outboardMount,
            out bool added)
        {
            added = false;
            if (weaponManager?.hardpointSets == null || aircraft == null || outboardMount == null)
            {
                return false;
            }

            int index = FindSetIndex(weaponManager, WingGloveOutboardPylonName);
            if (index < 0)
            {
                index = FindSetIndex(weaponManager, LegacyWingGloveOutboardPylonName);
            }

            HardpointSet set;
            if (index < 0)
            {
                Hardpoint template = FindTemplateHardpoint(weaponManager.hardpointSets);
                if (template == null)
                {
                    return false;
                }

                set = new HardpointSet
                {
                    name = WingGloveOutboardPylonName,
                    precludingHardpointSets = new List<byte>(),
                    SymmetryWithPrev = false,
                    SymmetryName = string.Empty,
                    weaponOptions = new List<WeaponMount> { outboardMount },
                    weaponMount = null,
                    hardpoints = new List<Hardpoint>
                    {
                        CreateWingGloveOutboardHardpoint(template, aircraft, 1),
                        CreateWingGloveOutboardHardpoint(template, aircraft, 2)
                    }
                };
                List<HardpointSet> expanded = new List<HardpointSet>(weaponManager.hardpointSets)
                {
                    set
                };
                weaponManager.hardpointSets = expanded.ToArray();
                added = true;
            }
            else
            {
                set = weaponManager.hardpointSets[index];
                if (set == null)
                {
                    return false;
                }

                Hardpoint template = FindTemplateHardpoint(weaponManager.hardpointSets);
                if (template == null)
                {
                    return false;
                }
                if (set.hardpoints == null)
                {
                    set.hardpoints = new List<Hardpoint>();
                }
                while (set.hardpoints.Count < 2)
                {
                    set.hardpoints.Add(CreateWingGloveOutboardHardpoint(
                        template,
                        aircraft,
                        set.hardpoints.Count + 1));
                }
                if (set.hardpoints.Count > 2)
                {
                    set.hardpoints = set.hardpoints.GetRange(0, 2);
                }
            }

            set.name = WingGloveOutboardPylonName;
            set.SymmetryWithPrev = false;
            set.SymmetryName = string.Empty;
            set.precludingHardpointSets = new List<byte>();
            set.weaponOptions = new List<WeaponMount> { outboardMount };
            set.weaponMount = null;
            for (int hardpointIndex = 0; hardpointIndex < set.hardpoints.Count; hardpointIndex++)
            {
                ConfigureWingGloveOutboardHardpoint(
                    set.hardpoints[hardpointIndex],
                    aircraft,
                    hardpointIndex + 1);
            }
            RegisterWingGloveVisibilityLink(weaponManager, set);
            return true;
        }

        private static bool EnsureDropTankPylon(
            WeaponManager weaponManager,
            Aircraft aircraft,
            WeaponMount dropTankMount,
            WeaponMount largeDropTankMount,
            out bool added)
        {
            added = false;
            if (weaponManager?.hardpointSets == null || aircraft == null ||
                dropTankMount == null || largeDropTankMount == null)
            {
                return false;
            }

            int index = FindSetIndex(weaponManager, DropTankPylonName);
            HardpointSet set;
            if (index < 0)
            {
                Hardpoint template = FindTemplateHardpoint(weaponManager.hardpointSets);
                if (template == null)
                {
                    return false;
                }

                set = new HardpointSet
                {
                    name = DropTankPylonName,
                    precludingHardpointSets = new List<byte>(),
                    SymmetryWithPrev = false,
                    SymmetryName = string.Empty,
                    weaponOptions = new List<WeaponMount>
                    {
                        dropTankMount,
                        largeDropTankMount
                    },
                    weaponMount = null,
                    hardpoints = new List<Hardpoint>
                    {
                        CreateDropTankHardpoint(template, aircraft, 1),
                        CreateDropTankHardpoint(template, aircraft, 2)
                    }
                };
                List<HardpointSet> expanded = new List<HardpointSet>(weaponManager.hardpointSets)
                {
                    set
                };
                weaponManager.hardpointSets = expanded.ToArray();
                added = true;
            }
            else
            {
                set = weaponManager.hardpointSets[index];
                if (set == null)
                {
                    return false;
                }

                Hardpoint template = FindTemplateHardpoint(weaponManager.hardpointSets);
                if (template == null)
                {
                    return false;
                }
                if (set.hardpoints == null)
                {
                    set.hardpoints = new List<Hardpoint>();
                }
                while (set.hardpoints.Count < 2)
                {
                    set.hardpoints.Add(CreateDropTankHardpoint(
                        template,
                        aircraft,
                        set.hardpoints.Count + 1));
                }
                if (set.hardpoints.Count > 2)
                {
                    set.hardpoints = set.hardpoints.GetRange(0, 2);
                }
            }

            set.name = DropTankPylonName;
            set.SymmetryWithPrev = false;
            set.SymmetryName = string.Empty;
            set.precludingHardpointSets = new List<byte>();
            set.weaponOptions = new List<WeaponMount>
            {
                dropTankMount,
                largeDropTankMount
            };
            set.weaponMount = null;
            for (int hardpointIndex = 0; hardpointIndex < set.hardpoints.Count; hardpointIndex++)
            {
                ConfigureDropTankHardpoint(
                    set.hardpoints[hardpointIndex],
                    aircraft,
                    hardpointIndex + 1);
            }
            return true;
        }

        private static Hardpoint CreateTargetingPodHardpoint(Hardpoint source, Aircraft aircraft)
        {
            GameObject anchor = new GameObject("FS-41 TGP Hardpoint 1");
            anchor.layer = source.transform.gameObject.layer;
            anchor.hideFlags = source.transform.gameObject.hideFlags;
            anchor.transform.SetParent(aircraft.transform, false);
            anchor.transform.localPosition = TargetingPodPosition;
            anchor.transform.localRotation = Quaternion.Euler(TargetingPodRotation);
            anchor.transform.localScale = Vector3.one;

            return CreateHardpointData(source, aircraft, anchor.transform);
        }

        private static Hardpoint CreateWingGloveOutboardHardpoint(
            Hardpoint source,
            Aircraft aircraft,
            int number)
        {
            GameObject assembly = new GameObject(
                "TGBR Wing Glove Outboard Pylon Assembly " + number);
            assembly.layer = source.transform.gameObject.layer;
            assembly.hideFlags = source.transform.gameObject.hideFlags;
            assembly.transform.SetParent(aircraft.transform, false);

            GameObject anchor = new GameObject(
                "TGBR Wing Glove Outboard Weapon Anchor " + number);
            anchor.layer = source.transform.gameObject.layer;
            anchor.hideFlags = source.transform.gameObject.hideFlags;
            anchor.transform.SetParent(assembly.transform, false);

            return CreateHardpointData(source, aircraft, anchor.transform);
        }

        private static Hardpoint CreateDropTankHardpoint(
            Hardpoint source,
            Aircraft aircraft,
            int number)
        {
            GameObject assembly = new GameObject("TGBR Drop Tank Pylon Assembly " + number);
            assembly.layer = source.transform.gameObject.layer;
            assembly.hideFlags = source.transform.gameObject.hideFlags;
            assembly.transform.SetParent(aircraft.transform, false);

            GameObject anchor = new GameObject("TGBR Drop Tank Weapon Anchor " + number);
            anchor.layer = source.transform.gameObject.layer;
            anchor.hideFlags = source.transform.gameObject.hideFlags;
            anchor.transform.SetParent(assembly.transform, false);

            return CreateHardpointData(source, aircraft, anchor.transform);
        }

        private static Hardpoint CreateHardpointData(
            Hardpoint source,
            Aircraft aircraft,
            Transform transform)
        {
            Hardpoint hardpoint = new Hardpoint
            {
                transform = transform,
                part = FindRootUnitPart(aircraft) ?? source.part,
                bayDoors = Array.Empty<BayDoor>(),
                doorOpenDuration = 0f,
                Pylon = null,
                Plug = null,
                BuiltInWeapons = Array.Empty<Weapon>(),
                BuiltInTurrets = Array.Empty<Turret>(),
                HardpointIndex = -1
            };
            ClearPylonOptions(hardpoint);
            return hardpoint;
        }

        private static void ConfigureTargetingPodHardpoint(
            Hardpoint hardpoint,
            Aircraft aircraft)
        {
            if (hardpoint?.transform == null)
            {
                return;
            }
            hardpoint.transform.name = "FS-41 TGP Hardpoint 1";
            hardpoint.transform.SetParent(aircraft.transform, false);
            hardpoint.transform.localPosition = TargetingPodPosition;
            hardpoint.transform.localRotation = Quaternion.Euler(TargetingPodRotation);
            hardpoint.transform.localScale = Vector3.one;
            ConfigureEmptyHardpoint(hardpoint, aircraft);
        }

        private static void ConfigureWingGloveOutboardHardpoint(
            Hardpoint hardpoint,
            Aircraft aircraft,
            int number)
        {
            if (hardpoint?.transform == null)
            {
                return;
            }
            hardpoint.transform.name = "TGBR Wing Glove Outboard Weapon Anchor " + number;
            hardpoint.transform.localPosition = WingGloveOutboardPositions[number - 1];
            hardpoint.transform.localRotation = Quaternion.Euler(
                WingGloveOutboardRotations[number - 1]);
            hardpoint.transform.localScale = Vector3.one;
            if (hardpoint.transform.parent != null &&
                hardpoint.transform.parent != aircraft.transform)
            {
                hardpoint.transform.parent.name =
                    "TGBR Wing Glove Outboard Pylon Assembly " + number;
                hardpoint.transform.parent.localPosition = Vector3.zero;
                hardpoint.transform.parent.localRotation = Quaternion.identity;
                hardpoint.transform.parent.localScale = Vector3.one;
            }
            ConfigureEmptyHardpoint(hardpoint, aircraft);
        }

        private static void ConfigureDropTankHardpoint(
            Hardpoint hardpoint,
            Aircraft aircraft,
            int number)
        {
            if (hardpoint?.transform == null)
            {
                return;
            }
            hardpoint.transform.name = "TGBR Drop Tank Weapon Anchor " + number;
            hardpoint.transform.localPosition = DropTankPositions[number - 1];
            hardpoint.transform.localRotation = Quaternion.identity;
            hardpoint.transform.localScale = Vector3.one;
            if (hardpoint.transform.parent != null &&
                hardpoint.transform.parent != aircraft.transform)
            {
                hardpoint.transform.parent.name = "TGBR Drop Tank Pylon Assembly " + number;
                hardpoint.transform.parent.localPosition = Vector3.zero;
                hardpoint.transform.parent.localRotation = Quaternion.identity;
                hardpoint.transform.parent.localScale = Vector3.one;
            }
            ConfigureEmptyHardpoint(hardpoint, aircraft);
        }

        private static void ConfigureEmptyHardpoint(Hardpoint hardpoint, Aircraft aircraft)
        {
            hardpoint.part = FindRootUnitPart(aircraft) ?? hardpoint.part;
            hardpoint.bayDoors = Array.Empty<BayDoor>();
            hardpoint.doorOpenDuration = 0f;
            hardpoint.Pylon = null;
            hardpoint.Plug = null;
            hardpoint.BuiltInWeapons = Array.Empty<Weapon>();
            hardpoint.BuiltInTurrets = Array.Empty<Turret>();
            hardpoint.HardpointIndex = -1;
            ClearPylonOptions(hardpoint);
        }

        private static Hardpoint FindTemplateHardpoint(HardpointSet[] sets)
        {
            if (sets == null)
            {
                return null;
            }
            foreach (HardpointSet set in sets)
            {
                if (set?.hardpoints == null)
                {
                    continue;
                }
                foreach (Hardpoint hardpoint in set.hardpoints)
                {
                    if (hardpoint?.transform != null)
                    {
                        return hardpoint;
                    }
                }
            }
            return null;
        }

        private static WeaponMount FindMountByJsonKey(
            Encyclopedia encyclopedia,
            HardpointSet[] hardpointSets,
            string jsonKey)
        {
            if (hardpointSets != null)
            {
                foreach (HardpointSet set in hardpointSets)
                {
                    WeaponMount match = set?.weaponOptions?.Find(candidate =>
                        candidate != null && string.Equals(
                            candidate.jsonKey,
                            jsonKey,
                            StringComparison.OrdinalIgnoreCase));
                    if (match != null)
                    {
                        return match;
                    }
                }
            }
            return encyclopedia?.weaponMounts?.Find(candidate =>
                candidate != null && string.Equals(
                    candidate.jsonKey,
                    jsonKey,
                    StringComparison.OrdinalIgnoreCase));
        }

        private static WeaponMount Find450KgDropTankMount(
            Encyclopedia encyclopedia,
            HardpointSet[] hardpointSets)
        {
            WeaponMount exact = FindMountByJsonKey(
                encyclopedia,
                hardpointSets,
                DropTank450MountKey);
            if (exact != null)
            {
                return exact;
            }

            WeaponMount best = null;
            int bestScore = 0;
            HashSet<WeaponMount> visited = new HashSet<WeaponMount>();

            if (hardpointSets != null)
            {
                foreach (HardpointSet set in hardpointSets)
                {
                    if (set?.weaponOptions == null)
                    {
                        continue;
                    }
                    foreach (WeaponMount mount in set.weaponOptions)
                    {
                        Score450KgDropTankCandidate(
                            mount,
                            50,
                            visited,
                            ref best,
                            ref bestScore);
                    }
                }
            }
            if (encyclopedia?.weaponMounts != null)
            {
                foreach (WeaponMount mount in encyclopedia.weaponMounts)
                {
                    Score450KgDropTankCandidate(
                        mount,
                        0,
                        visited,
                        ref best,
                        ref bestScore);
                }
            }
            return best;
        }

        private static void Score450KgDropTankCandidate(
            WeaponMount mount,
            int existingFs41Bonus,
            HashSet<WeaponMount> visited,
            ref WeaponMount best,
            ref int bestScore)
        {
            if (mount == null || !visited.Add(mount))
            {
                return;
            }

            int score = Math.Max(
                Score450KgDropTankLabel(mount.jsonKey),
                Math.Max(
                    Score450KgDropTankLabel(mount.mountName),
                    Math.Max(
                        Score450KgDropTankLabel(mount.info?.weaponName),
                        Score450KgDropTankLabel(mount.info?.shortName))));
            if (score <= 0)
            {
                return;
            }
            score += existingFs41Bonus;
            if (score > bestScore ||
                (score == bestScore && string.CompareOrdinal(mount.jsonKey, best?.jsonKey) < 0))
            {
                best = mount;
                bestScore = score;
            }
        }

        private static int Score450KgDropTankLabel(string value)
        {
            string normalized = Normalize(value);
            if (normalized == "450KGDROPTANK" || normalized == "DROPTANK450KG")
            {
                return 1000;
            }
            return normalized.Contains("450") && normalized.Contains("DROPTANK")
                ? 900
                : 0;
        }

        private static bool ApplyExportedStockPylonConfiguration(
            WeaponManager weaponManager,
            Encyclopedia encyclopedia)
        {
            int wingGloveIndex = FindSetIndex(weaponManager, WingGlovePylonName);
            int wingIndex = FindSetIndex(weaponManager, WingPylonName);
            if (wingGloveIndex < 0 || wingIndex < 0)
            {
                return false;
            }

            HardpointSet wingGlove = weaponManager.hardpointSets[wingGloveIndex];
            HardpointSet wing = weaponManager.hardpointSets[wingIndex];
            if (wingGlove?.hardpoints?.Count != 2 || wing?.hardpoints?.Count != 2)
            {
                return false;
            }

            List<WeaponMount> wingMounts = new List<WeaponMount>();
            foreach (string key in WingPylonMountKeys)
            {
                WeaponMount mount = FindMountByJsonKey(
                    encyclopedia,
                    weaponManager.hardpointSets,
                    key);
                if (mount == null)
                {
                    return false;
                }
                wingMounts.Add(mount);
            }

            if (wingGlove.weaponOptions == null)
            {
                return false;
            }
            wingGlove.weaponOptions.RemoveAll(IsForbiddenWingGloveMount);
            wing.weaponOptions = wingMounts;

            for (int index = 0; index < 2; index++)
            {
                Transform wingGloveTransform = wingGlove.hardpoints[index]?.transform;
                Transform wingTransform = wing.hardpoints[index]?.transform;
                if (wingGloveTransform == null || wingTransform == null)
                {
                    return false;
                }

                wingGloveTransform.localPosition = WingGlovePositions[index];
                wingGloveTransform.localRotation = Quaternion.Euler(WingGloveRotations[index]);
                wingGloveTransform.localScale = Vector3.one;
                wingTransform.localPosition = WingPylonPositions[index];
                wingTransform.localRotation = Quaternion.Euler(WingPylonRotations[index]);
                wingTransform.localScale = WingPylonScales[index];
            }
            return true;
        }

        private static bool IsForbiddenWingGloveMount(WeaponMount mount)
        {
            return mount != null &&
                   (string.Equals(
                        mount.jsonKey,
                        RemovedWingGloveMountKey,
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        mount.jsonKey,
                        RemovedWingGloveArmMountKey,
                        StringComparison.OrdinalIgnoreCase) ||
                    IsDropTankMount(mount));
        }

        private static bool IsDropTankMount(WeaponMount mount)
        {
            return mount != null &&
                   (Normalize(mount.jsonKey).Contains("DROPTANK") ||
                    Normalize(mount.mountName).Contains("DROPTANK") ||
                    Normalize(mount.info?.weaponName).Contains("DROPTANK") ||
                    Normalize(mount.info?.shortName).Contains("DROPTANK"));
        }

        private static void RegisterWingGloveVisibilityLink(
            WeaponManager weaponManager,
            HardpointSet outboardSet)
        {
            int sourceIndex = FindSetIndex(weaponManager, WingGlovePylonName);
            if (sourceIndex < 0 || outboardSet == null)
            {
                return;
            }

            HardpointSet wingGloveSet = weaponManager.hardpointSets[sourceIndex];
            if (wingGloveSet == null)
            {
                return;
            }
            WingGloveSetOwners[wingGloveSet] = weaponManager;
            WingGloveSetOwners[outboardSet] = weaponManager;
            SynchronizeWingGloveRenderer(weaponManager);
        }

        internal static void SynchronizeWingGloveRendererForSet(HardpointSet changedSet)
        {
            if (changedSet == null ||
                !WingGloveSetOwners.TryGetValue(changedSet, out WeaponManager weaponManager))
            {
                return;
            }
            SynchronizeWingGloveRenderer(weaponManager);
        }

        private static void SynchronizeWingGloveRenderer(WeaponManager weaponManager)
        {
            int wingGloveIndex = FindSetIndex(weaponManager, WingGlovePylonName);
            int outboardIndex = FindSetIndex(weaponManager, WingGloveOutboardPylonName);
            if (wingGloveIndex < 0 || outboardIndex < 0)
            {
                return;
            }

            HardpointSet wingGloveSet = weaponManager.hardpointSets[wingGloveIndex];
            HardpointSet outboardSet = weaponManager.hardpointSets[outboardIndex];
            if (wingGloveSet?.hardpoints == null || outboardSet == null)
            {
                return;
            }

            bool wingGloveLoaded = wingGloveSet.weaponMount != null;
            bool outboardLoaded = outboardSet.weaponMount != null;
            foreach (Hardpoint hardpoint in wingGloveSet.hardpoints)
            {
                if (hardpoint == null)
                {
                    continue;
                }

                if (wingGloveLoaded)
                {
                    hardpoint.ShowPylon(weaponLoaded: true);
                    continue;
                }

                hardpoint.ShowPylon(weaponLoaded: false);
                if (outboardLoaded)
                {
                    Renderer baseRenderer = GetBasePylonRenderer(hardpoint);
                    if (baseRenderer != null)
                    {
                        baseRenderer.enabled = true;
                    }
                }
            }
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

        private static WeaponMount FindEtsMount(
            Encyclopedia encyclopedia,
            HardpointSet[] hardpointSets)
        {
            WeaponMount best = null;
            int bestScore = 0;
            HashSet<WeaponMount> visited = new HashSet<WeaponMount>();

            if (hardpointSets != null)
            {
                foreach (HardpointSet set in hardpointSets)
                {
                    if (set?.weaponOptions == null)
                    {
                        continue;
                    }
                    foreach (WeaponMount mount in set.weaponOptions)
                    {
                        ScoreEtsCandidate(mount, 50, visited, ref best, ref bestScore);
                    }
                }
            }
            if (encyclopedia?.weaponMounts != null)
            {
                foreach (WeaponMount mount in encyclopedia.weaponMounts)
                {
                    ScoreEtsCandidate(mount, 0, visited, ref best, ref bestScore);
                }
            }

            return best;
        }

        private static void ScoreEtsCandidate(
            WeaponMount mount,
            int existingFs41Bonus,
            HashSet<WeaponMount> visited,
            ref WeaponMount best,
            ref int bestScore)
        {
            if (mount == null || !visited.Add(mount))
            {
                return;
            }

            int score = string.Equals(
                    mount.jsonKey,
                    EtsMountKey,
                    StringComparison.OrdinalIgnoreCase)
                ? 2000
                : Math.Max(
                    ScoreEtsLabel(mount.jsonKey),
                    Math.Max(
                        ScoreEtsLabel(mount.mountName),
                        Math.Max(
                            ScoreEtsLabel(mount.info?.weaponName),
                            ScoreEtsLabel(mount.info?.shortName))));
            if (score <= 0)
            {
                return;
            }
            score += existingFs41Bonus;
            if (score > bestScore ||
                (score == bestScore && string.CompareOrdinal(mount.jsonKey, best?.jsonKey) < 0))
            {
                best = mount;
                bestScore = score;
            }
        }

        private static int ScoreEtsLabel(string value)
        {
            string normalized = Normalize(value);
            if (normalized == "ETS")
            {
                return 1000;
            }
            if (normalized.Contains("EXTERNALTARGETINGSYSTEM"))
            {
                return 950;
            }
            if (normalized.Contains("ELECTROOPTICALTARGETINGSYSTEM") ||
                normalized.Contains("ENHANCEDTARGETINGSYSTEM"))
            {
                return 900;
            }
            if (ContainsEtsToken(value))
            {
                return 850;
            }
            return 0;
        }

        private static bool ContainsEtsToken(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }
            int tokenStart = -1;
            for (int index = 0; index <= value.Length; index++)
            {
                bool tokenCharacter = index < value.Length && char.IsLetterOrDigit(value[index]);
                if (tokenCharacter && tokenStart < 0)
                {
                    tokenStart = index;
                }
                else if (!tokenCharacter && tokenStart >= 0)
                {
                    int length = index - tokenStart;
                    if (length == 3 && string.Compare(
                            value,
                            tokenStart,
                            "ETS",
                            0,
                            3,
                            StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        return true;
                    }
                    tokenStart = -1;
                }
            }
            return false;
        }

        private static string Normalize(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }
            char[] buffer = new char[value.Length];
            int length = 0;
            foreach (char character in value)
            {
                if (char.IsLetterOrDigit(character))
                {
                    buffer[length++] = char.ToUpperInvariant(character);
                }
            }
            return new string(buffer, 0, length);
        }

        private static bool ValidateFs41PylonConfiguration(
            WeaponManager weaponManager,
            Aircraft aircraft,
            WeaponMount ets,
            WeaponMount outboardMount,
            WeaponMount dropTankMount,
            WeaponMount largeDropTankMount,
            out string failure)
        {
            failure = string.Empty;
            int index = FindSetIndex(weaponManager, TargetingPodPylonName);
            int wingGloveIndex = FindSetIndex(weaponManager, WingGloveOutboardPylonName);
            int dropTankIndex = FindSetIndex(weaponManager, DropTankPylonName);
            if (index < 0 || wingGloveIndex < 0 || dropTankIndex < 0 ||
                index >= wingGloveIndex || wingGloveIndex >= dropTankIndex)
            {
                failure = "TGP, Wing Glove Outboard, or Drop Tanks is missing or out of append order";
                return false;
            }

            HardpointSet set = weaponManager.hardpointSets[index];
            if (set.hardpoints?.Count != 1 || set.weaponOptions?.Count != 1 ||
                set.weaponOptions[0] != ets)
            {
                failure = "hardpoint count or ETS-only allow-list is incorrect";
                return false;
            }
            if (set.SymmetryWithPrev ||
                !string.IsNullOrEmpty(set.SymmetryName) ||
                (set.precludingHardpointSets?.Count ?? 0) != 0)
            {
                failure = "unexpected symmetry or preclusion metadata remains";
                return false;
            }

            Hardpoint hardpoint = set.hardpoints[0];
            if (hardpoint?.transform == null || hardpoint.transform.parent != aircraft.transform ||
                hardpoint.part == null)
            {
                failure = "hardpoint is not attached to the aircraft root";
                return false;
            }
            if (hardpoint.Pylon != null || hardpoint.Plug != null ||
                GetPylonOptionCount(hardpoint) != 0)
            {
                failure = "new hardpoint unexpectedly has a pylon visual";
                return false;
            }

            if ((hardpoint.transform.localPosition - TargetingPodPosition).sqrMagnitude > 0.000001f ||
                Quaternion.Angle(
                    hardpoint.transform.localRotation,
                    Quaternion.Euler(TargetingPodRotation)) > 0.01f)
            {
                failure = "TGP hardpoint does not match the exported transform";
                return false;
            }

            int stockWingGloveIndex = FindSetIndex(weaponManager, WingGlovePylonName);
            int stockWingIndex = FindSetIndex(weaponManager, WingPylonName);
            if (stockWingGloveIndex < 0 || stockWingIndex < 0)
            {
                failure = "stock wing pylon sets are missing";
                return false;
            }
            HardpointSet stockWingGlove = weaponManager.hardpointSets[stockWingGloveIndex];
            HardpointSet stockWing = weaponManager.hardpointSets[stockWingIndex];
            if (stockWingGlove.weaponOptions?.Exists(IsForbiddenWingGloveMount) == true ||
                !MountKeysMatch(stockWing.weaponOptions, WingPylonMountKeys) ||
                !HardpointTransformsMatch(
                    stockWingGlove.hardpoints,
                    WingGlovePositions,
                    WingGloveRotations,
                    null) ||
                !HardpointTransformsMatch(
                    stockWing.hardpoints,
                    WingPylonPositions,
                    WingPylonRotations,
                    WingPylonScales))
            {
                failure = "stock wing restrictions or transforms do not match the export";
                return false;
            }
            foreach (Hardpoint stockWingGloveHardpoint in stockWingGlove.hardpoints)
            {
                if (GetBasePylonRenderer(stockWingGloveHardpoint) == null)
                {
                    failure = "stock Wing Glove base renderer is unavailable for outboard linking";
                    return false;
                }
            }

            HardpointSet wingGloveSet = weaponManager.hardpointSets[wingGloveIndex];
            if (wingGloveSet.hardpoints?.Count != 2 ||
                wingGloveSet.weaponOptions?.Count != 1 ||
                wingGloveSet.weaponOptions[0] != outboardMount ||
                wingGloveSet.SymmetryWithPrev ||
                !string.IsNullOrEmpty(wingGloveSet.SymmetryName) ||
                (wingGloveSet.precludingHardpointSets?.Count ?? 0) != 0)
            {
                failure = "Wing Glove Outboard set does not match the exported AAM1-only layout";
                return false;
            }
            for (int hardpointIndex = 0;
                 hardpointIndex < wingGloveSet.hardpoints.Count;
                 hardpointIndex++)
            {
                Hardpoint wingGloveHardpoint = wingGloveSet.hardpoints[hardpointIndex];
                if (wingGloveHardpoint?.transform == null ||
                    !wingGloveHardpoint.transform.IsChildOf(aircraft.transform) ||
                    wingGloveHardpoint.part == null ||
                    wingGloveHardpoint.Pylon != null ||
                    wingGloveHardpoint.Plug != null ||
                    GetPylonOptionCount(wingGloveHardpoint) != 0)
                {
                    failure = "Wing Glove Outboard hardpoint attachment or visual metadata is invalid";
                    return false;
                }
                if ((wingGloveHardpoint.transform.localPosition -
                     WingGloveOutboardPositions[hardpointIndex]).sqrMagnitude > 0.000001f ||
                    Quaternion.Angle(
                        wingGloveHardpoint.transform.localRotation,
                        Quaternion.Euler(WingGloveOutboardRotations[hardpointIndex])) > 0.01f)
                {
                    failure = "Wing Glove Outboard hardpoint does not match the exported transform";
                    return false;
                }
            }

            HardpointSet dropTankSet = weaponManager.hardpointSets[dropTankIndex];
            if (dropTankSet.hardpoints?.Count != 2 ||
                dropTankSet.weaponOptions?.Count != 2 ||
                dropTankSet.weaponOptions[0] != dropTankMount ||
                dropTankSet.weaponOptions[1] != largeDropTankMount ||
                dropTankSet.SymmetryWithPrev ||
                !string.IsNullOrEmpty(dropTankSet.SymmetryName) ||
                (dropTankSet.precludingHardpointSets?.Count ?? 0) != 0)
            {
                failure = "Drop Tanks does not match the exported two-hardpoint tank layout";
                return false;
            }
            for (int hardpointIndex = 0;
                 hardpointIndex < dropTankSet.hardpoints.Count;
                 hardpointIndex++)
            {
                Hardpoint dropTankHardpoint = dropTankSet.hardpoints[hardpointIndex];
                if (dropTankHardpoint?.transform == null ||
                    !dropTankHardpoint.transform.IsChildOf(aircraft.transform) ||
                    dropTankHardpoint.part == null ||
                    dropTankHardpoint.Pylon != null ||
                    dropTankHardpoint.Plug != null ||
                    GetPylonOptionCount(dropTankHardpoint) != 0)
                {
                    failure = "Drop Tanks hardpoint attachment or visual metadata is invalid";
                    return false;
                }
                if ((dropTankHardpoint.transform.localPosition -
                     DropTankPositions[hardpointIndex]).sqrMagnitude > 0.000001f ||
                    Quaternion.Angle(
                        dropTankHardpoint.transform.localRotation,
                        Quaternion.identity) > 0.01f ||
                    (dropTankHardpoint.transform.localScale - Vector3.one).sqrMagnitude >
                    0.000001f)
                {
                    failure = "Drop Tanks hardpoint does not match the exported transform";
                    return false;
                }
            }
            return true;
        }

        private static bool MountKeysMatch(
            List<WeaponMount> mounts,
            IReadOnlyList<string> expectedKeys)
        {
            if (mounts == null || mounts.Count != expectedKeys.Count)
            {
                return false;
            }
            for (int index = 0; index < expectedKeys.Count; index++)
            {
                if (!string.Equals(
                        mounts[index]?.jsonKey,
                        expectedKeys[index],
                        StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool HardpointTransformsMatch(
            List<Hardpoint> hardpoints,
            IReadOnlyList<Vector3> positions,
            IReadOnlyList<Vector3> rotations,
            IReadOnlyList<Vector3> scales)
        {
            if (hardpoints == null || hardpoints.Count != positions.Count ||
                rotations.Count != positions.Count ||
                (scales != null && scales.Count != positions.Count))
            {
                return false;
            }
            for (int index = 0; index < hardpoints.Count; index++)
            {
                Transform transform = hardpoints[index]?.transform;
                Vector3 expectedScale = scales == null ? Vector3.one : scales[index];
                if (transform == null ||
                    (transform.localPosition - positions[index]).sqrMagnitude > 0.000001f ||
                    Quaternion.Angle(
                        transform.localRotation,
                        Quaternion.Euler(rotations[index])) > 0.01f ||
                    (transform.localScale - expectedScale).sqrMagnitude > 0.000001f)
                {
                    return false;
                }
            }
            return true;
        }

        private static int FindSetIndex(WeaponManager weaponManager, string name)
        {
            return weaponManager?.hardpointSets == null
                ? -1
                : Array.FindIndex(
                    weaponManager.hardpointSets,
                    set => set != null && string.Equals(
                        set.name,
                        name,
                        StringComparison.OrdinalIgnoreCase));
        }

        private static UnitPart FindRootUnitPart(Aircraft aircraft)
        {
            UnitPart direct = aircraft?.GetComponent<UnitPart>();
            if (direct != null)
            {
                return direct;
            }
            if (aircraft != null)
            {
                foreach (UnitPart part in aircraft.GetComponentsInChildren<UnitPart>(true))
                {
                    if (part != null && part.transform == aircraft.transform)
                    {
                        return part;
                    }
                }
            }
            return null;
        }

        private static void ClearPylonOptions(Hardpoint hardpoint)
        {
            Type elementType = PylonOptionsField?.FieldType.GetElementType();
            if (elementType != null)
            {
                PylonOptionsField.SetValue(hardpoint, Array.CreateInstance(elementType, 0));
            }
        }

        private static int GetPylonOptionCount(Hardpoint hardpoint)
        {
            return PylonOptionsField?.GetValue(hardpoint) is Array options
                ? options.Length
                : 0;
        }

        private static bool TryGetWeaponManager(
            AircraftDefinition definition,
            out Aircraft aircraft,
            out WeaponManager weaponManager)
        {
            aircraft = null;
            weaponManager = null;
            if (definition?.unitPrefab == null)
            {
                return false;
            }
            aircraft = definition.unitPrefab.GetComponent<Aircraft>() ??
                       definition.unitPrefab.GetComponentInChildren<Aircraft>(true);
            weaponManager = aircraft?.weaponManager ??
                            definition.unitPrefab.GetComponentInChildren<WeaponManager>(true);
            return aircraft != null && weaponManager != null;
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
                    extended += EnsureLoadoutLength(loadout, hardpointSetCount) ? 1 : 0;
                }
            }
            if (parameters.StandardLoadouts != null)
            {
                foreach (StandardLoadout standardLoadout in parameters.StandardLoadouts)
                {
                    extended += EnsureLoadoutLength(
                        standardLoadout?.loadout,
                        hardpointSetCount) ? 1 : 0;
                }
            }
            return extended;
        }

        private static bool EnsureLoadoutLength(Loadout loadout, int hardpointSetCount)
        {
            if (loadout?.weapons == null)
            {
                return false;
            }
            bool extended = loadout.weapons.Count < hardpointSetCount;
            while (loadout.weapons.Count < hardpointSetCount)
            {
                loadout.weapons.Add(null);
            }
            return extended;
        }

        private static bool IsFs41(AircraftDefinition definition)
        {
            if (definition == null)
            {
                return false;
            }
            return string.Equals(
                       definition.jsonKey,
                       Fs41DefinitionKey,
                       StringComparison.OrdinalIgnoreCase) ||
                   ContainsIgnoreCase(definition.jsonKey, "Interceptor1") ||
                   ContainsIgnoreCase(definition.unitName, "FS-41") ||
                   ContainsIgnoreCase(definition.unitName, "Eclipse") ||
                   string.Equals(definition.code, "FS-41", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ContainsIgnoreCase(string value, string expected)
        {
            return value != null && value.IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    [HarmonyPatch]
    internal static class Fs41TargetingPodEncyclopediaPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Encyclopedia), "AfterLoad", Type.EmptyTypes);
        }

        private static void Postfix(Encyclopedia __instance)
        {
            Fs41TargetingPodPylon.RegisterEncyclopedia(__instance);
        }
    }

    [HarmonyPatch(typeof(WeaponManager), "Awake")]
    internal static class Fs41TargetingPodWeaponManagerAwakePatch
    {
        private static void Prefix(WeaponManager __instance)
        {
            Fs41TargetingPodPylon.ApplyToWeaponManager(__instance);
        }
    }

    [HarmonyPatch(typeof(WeaponManager), nameof(WeaponManager.SpawnWeapons))]
    internal static class Fs41TargetingPodSpawnWeaponsPatch
    {
        private static void Prefix(WeaponManager __instance)
        {
            Fs41TargetingPodPylon.ApplyToWeaponManager(__instance);
        }
    }

    [HarmonyPatch(typeof(HardpointSet), nameof(HardpointSet.SpawnMounts))]
    internal static class Fs41WingGloveSpawnMountsPatch
    {
        private static void Postfix(HardpointSet __instance)
        {
            Fs41TargetingPodPylon.SynchronizeWingGloveRendererForSet(__instance);
        }
    }

    [HarmonyPatch(typeof(HardpointSet), nameof(HardpointSet.RemoveMounts))]
    internal static class Fs41WingGloveRemoveMountsPatch
    {
        private static void Postfix(HardpointSet __instance)
        {
            Fs41TargetingPodPylon.SynchronizeWingGloveRendererForSet(__instance);
        }
    }
}

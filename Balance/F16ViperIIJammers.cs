using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using NuclearOption.Networking;
using NuclearOption.SavedMission;
using UnityEngine;

namespace TheGreatBigRebalancing.Balance
{
    internal static class F16ViperIIJammers
    {
        internal sealed class ServerValidationState
        {
            internal HardpointSet Centerline;
            internal int CenterlineIndex;
            internal HardpointSet[] HardpointSets;
            internal List<WeaponMount>[] WeaponOptions;
            internal WeaponMount[] SelectedMounts;
        }

        internal struct EcmPowerDrawState
        {
            internal bool Active;
            internal bool HasOjs;
            internal bool HasAdj;
            internal bool Blocked;
            internal float ChargeBefore;
            internal float OriginalRequestedPower;
            internal float ConsumptionScale;
            internal Aircraft Aircraft;
        }

        internal struct AdjRechargeState
        {
            internal bool Active;
            internal float OriginalChargePerRpm;
        }

        private sealed class JammerSpec
        {
            internal JammerSpec(string key, string code, string displayName, string description)
            {
                Key = key;
                Code = code;
                DisplayName = displayName;
                Description = description;
            }

            internal string Key { get; }
            internal string Code { get; }
            internal string DisplayName { get; }
            internal string Description { get; }
        }

        private sealed class JamDispatchProofState
        {
            internal Aircraft JammingAircraft;
            internal Unit Target;
            internal int Calls;
            internal bool InvalidCall;
        }

        internal struct EcmTargetSelection
        {
            internal Unit Target;
            internal Missile ThreatMissile;
            internal bool IsMissileThreat;
            internal bool IsSarhSource;
        }

        private const string TemplateMountKey = "JammingPod1";
        private const string AdjMountKey = "TGBR_ADJ94";
        private const string OjsMountKey = "TGBR_OJS808";
        private const float PurchaseCost = 27f;
        private const float LengthScale = 0.9f;
        private const float SlimnessScale = 0.8f;
        private const float RadialScale = LengthScale * SlimnessScale;
        private const float DragScale = RadialScale * RadialScale;
        private const float MassScale = DragScale * LengthScale;
        private const float EcmConsumptionScale = 1.6f;
        private const float AdjConsumptionMultiplier = 0.7f;
        private const float AdjRechargeMultiplier = 1.3f;
        private const float OjsConsumptionMultiplier = 0.5f;
        private const float OjsRechargeThreshold = 300f;
        private const float EcmPulseLifetime = 0.15f;
        internal const int TargetChannelsPerPod = 1;

        private static readonly JammerSpec[] Specs =
        {
            new JammerSpec(
                "TGBR_ADJ94",
                "ADJ-94",
                "ADJ-94 Self-Defence ECM Suite",
                "Viper II centerline self-defense jamming suite."),
            new JammerSpec(
                "TGBR_OJS808",
                "OJS-808",
                "OJS-808 Semi-Offensive Jamming Suite",
                "Viper II centerline semi-offensive jamming suite."),
            new JammerSpec(
                "TGBR_MIM20",
                "MiM-20",
                "MiM-20 Datalink and Radar-Return Spoofer",
                "Viper II centerline datalink and radar-return spoofing suite.")
        };

        private static readonly List<WeaponMount> Mounts = new List<WeaponMount>();
        private static readonly Dictionary<PowerSupply, Aircraft> ViperPowerSupplies =
            new Dictionary<PowerSupply, Aircraft>();
        private static readonly HashSet<PowerSupply> AdjRechargePowerSupplies =
            new HashSet<PowerSupply>();
        private static readonly HashSet<string> LoggedPassiveSuites =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly FieldInfo PowerSupplyCharge =
            AccessTools.Field(typeof(PowerSupply), "charge");
        private static readonly FieldInfo PowerSupplyDrawn =
            AccessTools.Field(typeof(PowerSupply), "powerDrawn");
        private static readonly FieldInfo PowerSupplyRequested =
            AccessTools.Field(typeof(PowerSupply), "powerRequested");
        private static readonly FieldInfo PowerSupplyChargePerRpm =
            AccessTools.Field(typeof(PowerSupply), "chargePerRPM");
        private static readonly FieldInfo RadarJammerPowerUsage =
            AccessTools.Field(typeof(RadarJammer), "powerUsage");
        private static readonly FieldInfo RadarJammerIntensity =
            AccessTools.Field(typeof(RadarJammer), "jammingIntensity");
        private static readonly FieldInfo RadarJammerCurrentIntensity =
            AccessTools.Field(typeof(RadarJammer), "jamIntensityCurrent");
        private static readonly FieldInfo JammingPodRangeFalloff =
            AccessTools.Field(typeof(JammingPod), "rangeFalloff");
        private static readonly FieldInfo JammingPodDirectionTransform =
            AccessTools.Field(typeof(JammingPod), "directionTransform");
        private static readonly FieldInfo HardpointSpawnedPrefab =
            AccessTools.Field(typeof(Hardpoint), "spawnedPrefab");
        private static WeaponMount templateMount;
        private static bool loggedServerValidation;
        private static bool loggedAdjPowerEngaged;
        private static bool loggedAdjRechargeEngaged;
        private static bool loggedOjsPowerEngaged;

        [ThreadStatic]
        private static JamDispatchProofState activeJamDispatchProof;

        [ThreadStatic]
        private static int viperEcmPowerTickDepth;

        [ThreadStatic]
        private static RadarJammer activeViperEcm;

        [ThreadStatic]
        private static bool activeViperEcmHasOjs;

        [ThreadStatic]
        private static bool activeViperEcmHasAdj;

        [ThreadStatic]
        private static bool activeViperEcmBlocked;

        internal static bool TryRegister(Encyclopedia encyclopedia)
        {
            if (encyclopedia?.weaponMounts == null)
            {
                return false;
            }

            templateMount = encyclopedia.weaponMounts.Find(
                mount => mount != null &&
                         string.Equals(
                             mount.jsonKey,
                             TemplateMountKey,
                             StringComparison.OrdinalIgnoreCase));
            if (templateMount?.prefab == null || templateMount.info == null)
            {
                return false;
            }

            Mounts.Clear();
            foreach (JammerSpec spec in Specs)
            {
                WeaponMount mount = encyclopedia.weaponMounts.Find(
                    candidate => candidate != null &&
                                 string.Equals(
                                     candidate.jsonKey,
                                     spec.Key,
                                     StringComparison.OrdinalIgnoreCase));
                if (mount == null)
                {
                    mount = CreateMount(templateMount, spec);
                    encyclopedia.weaponMounts.Add(mount);
                }
                Mounts.Add(mount);
            }
            return ValidateRegistration(encyclopedia, out _);
        }

        internal static void RebindRegistration(Encyclopedia encyclopedia)
        {
            templateMount = encyclopedia?.weaponMounts?.Find(
                mount => mount != null &&
                         string.Equals(
                             mount.jsonKey,
                             TemplateMountKey,
                             StringComparison.OrdinalIgnoreCase));
            Mounts.Clear();
            if (encyclopedia?.weaponMounts == null)
            {
                return;
            }
            foreach (JammerSpec spec in Specs)
            {
                WeaponMount mount = encyclopedia.weaponMounts.Find(
                    candidate => candidate != null &&
                                 string.Equals(
                                     candidate.jsonKey,
                                     spec.Key,
                                     StringComparison.OrdinalIgnoreCase));
                if (mount != null)
                {
                    Mounts.Add(mount);
                }
            }
        }

        internal static void ConfigureCenterline(HardpointSet centerline)
        {
            if (centerline == null || Mounts.Count != Specs.Length)
            {
                return;
            }
            centerline.weaponOptions = new List<WeaponMount>(Mounts);
            if (!IsViperJammer(centerline.weaponMount))
            {
                centerline.weaponMount = null;
            }
        }

        internal static bool IsViperJammer(WeaponMount mount)
        {
            if (mount == null)
            {
                return false;
            }
            foreach (JammerSpec spec in Specs)
            {
                if (string.Equals(mount.jsonKey, spec.Key, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        internal static void ConfigureSpawnedWeapon(Weapon weapon, WeaponMount weaponMount)
        {
            if (weapon != null && IsViperJammer(weaponMount) && weaponMount.info != null)
            {
                weapon.info = weaponMount.info;
            }
        }

        internal static bool AttachAsEcmPod(
            WeaponManager weaponManager,
            Weapon weapon,
            WeaponMount weaponMount,
            Hardpoint hardpoint)
        {
            ConfigureSpawnedWeapon(weapon, weaponMount);
            if (weapon == null || !IsViperJammer(weaponMount))
            {
                return false;
            }

            Aircraft aircraft = weaponManager?.GetComponentInParent<Aircraft>();
            if (aircraft == null)
            {
                return false;
            }
            weapon.AttachToHardpoint(aircraft, hardpoint, weaponMount);
            if (!(weapon is JammingPod pod))
            {
                Plugin.ModLogger?.LogError(
                    "F-16VX custom jammer mount did not spawn a JammingPod component.");
                return true;
            }
            pod.enabled = false;
            if (IsOjsMount(weaponMount))
            {
                F16ViperIIJammerController controller =
                    aircraft.GetComponent<F16ViperIIJammerController>() ??
                    aircraft.gameObject.AddComponent<F16ViperIIJammerController>();
                controller.Register(pod);
            }
            else if (LoggedPassiveSuites.Add(weaponMount.jsonKey))
            {
                Plugin.ModLogger?.LogInfo(
                    "F-16VX mounted " + weaponMount.mountName +
                    " as a non-jamming suite with zero Unit.Jam target channels.");
            }
            return true;
        }

        internal static int ActivatePodsFromEcm(Aircraft aircraft)
        {
            if (aircraft == null || !F16ViperII.IsVariant(aircraft.definition))
            {
                return 0;
            }
            EnsureRuntimeDeployment(aircraft, forceSpawn: true);
            if (!HasEquippedOjs(aircraft))
            {
                DisableViperJammerPods(aircraft);
                return 0;
            }
            F16ViperIIJammerController controller =
                aircraft.GetComponent<F16ViperIIJammerController>();
            if (controller == null || controller.RegisteredPodCount == 0)
            {
                foreach (JammingPod pod in aircraft.GetComponentsInChildren<JammingPod>(true))
                {
                    if (pod == null || pod.attachedUnit != aircraft || !IsOjsPodInfo(pod.info))
                    {
                        continue;
                    }
                    controller = controller ??
                        aircraft.gameObject.AddComponent<F16ViperIIJammerController>();
                    controller.Register(pod);
                }
            }
            if (IsOjsRechargeLocked(aircraft))
            {
                controller?.Stop();
                return 0;
            }
            return controller?.Pulse() ?? 0;
        }

        internal static void FinalizeRuntimeDeployment(WeaponManager weaponManager)
        {
            Aircraft aircraft = weaponManager?.GetComponentInParent<Aircraft>();
            if (aircraft != null)
            {
                EnsureRuntimeDeployment(aircraft, forceSpawn: false);
            }
        }

        internal static int EnsureRuntimeDeployment(Aircraft aircraft, bool forceSpawn)
        {
            WeaponManager weaponManager = aircraft?.weaponManager;
            if (aircraft == null || !F16ViperII.IsVariant(aircraft.definition) ||
                weaponManager?.hardpointSets == null)
            {
                return 0;
            }
            int centerlineIndex = F16ViperII.FindCenterlineIndex(weaponManager);
            if (centerlineIndex < 0 || centerlineIndex >= weaponManager.hardpointSets.Length)
            {
                return 0;
            }
            HardpointSet centerline = weaponManager.hardpointSets[centerlineIndex];
            WeaponMount selectedMount = centerline?.weaponMount;
            if (!IsViperJammer(selectedMount) && aircraft.loadout?.weapons != null &&
                centerlineIndex < aircraft.loadout.weapons.Count)
            {
                selectedMount = aircraft.loadout.weapons[centerlineIndex];
            }
            SetAdjRechargeEnabled(aircraft, IsAdjMount(selectedMount));
            if (!IsViperJammer(selectedMount) || centerline?.hardpoints == null)
            {
                aircraft.GetComponent<F16ViperIIJammerController>()?.Stop();
                return 0;
            }
            bool enablesDirectionalJamming = IsOjsMount(selectedMount);
            F16ViperIIJammerController existingController =
                aircraft.GetComponent<F16ViperIIJammerController>();
            if (enablesDirectionalJamming && existingController != null &&
                existingController.RegisteredPodCount > 0)
            {
                return existingController.RegisteredPodCount;
            }

            F16ViperIIJammerController controller = enablesDirectionalJamming
                ? existingController ??
                  aircraft.gameObject.AddComponent<F16ViperIIJammerController>()
                : null;
            int physicalPods = RecoverPhysicalPods(
                aircraft,
                centerline,
                selectedMount,
                controller,
                enablesDirectionalJamming);
            if (physicalPods > 0 && !enablesDirectionalJamming)
            {
                return physicalPods;
            }
            bool forcedSpawn = false;
            if (physicalPods == 0 && forceSpawn)
            {
                forcedSpawn = true;
                centerline.RemoveMounts();
                centerline.SpawnMounts(aircraft, selectedMount);
                physicalPods = RecoverPhysicalPods(
                    aircraft,
                    centerline,
                    selectedMount,
                    controller,
                    enablesDirectionalJamming);
            }
            if (physicalPods == 0)
            {
                if (forceSpawn)
                {
                    Plugin.ModLogger?.LogError(
                        "F-16VX authoritative ECM recovery selected " +
                        selectedMount.mountName +
                        " but could not create a physical centerline JammingPod.");
                }
                return 0;
            }

            Plugin.ModLogger?.LogInfo(
                "F-16VX " + (forcedSpawn ? "authoritative ECM recovery forced" :
                "live deployment recovered") + " " + physicalPods +
                " physical centerline pod(s) with " +
                (enablesDirectionalJamming
                    ? controller.RegisteredChannelCount + " independent target " +
                      (controller.RegisteredChannelCount == 1 ? "channel." : "channels.")
                    : "zero Unit.Jam target channels."));
            return physicalPods;
        }

        private static int RecoverPhysicalPods(
            Aircraft aircraft,
            HardpointSet centerline,
            WeaponMount selectedMount,
            F16ViperIIJammerController controller,
            bool enablesDirectionalJamming)
        {
            int physicalPods = 0;
            foreach (Hardpoint hardpoint in centerline.hardpoints)
            {
                GameObject spawnedPrefab = HardpointSpawnedPrefab?.GetValue(hardpoint) as GameObject;
                if (spawnedPrefab == null)
                {
                    continue;
                }
                spawnedPrefab.SetActive(true);
                foreach (JammingPod pod in
                         spawnedPrefab.GetComponentsInChildren<JammingPod>(true))
                {
                    pod.enabled = false;
                    ConfigureSpawnedWeapon(pod, selectedMount);
                    if (pod.attachedUnit != aircraft)
                    {
                        pod.AttachToHardpoint(aircraft, hardpoint, selectedMount);
                    }
                    if (enablesDirectionalJamming)
                    {
                        controller.Register(pod);
                    }
                    physicalPods++;
                }
            }
            return physicalPods;
        }

        internal static bool BeginJamDispatchProof(
            Aircraft aircraft,
            Unit target,
            out string failure)
        {
            failure = string.Empty;
            if (aircraft == null || target == null || activeJamDispatchProof != null)
            {
                failure = "a Unit target was unavailable for the single-channel Unit.Jam proof";
                return false;
            }
            activeJamDispatchProof = new JamDispatchProofState
            {
                JammingAircraft = aircraft,
                Target = target
            };
            return true;
        }

        internal static bool CompleteJamDispatchProof(out string failure)
        {
            JamDispatchProofState proof = activeJamDispatchProof;
            activeJamDispatchProof = null;
            if (proof == null || proof.InvalidCall || proof.Calls != 1)
            {
                failure = "the controller channel did not invoke Unit.Jam exactly once " +
                    "(calls=" + (proof?.Calls ?? 0) + ")";
                return false;
            }
            failure = string.Empty;
            return true;
        }

        internal static bool RunOriginalUnitJam(Unit target, Unit.JamEventArgs args)
        {
            JamDispatchProofState proof = activeJamDispatchProof;
            if (proof == null)
            {
                return true;
            }
            if (args.jammingUnit != proof.JammingAircraft || args.jamAmount <= 0f)
            {
                proof.InvalidCall = true;
                return false;
            }
            if (target == proof.Target)
            {
                proof.Calls++;
            }
            else
            {
                proof.InvalidCall = true;
            }
            return false;
        }

        internal static bool RunOriginalPodUpdate(JammingPod pod)
        {
            return pod == null || !IsViperJammerInfo(pod.info);
        }

        internal static bool BeginViperEcmPowerTick(RadarJammer radarEcm)
        {
            Aircraft aircraft = radarEcm?.aircraft;
            if (aircraft == null || !F16ViperII.IsVariant(aircraft.definition))
            {
                return false;
            }

            bool hasOjs = HasEquippedOjs(aircraft);
            bool hasAdj = !hasOjs && HasEquippedAdj(aircraft);
            SetAdjRechargeEnabled(aircraft, hasAdj);
            if (!hasOjs)
            {
                ResetOjsRechargeLock(aircraft);
            }
            bool blocked = hasOjs && IsOjsRechargeLocked(aircraft);
            viperEcmPowerTickDepth++;
            activeViperEcm = radarEcm;
            activeViperEcmHasOjs = hasOjs;
            activeViperEcmHasAdj = hasAdj;
            activeViperEcmBlocked = blocked;
            if (hasOjs && !loggedOjsPowerEngaged)
            {
                loggedOjsPowerEngaged = true;
                float request = RadarJammerPowerUsage != null
                    ? (float)RadarJammerPowerUsage.GetValue(radarEcm)
                    : -1f;
                Plugin.ModLogger?.LogInfo(
                    "OJS-808 constant-power Radar ECM engaged; configured request=" +
                    request + ", effective draw=" +
                    request * EcmConsumptionScale * OjsConsumptionMultiplier + ".");
            }
            if (hasAdj && !loggedAdjPowerEngaged)
            {
                loggedAdjPowerEngaged = true;
                float request = RadarJammerPowerUsage != null
                    ? (float)RadarJammerPowerUsage.GetValue(radarEcm)
                    : -1f;
                Plugin.ModLogger?.LogInfo(
                    "ADJ-94 Radar ECM economy engaged; configured request=" +
                    request + ", effective draw=" +
                    request * EcmConsumptionScale * AdjConsumptionMultiplier +
                    " (30% below the Viper II baseline).");
            }
            return true;
        }

        internal static void EndViperEcmPowerTick(bool entered)
        {
            if (!entered || viperEcmPowerTickDepth <= 0)
            {
                return;
            }
            viperEcmPowerTickDepth--;
            if (viperEcmPowerTickDepth == 0)
            {
                activeViperEcm = null;
                activeViperEcmHasOjs = false;
                activeViperEcmHasAdj = false;
                activeViperEcmBlocked = false;
            }
        }

        internal static EcmPowerDrawState BeginViperEcmPowerDraw(
            PowerSupply powerSupply,
            ref float requestedPower)
        {
            EcmPowerDrawState state = default;
            if (viperEcmPowerTickDepth <= 0 || activeViperEcm?.aircraft == null ||
                powerSupply == null || PowerSupplyCharge == null || PowerSupplyDrawn == null)
            {
                return state;
            }
            state.Active = true;
            state.HasOjs = activeViperEcmHasOjs;
            state.HasAdj = activeViperEcmHasAdj;
            state.Blocked = activeViperEcmBlocked;
            state.ChargeBefore = (float)PowerSupplyCharge.GetValue(powerSupply);
            state.OriginalRequestedPower = Mathf.Max(requestedPower, 0f);
            state.ConsumptionScale = EcmConsumptionScale *
                (state.HasOjs ? OjsConsumptionMultiplier :
                 state.HasAdj ? AdjConsumptionMultiplier : 1f);
            state.Aircraft = activeViperEcm.aircraft;
            if (!state.Blocked)
            {
                requestedPower = state.OriginalRequestedPower * state.ConsumptionScale;
            }
            return state;
        }

        internal static void CompleteViperEcmPowerDraw(
            PowerSupply powerSupply,
            ref float suppliedPower,
            EcmPowerDrawState state)
        {
            if (!state.Active || state.Blocked || powerSupply == null)
            {
                return;
            }

            if (!state.HasOjs)
            {
                suppliedPower /= state.ConsumptionScale;
                return;
            }

            float costPower = state.OriginalRequestedPower * state.ConsumptionScale;
            float fullPower = state.ChargeBefore > 0f ? state.OriginalRequestedPower : 0f;
            float chargeAfter = Mathf.Max(
                0f,
                state.ChargeBefore - costPower * Time.deltaTime);
            float originalSuppliedPower = suppliedPower;
            float powerDrawnAfter = (float)PowerSupplyDrawn.GetValue(powerSupply);
            PowerSupplyCharge.SetValue(powerSupply, chargeAfter);
            PowerSupplyDrawn.SetValue(
                powerSupply,
                powerDrawnAfter + costPower - originalSuppliedPower);
            suppliedPower = fullPower;
            HandleOjsPowerState(state.Aircraft, chargeAfter);
        }

        internal static AdjRechargeState BeginAdjRecharge(PowerSupply powerSupply)
        {
            AdjRechargeState state = default;
            if (powerSupply == null ||
                !ViperPowerSupplies.TryGetValue(powerSupply, out Aircraft aircraft) ||
                aircraft == null ||
                !AdjRechargePowerSupplies.Contains(powerSupply) ||
                PowerSupplyChargePerRpm == null)
            {
                return state;
            }

            state.Active = true;
            state.OriginalChargePerRpm =
                (float)PowerSupplyChargePerRpm.GetValue(powerSupply);
            PowerSupplyChargePerRpm.SetValue(
                powerSupply,
                state.OriginalChargePerRpm * AdjRechargeMultiplier);
            if (!loggedAdjRechargeEngaged)
            {
                loggedAdjRechargeEngaged = true;
                Plugin.ModLogger?.LogInfo(
                    "ADJ-94 capacitor generation engaged at 130% of the Viper II baseline.");
            }
            return state;
        }

        internal static void EndAdjRecharge(
            PowerSupply powerSupply,
            ref AdjRechargeState state)
        {
            if (!state.Active || powerSupply == null || PowerSupplyChargePerRpm == null)
            {
                return;
            }
            PowerSupplyChargePerRpm.SetValue(
                powerSupply,
                state.OriginalChargePerRpm);
            state.Active = false;
        }

        internal static void RegisterViperPowerSupply(
            Aircraft aircraft,
            PowerSupply powerSupply)
        {
            if (aircraft == null || powerSupply == null)
            {
                return;
            }
            ViperPowerSupplies[powerSupply] = aircraft;
            SetAdjRechargeEnabled(powerSupply, HasEquippedAdj(aircraft));
            F16ViperIIPowerSupplyRegistration registration =
                powerSupply.GetComponent<F16ViperIIPowerSupplyRegistration>() ??
                powerSupply.gameObject.AddComponent<F16ViperIIPowerSupplyRegistration>();
            registration.Initialize(powerSupply);
        }

        internal static void UnregisterViperPowerSupply(PowerSupply powerSupply)
        {
            if (powerSupply != null)
            {
                ViperPowerSupplies.Remove(powerSupply);
                AdjRechargePowerSupplies.Remove(powerSupply);
            }
        }

        private static void SetAdjRechargeEnabled(Aircraft aircraft, bool enabled)
        {
            PowerSupply powerSupply = aircraft?.GetPowerSupply();
            if (powerSupply != null && ViperPowerSupplies.ContainsKey(powerSupply))
            {
                SetAdjRechargeEnabled(powerSupply, enabled);
            }
        }

        private static void SetAdjRechargeEnabled(PowerSupply powerSupply, bool enabled)
        {
            if (powerSupply == null)
            {
                return;
            }
            if (enabled)
            {
                AdjRechargePowerSupplies.Add(powerSupply);
            }
            else
            {
                AdjRechargePowerSupplies.Remove(powerSupply);
            }
        }

        internal static void ScaleSpawnedMount(GameObject spawnedMount, WeaponMount weaponMount)
        {
            if (spawnedMount == null || !IsViperJammer(weaponMount))
            {
                return;
            }
            spawnedMount.transform.localScale = Vector3.Scale(
                weaponMount.prefab.transform.localScale,
                new Vector3(RadialScale, RadialScale, LengthScale));
        }

        internal static bool ActivatePrefabForSpawn(WeaponMount weaponMount)
        {
            if (!IsViperJammer(weaponMount) || weaponMount.prefab == null ||
                weaponMount.prefab.activeSelf)
            {
                return false;
            }
            weaponMount.prefab.SetActive(true);
            return true;
        }

        internal static void RestorePrefabAfterSpawn(
            WeaponMount weaponMount,
            ref bool activatedForSpawn)
        {
            if (!activatedForSpawn)
            {
                return;
            }
            if (weaponMount?.prefab != null)
            {
                weaponMount.prefab.SetActive(false);
            }
            activatedForSpawn = false;
        }

        internal static ServerValidationState BeginServerValidation(
            AircraftDefinition definition,
            Loadout requestedLoadout)
        {
            if (!F16ViperII.IsVariant(definition))
            {
                return null;
            }

            Aircraft aircraft = definition.unitPrefab?.GetComponent<Aircraft>() ??
                                definition.unitPrefab?.GetComponentInChildren<Aircraft>(true);
            WeaponManager weaponManager = aircraft?.weaponManager;
            int centerlineIndex = F16ViperII.FindCenterlineIndex(weaponManager);
            if (centerlineIndex < 0 || centerlineIndex >= weaponManager.hardpointSets.Length)
            {
                return null;
            }

            HardpointSet centerline = weaponManager.hardpointSets[centerlineIndex];
            HardpointSet[] hardpointSets = weaponManager.hardpointSets;
            ServerValidationState state = new ServerValidationState
            {
                Centerline = centerline,
                CenterlineIndex = centerlineIndex,
                HardpointSets = hardpointSets,
                WeaponOptions = new List<WeaponMount>[hardpointSets.Length],
                SelectedMounts = new WeaponMount[hardpointSets.Length]
            };
            for (int i = 0; i < hardpointSets.Length; i++)
            {
                HardpointSet set = hardpointSets[i];
                if (set == null)
                {
                    continue;
                }
                state.WeaponOptions[i] = set.weaponOptions;
                state.SelectedMounts[i] = set.weaponMount;
                set.weaponOptions = F16ViperII.BuildServerValidationOptions(set);
            }
            ConfigureCenterline(centerline);

            if (!loggedServerValidation && requestedLoadout?.weapons != null)
            {
                int customWeapons = 0;
                bool jammer = false;
                bool sixJdam = false;
                for (int i = 0; i < requestedLoadout.weapons.Count; i++)
                {
                    WeaponMount requested = requestedLoadout.weapons[i];
                    customWeapons += F16ViperII.IsViperWeaponPrefab(requested) ? 1 : 0;
                    jammer |= IsViperJammer(requested);
                    sixJdam |= requested == F16ViperII.SixJdamMountForValidation;
                }
                if (customWeapons > 0 || jammer)
                {
                    loggedServerValidation = true;
                    Plugin.ModLogger?.LogInfo(
                        "F-16VX Viper II server validation exposed " + customWeapons +
                        " custom weapon prefab(s)" +
                        (sixJdam ? ", including the permanent GBU-38 x6," : string.Empty) +
                        (jammer ? " and a centerline jammer." : "."));
                }
            }
            return state;
        }

        internal static void EndServerValidation(ServerValidationState state)
        {
            if (state?.HardpointSets == null || state.WeaponOptions == null ||
                state.SelectedMounts == null)
            {
                return;
            }
            for (int i = 0; i < state.HardpointSets.Length; i++)
            {
                HardpointSet set = state.HardpointSets[i];
                if (set == null)
                {
                    continue;
                }
                set.weaponOptions = state.WeaponOptions[i];
                set.weaponMount = state.SelectedMounts[i];
            }
        }

        internal static bool ValidateRegistration(Encyclopedia encyclopedia, out string failure)
        {
            failure = string.Empty;
            if (Mounts.Count != Specs.Length || templateMount == null)
            {
                failure = "the three jammer mounts were not registered";
                return false;
            }
            if (JammingPodRangeFalloff == null || JammingPodDirectionTransform == null)
            {
                failure = "JammingPod range or direction fields are unavailable for ECM coupling";
                return false;
            }
            if (HardpointSpawnedPrefab == null)
            {
                failure = "Hardpoint.spawnedPrefab is unavailable for live jammer recovery";
                return false;
            }
            if (PowerSupplyCharge == null || PowerSupplyDrawn == null ||
                PowerSupplyRequested == null || PowerSupplyChargePerRpm == null)
            {
                failure = "PowerSupply fields are unavailable for ECM economy/recharge validation";
                return false;
            }
            if (RadarJammerPowerUsage == null || RadarJammerIntensity == null ||
                RadarJammerCurrentIntensity == null)
            {
                failure = "RadarJammer power/effect fields are unavailable for OJS validation";
                return false;
            }

            HashSet<string> keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<WeaponInfo> infos = new HashSet<WeaponInfo>();
            HashSet<GameObject> prefabs = new HashSet<GameObject>();
            for (int i = 0; i < Specs.Length; i++)
            {
                WeaponMount mount = Mounts[i];
                JammerSpec spec = Specs[i];
                JammingPod prefabPod = mount?.prefab?
                    .GetComponentInChildren<JammingPod>(true);
                if (mount == null || !keys.Add(mount.jsonKey) ||
                    !string.Equals(mount.jsonKey, spec.Key, StringComparison.Ordinal) ||
                    !string.Equals(mount.mountName, spec.DisplayName, StringComparison.Ordinal) ||
                    !Mathf.Approximately(mount.emptyCost, PurchaseCost) ||
                    !Mathf.Approximately(mount.emptyMass, templateMount.mass * MassScale) ||
                    !Mathf.Approximately(mount.mass, templateMount.mass * MassScale) ||
                    !Mathf.Approximately(mount.emptyDrag, templateMount.emptyDrag * DragScale) ||
                    !Mathf.Approximately(mount.drag, templateMount.drag * DragScale) ||
                    mount.prefab == null || mount.prefab == templateMount.prefab ||
                    !prefabs.Add(mount.prefab) || !mount.prefab.name.StartsWith(spec.Key) ||
                    mount.prefab.activeSelf || prefabPod == null || prefabPod.info != mount.info ||
                    mount.info == null ||
                    mount.info == templateMount.info || !infos.Add(mount.info) ||
                    !string.Equals(mount.info.weaponName, spec.DisplayName, StringComparison.Ordinal) ||
                    encyclopedia?.weaponMounts == null || !encyclopedia.weaponMounts.Contains(mount))
                {
                    failure = "identity, name, $27m cost, scaled mass/drag, unique prefab, or WeaponInfo differs for " +
                              spec.Code;
                    return false;
                }
            }
            return true;
        }

        internal static bool ValidateCenterline(HardpointSet centerline, out string failure)
        {
            failure = string.Empty;
            if (centerline?.weaponOptions == null ||
                centerline.weaponOptions.Count != Specs.Length)
            {
                failure = "the Viper II centerline does not expose exactly three jammer suites";
                return false;
            }
            for (int i = 0; i < Specs.Length; i++)
            {
                if (centerline.weaponOptions[i] != Mounts[i] ||
                    !Mathf.Approximately(centerline.weaponOptions[i].emptyCost, PurchaseCost))
                {
                    failure = "the Viper II centerline jammer order or cost is incorrect";
                    return false;
                }
            }
            return true;
        }

        internal static bool ValidateLookups(Encyclopedia encyclopedia, out string failure)
        {
            failure = string.Empty;
            if (Encyclopedia.WeaponLookup == null || encyclopedia?.IndexLookup == null)
            {
                failure = "the encyclopedia weapon or network lookup is unavailable";
                return false;
            }
            foreach (WeaponMount mount in Mounts)
            {
                if (!Encyclopedia.WeaponLookup.TryGetValue(mount.jsonKey, out WeaponMount lookup) ||
                    lookup != mount || !encyclopedia.IndexLookup.Contains(mount))
                {
                    failure = "save or network lookup registration failed for " + mount.jsonKey;
                    return false;
                }
            }
            return true;
        }

        internal static bool SmokeValidateSpawnedWeaponInfo(out string failure)
        {
            failure = string.Empty;
            foreach (WeaponMount mount in Mounts)
            {
                GameObject probe = null;
                try
                {
                    probe = UnityEngine.Object.Instantiate(mount.prefab);
                    Weapon weapon = probe.GetComponentInChildren<Weapon>(true);
                    ConfigureSpawnedWeapon(weapon, mount);
                    ScaleSpawnedMount(probe, mount);
                    if (weapon == null || weapon.info != mount.info ||
                        !string.Equals(
                            weapon.info.weaponName,
                            mount.mountName,
                            StringComparison.Ordinal) ||
                        !HasExpectedScale(probe, mount))
                    {
                        failure = "spawned station identity or 72%-radial/90%-length scale differs for " +
                                  mount.jsonKey;
                        return false;
                    }
                }
                finally
                {
                    if (probe != null)
                    {
                        UnityEngine.Object.Destroy(probe);
                    }
                }
            }
            return true;
        }

        internal static bool SmokeValidateServerVetting(
            AircraftDefinition definition,
            out string failure)
        {
            failure = string.Empty;
            Aircraft sourceAircraft = definition?.unitPrefab?.GetComponent<Aircraft>() ??
                                      definition?.unitPrefab?.GetComponentInChildren<Aircraft>(true);
            WeaponManager sourceManager = sourceAircraft?.weaponManager;
            int centerlineIndex = F16ViperII.FindCenterlineIndex(sourceManager);
            if (centerlineIndex < 0 || Mounts.Count == 0)
            {
                failure = "the shared source centerline or jammer registry is unavailable";
                return false;
            }

            HardpointSet centerline = sourceManager.hardpointSets[centerlineIndex];
            List<WeaponMount> originalOptions = centerline.weaponOptions;
            WeaponMount originalSelection = centerline.weaponMount;
            int innerWingIndex = -1;
            for (int i = 0; i < sourceManager.hardpointSets.Length; i++)
            {
                if (F16ViperII.IsInnerWingPylon(sourceManager.hardpointSets[i]))
                {
                    innerWingIndex = i;
                    break;
                }
            }
            WeaponMount sixJdamMount = F16ViperII.SixJdamMountForValidation;
            if (innerWingIndex < 0 || sixJdamMount == null)
            {
                failure = "the inner-wing pylon or permanent GBU-38 x6 was unavailable for server vetting";
                return false;
            }
            List<WeaponMount> originalInnerWingOptions =
                sourceManager.hardpointSets[innerWingIndex].weaponOptions;
            Loadout requestedLoadout = new Loadout
            {
                weapons = new List<WeaponMount>()
            };
            for (int i = 0; i < sourceManager.hardpointSets.Length; i++)
            {
                requestedLoadout.weapons.Add(
                    i == centerlineIndex ? Mounts[0] :
                    i == innerWingIndex ? sixJdamMount : null);
            }

            ServerValidationState state = null;
            bool accepted;
            try
            {
                state = BeginServerValidation(definition, requestedLoadout);
                accepted = state != null && state.CenterlineIndex == centerlineIndex &&
                           WeaponChecker.MountAllowedHardpoint(Mounts[0], centerline) &&
                           WeaponChecker.MountAllowedHardpoint(
                               sixJdamMount,
                               sourceManager.hardpointSets[innerWingIndex]);
            }
            finally
            {
                EndServerValidation(state);
            }

            if (!accepted || !ReferenceEquals(centerline.weaponOptions, originalOptions) ||
                centerline.weaponMount != originalSelection || originalOptions.Contains(Mounts[0]))
            {
                failure = "the server validator rejected the permanent GBU-38 x6/jammer or leaked Viper II " +
                          "options into the King Viper";
                return false;
            }
            if (!ReferenceEquals(
                    sourceManager.hardpointSets[innerWingIndex].weaponOptions,
                    originalInnerWingOptions) ||
                originalInnerWingOptions.Contains(sixJdamMount))
            {
                failure = "the server-validation bridge leaked the permanent GBU-38 x6 into the King Viper";
                return false;
            }
            for (int i = 0; i < sourceManager.hardpointSets.Length; i++)
            {
                HardpointSet set = sourceManager.hardpointSets[i];
                if (set != null && state != null &&
                    (!ReferenceEquals(set.weaponOptions, state.WeaponOptions[i]) ||
                     set.weaponMount != state.SelectedMounts[i]))
                {
                    failure = "the server-validation bridge did not restore hardpoint " + i;
                    return false;
                }
            }
            return true;
        }

        internal static bool SmokeValidatePhysicalCenterlineMount(
            Aircraft aircraft,
            HardpointSet centerline,
            out string failure)
        {
            failure = string.Empty;
            if (aircraft == null || centerline?.hardpoints == null ||
                centerline.hardpoints.Count == 0 || Mounts.Count == 0)
            {
                failure = "the Viper II aircraft, centerline hardpoint, or jammer registry is unavailable";
                return false;
            }

            WeaponMount mount = Mounts[1];
            int originalStationCount = aircraft.weaponStations.Count;
            int centerlineIndex = Array.IndexOf(
                aircraft.weaponManager.hardpointSets,
                centerline);
            if (centerlineIndex < 0)
            {
                failure = "the Viper II runtime loadout has no centerline slot";
                return false;
            }
            Loadout originalLoadout = aircraft.loadout;
            List<WeaponMount> originalWeaponList = originalLoadout?.weapons;
            if (aircraft.loadout == null)
            {
                aircraft.loadout = new Loadout
                {
                    weapons = new List<WeaponMount>()
                };
            }
            else if (aircraft.loadout.weapons == null)
            {
                aircraft.loadout.weapons = new List<WeaponMount>();
            }
            int originalLoadoutCount = aircraft.loadout.weapons.Count;
            while (aircraft.loadout.weapons.Count <= centerlineIndex)
            {
                aircraft.loadout.weapons.Add(null);
            }
            WeaponMount originalLoadoutMount = aircraft.loadout.weapons[centerlineIndex];
            JammingPod mountedPod = null;
            try
            {
                foreach (int passiveIndex in new[] { 0, 2 })
                {
                    WeaponMount passiveMount = Mounts[passiveIndex];
                    aircraft.loadout.weapons[centerlineIndex] = passiveMount;
                    int passivePods = EnsureRuntimeDeployment(aircraft, forceSpawn: true);
                    JammingPod passivePod = FindPhysicalPod(centerline, passiveMount.info);
                    F16ViperIIJammerController passiveController =
                        aircraft.GetComponent<F16ViperIIJammerController>();
                    if (passivePods != 1 || passivePod == null || passivePod.enabled ||
                        (passiveController?.RegisteredChannelCount ?? 0) != 0 ||
                        ActivatePodsFromEcm(aircraft) != 0)
                    {
                        failure = passiveMount.mountName +
                            " gained a Unit.Jam target channel despite being non-jamming";
                        return false;
                    }
                    centerline.RemoveMounts();
                }
                aircraft.loadout.weapons[centerlineIndex] = null;

                RadarJammer radarEcm = aircraft.GetComponentInChildren<RadarJammer>(true);
                PowerSupply powerSupply = aircraft.GetPowerSupply();
                if (radarEcm == null || powerSupply == null || HasEquippedOjs(aircraft))
                {
                    failure = "the unladen Viper II ECM baseline was unavailable for validation";
                    return false;
                }
                float actualPowerUsage = (float)RadarJammerPowerUsage.GetValue(radarEcm);
                float actualJammingIntensity =
                    (float)RadarJammerIntensity.GetValue(radarEcm);
                powerSupply.SetFullyCharged();
                float baselineChargeBefore = powerSupply.GetChargeKJ();
                float baselineRequestedBefore =
                    (float)PowerSupplyRequested.GetValue(powerSupply);
                radarEcm.Fire();
                float expectedBaselineCharge = Mathf.Max(
                    0f,
                    baselineChargeBefore -
                    actualPowerUsage * EcmConsumptionScale * Time.deltaTime);
                float actualCurrentIntensity =
                    (float)RadarJammerCurrentIntensity.GetValue(radarEcm);
                float baselineRequestedDelta =
                    (float)PowerSupplyRequested.GetValue(powerSupply) -
                    baselineRequestedBefore;
                if (actualCurrentIntensity <= 0f ||
                    !Mathf.Approximately(
                        baselineRequestedDelta,
                        actualPowerUsage * EcmConsumptionScale) ||
                    !Mathf.Approximately(powerSupply.GetChargeKJ(), expectedBaselineCharge))
                {
                    failure = "the Viper II Radar ECM did not consume 160% of stock charge at full output " +
                        "(before=" + baselineChargeBefore + ", after=" +
                        powerSupply.GetChargeKJ() + ", expected=" + expectedBaselineCharge +
                        ", request=" + actualPowerUsage + ", intensity=" +
                        actualCurrentIntensity + "/" + actualJammingIntensity +
                        ", effectiveRequest=" + baselineRequestedDelta + ")";
                    return false;
                }

                WeaponMount adjMount = Mounts[0];
                aircraft.loadout.weapons[centerlineIndex] = adjMount;
                int adjPods = EnsureRuntimeDeployment(aircraft, forceSpawn: true);
                powerSupply.SetFullyCharged();
                float adjChargeBefore = powerSupply.GetChargeKJ();
                float adjRequestedBefore =
                    (float)PowerSupplyRequested.GetValue(powerSupply);
                radarEcm.Fire();
                float expectedAdjCharge = Mathf.Max(
                    0f,
                    adjChargeBefore - actualPowerUsage * EcmConsumptionScale *
                    AdjConsumptionMultiplier * Time.deltaTime);
                float adjRequestedDelta =
                    (float)PowerSupplyRequested.GetValue(powerSupply) -
                    adjRequestedBefore;
                actualCurrentIntensity =
                    (float)RadarJammerCurrentIntensity.GetValue(radarEcm);
                float originalChargePerRpm =
                    (float)PowerSupplyChargePerRpm.GetValue(powerSupply);
                AdjRechargeState rechargeState = BeginAdjRecharge(powerSupply);
                float boostedChargePerRpm =
                    (float)PowerSupplyChargePerRpm.GetValue(powerSupply);
                EndAdjRecharge(powerSupply, ref rechargeState);
                float restoredChargePerRpm =
                    (float)PowerSupplyChargePerRpm.GetValue(powerSupply);
                if (adjPods != 1 || actualCurrentIntensity <= 0f ||
                    originalChargePerRpm <= 0f ||
                    !Mathf.Approximately(
                        adjRequestedDelta,
                        actualPowerUsage * EcmConsumptionScale *
                        AdjConsumptionMultiplier) ||
                    !Mathf.Approximately(powerSupply.GetChargeKJ(), expectedAdjCharge) ||
                    !Mathf.Approximately(
                        boostedChargePerRpm,
                        originalChargePerRpm * AdjRechargeMultiplier) ||
                    !Mathf.Approximately(restoredChargePerRpm, originalChargePerRpm))
                {
                    failure = "ADJ-94 did not apply 70% ECM consumption and 130% capacitor recharge";
                    return false;
                }
                centerline.RemoveMounts();
                aircraft.loadout.weapons[centerlineIndex] = null;

                aircraft.loadout.weapons[centerlineIndex] = mount;
                int recoveredPods = EnsureRuntimeDeployment(aircraft, forceSpawn: true);
                if (recoveredPods != 1 || centerline.weaponMount != mount)
                {
                    failure = "authoritative ECM recovery did not force the selected centerline jammer";
                    return false;
                }
                foreach (Hardpoint hardpoint in centerline.hardpoints)
                {
                    if (hardpoint == null || hardpoint.GetMount() != mount ||
                        hardpoint.transform == null)
                    {
                        failure = "a physical centerline hardpoint did not mount the jammer";
                        return false;
                    }
                    Weapon[] weapons = hardpoint.transform.GetComponentsInChildren<Weapon>(true);
                    bool foundIdentity = false;
                    bool foundScaledMount = false;
                    foreach (Weapon weapon in weapons)
                    {
                        if (weapon != null && weapon.info == mount.info)
                        {
                            foundIdentity = true;
                            mountedPod = weapon as JammingPod;
                            break;
                        }
                    }
                    for (int i = 0; i < hardpoint.transform.childCount; i++)
                    {
                        GameObject child = hardpoint.transform.GetChild(i).gameObject;
                        Weapon childWeapon = child.GetComponentInChildren<Weapon>(true);
                        if (childWeapon != null && childWeapon.info == mount.info &&
                            HasExpectedScale(child, mount))
                        {
                            foundScaledMount = true;
                            break;
                        }
                    }
                    if (!foundIdentity)
                    {
                        failure = "the physical jammer spawned without its Viper II pod identity";
                        return false;
                    }
                    if (!foundScaledMount)
                    {
                        failure = "the physical jammer did not spawn at 72% radial and 90% length scale";
                        return false;
                    }
                }
                if (mountedPod == null || aircraft.weaponStations.Count != originalStationCount ||
                    IsRegisteredWeapon(aircraft, mountedPod))
                {
                    failure = "the ECM pod was registered as a selectable weapon station";
                    return false;
                }
                F16ViperIIJammerController controller =
                    aircraft.GetComponent<F16ViperIIJammerController>();
                if (controller == null || controller.RegisteredPodCount != 1 ||
                    controller.RegisteredChannelCount != TargetChannelsPerPod ||
                    !controller.HasValidPodConfiguration ||
                    ActivatePodsFromEcm(aircraft) != 1 || !controller.IsPulseActive)
                {
                    failure = "the fitted pod did not register with the aircraft-side ECM controller";
                    return false;
                }
                Unit proofTarget = aircraft.definition?.unitPrefab?
                    .GetComponent<Unit>() ?? aircraft.definition?.unitPrefab?
                    .GetComponentInChildren<Unit>(true);
                bool proofStarted = BeginJamDispatchProof(
                    aircraft,
                    proofTarget,
                    out string jamProofFailure);
                bool proofDispatched = false;
                bool proofPassed = false;
                if (proofStarted)
                {
                    try
                    {
                        proofDispatched = controller.SmokeDispatchSingleChannel(
                            proofTarget);
                    }
                    finally
                    {
                        proofPassed = CompleteJamDispatchProof(out jamProofFailure);
                    }
                }
                if (!proofStarted || !proofDispatched || !proofPassed)
                {
                    failure = "the fitted pod failed its single-channel Unit.Jam proof: " +
                        jamProofFailure;
                    return false;
                }
                powerSupply.SetFullyCharged();
                float chargeBefore = powerSupply.GetChargeKJ();
                controller.SmokeTickNow();
                if (mountedPod.enabled ||
                    !Mathf.Approximately(powerSupply.GetChargeKJ(), chargeBefore))
                {
                    failure = "the aircraft-side controller ran the vanilla pod or consumed pod electricity";
                    return false;
                }
                controller.Stop();
                if (controller.IsPulseActive || mountedPod.GetTarget() != null)
                {
                    failure = "the aircraft-side jammer controller did not stop with Radar ECM";
                    return false;
                }
                powerSupply.SetFullyCharged();
                float ojsChargeBefore = powerSupply.GetChargeKJ();
                float ojsRequestedBefore =
                    (float)PowerSupplyRequested.GetValue(powerSupply);
                radarEcm.Fire();
                float expectedOjsCharge = Mathf.Max(
                    0f,
                    ojsChargeBefore - actualPowerUsage * EcmConsumptionScale *
                    OjsConsumptionMultiplier * Time.deltaTime);
                actualCurrentIntensity =
                    (float)RadarJammerCurrentIntensity.GetValue(radarEcm);
                float ojsRequestedDelta =
                    (float)PowerSupplyRequested.GetValue(powerSupply) -
                    ojsRequestedBefore;
                if (actualPowerUsage <= 0f ||
                    !Mathf.Approximately(actualCurrentIntensity, actualJammingIntensity) ||
                    !Mathf.Approximately(
                        ojsRequestedDelta,
                        actualPowerUsage * EcmConsumptionScale *
                        OjsConsumptionMultiplier) ||
                    !Mathf.Approximately(powerSupply.GetChargeKJ(), expectedOjsCharge))
                {
                    failure = "the real OJS RadarJammer.Fire path tapered above zero charge";
                    return false;
                }

                PowerSupplyCharge.SetValue(powerSupply, 0.001f);
                aircraft.radar.activated = true;
                radarEcm.Fire();
                actualCurrentIntensity =
                    (float)RadarJammerCurrentIntensity.GetValue(radarEcm);
                if (!Mathf.Approximately(actualCurrentIntensity, actualJammingIntensity) ||
                    powerSupply.GetChargeKJ() != 0f || aircraft.radar.activated ||
                    !IsOjsRechargeLocked(aircraft))
                {
                    failure = "OJS did not hold full ECM output through depletion and shut down radar";
                    return false;
                }

                PowerSupplyCharge.SetValue(powerSupply, OjsRechargeThreshold - 1f);
                aircraft.radar.activated = true;
                radarEcm.Fire();
                actualCurrentIntensity =
                    (float)RadarJammerCurrentIntensity.GetValue(radarEcm);
                if (actualCurrentIntensity != 0f ||
                    !Mathf.Approximately(
                        powerSupply.GetChargeKJ(),
                        OjsRechargeThreshold - 1f) ||
                    !IsOjsRechargeLocked(aircraft) || controller.IsPulseActive)
                {
                    failure = "OJS Radar ECM was usable before recharging to 300 kJ";
                    return false;
                }

                PowerSupplyCharge.SetValue(powerSupply, OjsRechargeThreshold);
                radarEcm.Fire();
                float expectedRestartCharge = Mathf.Max(
                    0f,
                    OjsRechargeThreshold - actualPowerUsage * EcmConsumptionScale *
                    OjsConsumptionMultiplier * Time.deltaTime);
                actualCurrentIntensity =
                    (float)RadarJammerCurrentIntensity.GetValue(radarEcm);
                if (!Mathf.Approximately(actualCurrentIntensity, actualJammingIntensity) ||
                    !Mathf.Approximately(powerSupply.GetChargeKJ(), expectedRestartCharge) ||
                    IsOjsRechargeLocked(aircraft) || !controller.IsPulseActive)
                {
                    failure = "OJS Radar ECM did not unlock at 300 kJ";
                    return false;
                }
                return true;
            }
            finally
            {
                centerline.RemoveMounts();
                aircraft.loadout.weapons[centerlineIndex] = originalLoadoutMount;
                if (aircraft.loadout.weapons.Count > originalLoadoutCount)
                {
                    aircraft.loadout.weapons.RemoveRange(
                        originalLoadoutCount,
                        aircraft.loadout.weapons.Count - originalLoadoutCount);
                }
                if (originalLoadout == null)
                {
                    aircraft.loadout = null;
                }
                else
                {
                    originalLoadout.weapons = originalWeaponList;
                    aircraft.loadout = originalLoadout;
                }
            }
        }

        private static bool HasExpectedScale(GameObject spawnedMount, WeaponMount mount)
        {
            if (spawnedMount == null || mount?.prefab == null)
            {
                return false;
            }
            Vector3 expected = Vector3.Scale(
                mount.prefab.transform.localScale,
                new Vector3(RadialScale, RadialScale, LengthScale));
            return (spawnedMount.transform.localScale - expected).sqrMagnitude < 0.000001f;
        }

        private static JammingPod FindPhysicalPod(
            HardpointSet hardpointSet,
            WeaponInfo info)
        {
            if (hardpointSet?.hardpoints == null || info == null)
            {
                return null;
            }
            foreach (Hardpoint hardpoint in hardpointSet.hardpoints)
            {
                GameObject spawnedPrefab = HardpointSpawnedPrefab?.GetValue(hardpoint) as GameObject;
                foreach (JammingPod pod in spawnedPrefab?
                             .GetComponentsInChildren<JammingPod>(true) ??
                         Array.Empty<JammingPod>())
                {
                    if (pod != null && pod.info == info)
                    {
                        return pod;
                    }
                }
            }
            return null;
        }

        internal static bool IsViperJammerInfo(WeaponInfo info)
        {
            if (info == null)
            {
                return false;
            }
            foreach (WeaponMount mount in Mounts)
            {
                if (mount != null && mount.info == info)
                {
                    return true;
                }
            }
            return false;
        }

        internal static bool IsOjsPodInfo(WeaponInfo info)
        {
            return info != null && Mounts.Count > 1 && Mounts[1]?.info == info;
        }

        private static bool HasEquippedAdj(Aircraft aircraft)
        {
            if (aircraft == null)
            {
                return false;
            }
            bool hasAuthoritativeSelection = false;
            if (aircraft.loadout?.weapons != null)
            {
                hasAuthoritativeSelection = true;
                foreach (WeaponMount mount in aircraft.loadout.weapons)
                {
                    if (IsAdjMount(mount))
                    {
                        return true;
                    }
                }
            }
            if (aircraft.weaponManager?.hardpointSets != null)
            {
                hasAuthoritativeSelection = true;
                foreach (HardpointSet set in aircraft.weaponManager.hardpointSets)
                {
                    if (IsAdjMount(set?.weaponMount))
                    {
                        return true;
                    }
                }
            }
            if (hasAuthoritativeSelection)
            {
                return false;
            }
            if (Mounts.Count == 0 || Mounts[0]?.info == null)
            {
                return false;
            }
            foreach (JammingPod pod in aircraft.GetComponentsInChildren<JammingPod>(true))
            {
                if (pod != null && pod.attachedUnit == aircraft && pod.info == Mounts[0].info)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool HasEquippedOjs(Aircraft aircraft)
        {
            if (aircraft == null)
            {
                return false;
            }

            bool hasAuthoritativeSelection = false;
            if (aircraft.loadout?.weapons != null)
            {
                hasAuthoritativeSelection = true;
                foreach (WeaponMount mount in aircraft.loadout.weapons)
                {
                    if (IsOjsMount(mount))
                    {
                        return true;
                    }
                }
            }
            if (aircraft.weaponManager?.hardpointSets != null)
            {
                hasAuthoritativeSelection = true;
                foreach (HardpointSet set in aircraft.weaponManager.hardpointSets)
                {
                    if (IsOjsMount(set?.weaponMount))
                    {
                        return true;
                    }
                }
            }
            if (hasAuthoritativeSelection)
            {
                return false;
            }
            if (Mounts.Count < 2 || Mounts[1]?.info == null)
            {
                return false;
            }
            foreach (JammingPod pod in aircraft.GetComponentsInChildren<JammingPod>(true))
            {
                if (pod != null && pod.attachedUnit == aircraft && pod.info == Mounts[1].info)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsOjsMount(WeaponMount mount)
        {
            return mount != null &&
                   string.Equals(mount.jsonKey, OjsMountKey, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsAdjMount(WeaponMount mount)
        {
            return mount != null &&
                   string.Equals(mount.jsonKey, AdjMountKey, StringComparison.OrdinalIgnoreCase);
        }

        internal static bool IsOjsRechargeLocked(Aircraft aircraft)
        {
            F16ViperIIOjsRechargeLock rechargeLock =
                aircraft?.GetComponent<F16ViperIIOjsRechargeLock>();
            if (rechargeLock == null || !rechargeLock.Locked)
            {
                return false;
            }
            PowerSupply powerSupply = aircraft.GetPowerSupply();
            if (powerSupply != null && powerSupply.GetChargeKJ() >= OjsRechargeThreshold)
            {
                rechargeLock.Locked = false;
            }
            return rechargeLock.Locked;
        }

        private static void ResetOjsRechargeLock(Aircraft aircraft)
        {
            F16ViperIIOjsRechargeLock rechargeLock =
                aircraft?.GetComponent<F16ViperIIOjsRechargeLock>();
            if (rechargeLock != null)
            {
                rechargeLock.Locked = false;
            }
        }

        private static void SetOjsRechargeLocked(Aircraft aircraft)
        {
            if (aircraft == null)
            {
                return;
            }
            F16ViperIIOjsRechargeLock rechargeLock =
                aircraft.GetComponent<F16ViperIIOjsRechargeLock>() ??
                aircraft.gameObject.AddComponent<F16ViperIIOjsRechargeLock>();
            rechargeLock.Locked = true;
        }

        private static void DisableViperJammerPods(Aircraft aircraft)
        {
            aircraft?.GetComponent<F16ViperIIJammerController>()?.Stop();
        }

        private static void HandleOjsPowerState(Aircraft aircraft, float charge)
        {
            if (aircraft == null)
            {
                return;
            }
            if (charge > 0f)
            {
                return;
            }
            SetOjsRechargeLocked(aircraft);
            DisableViperJammerPods(aircraft);
            if (aircraft.radar == null || !aircraft.radar.activated)
            {
                return;
            }

            if (aircraft.countermeasureTrigger && aircraft.countermeasureManager != null)
            {
                aircraft.Countermeasures(active: false, aircraft.countermeasureManager.activeIndex);
            }
            bool networkSpawned = aircraft.Identity != null && aircraft.Identity.IsSpawned;
            if (networkSpawned && aircraft.IsServer)
            {
                aircraft.UserCode_CmdToggleRadar_1821461427();
                aircraft.radar.activated = false;
                return;
            }
            if (networkSpawned)
            {
                return;
            }

            aircraft.radar.activated = false;
            ReportLowPower(aircraft);
        }

        internal static bool TryHandleOjsLowPowerRadarRpc(Aircraft aircraft, bool activated)
        {
            if (activated || aircraft?.radar == null || !HasEquippedOjs(aircraft))
            {
                return false;
            }
            PowerSupply powerSupply = aircraft.GetPowerSupply();
            if (powerSupply == null || powerSupply.GetChargeKJ() > 0.01f)
            {
                return false;
            }
            aircraft.radar.activated = false;
            ReportLowPower(aircraft);
            return true;
        }

        private static void ReportLowPower(Aircraft aircraft)
        {
            if (SceneSingleton<CombatHUD>.i?.aircraft != aircraft ||
                SceneSingleton<AircraftActionsReport>.i == null)
            {
                return;
            }
            SceneSingleton<AircraftActionsReport>.i.ReportText("<b>LOW POWER</b>", 5f);
        }

        private static bool IsRegisteredWeapon(Aircraft aircraft, Weapon weapon)
        {
            foreach (WeaponStation station in aircraft.weaponStations)
            {
                if (station?.Weapons != null && station.Weapons.Contains(weapon))
                {
                    return true;
                }
            }
            return false;
        }

        internal static EcmTargetSelection SelectEcmTarget(
            Aircraft aircraft,
            JammingPod pod,
            Unit current,
            bool allowLiveRegistryScan)
        {
            float maxRange = GetPodMaximumRange(pod);
            if (TrySelectInboundMissileThreat(aircraft, maxRange, out EcmTargetSelection threat))
            {
                return threat;
            }
            if (IsValidEcmTarget(aircraft, current, maxRange))
            {
                return new EcmTargetSelection { Target = current };
            }

            List<Unit> designatedTargets = aircraft.weaponManager?.GetTargetList();
            if (designatedTargets != null)
            {
                foreach (Unit target in designatedTargets)
                {
                    if (IsValidEcmTarget(aircraft, target, maxRange))
                    {
                        return new EcmTargetSelection { Target = target };
                    }
                }
            }

            Unit closest = null;
            float closestDistanceSquared = float.PositiveInfinity;
            if (aircraft.NetworkHQ == null)
            {
                return default;
            }
            foreach (KeyValuePair<PersistentID, TrackingInfo> entry in
                     aircraft.NetworkHQ.trackingDatabase)
            {
                if (!entry.Value.TryGetUnit(out Unit candidate) ||
                    !IsValidEcmTarget(aircraft, candidate, maxRange))
                {
                    continue;
                }
                float distanceSquared =
                    (candidate.transform.position - aircraft.transform.position).sqrMagnitude;
                if (distanceSquared < closestDistanceSquared)
                {
                    closest = candidate;
                    closestDistanceSquared = distanceSquared;
                }
            }
            if (closest != null)
            {
                return new EcmTargetSelection { Target = closest };
            }

            if (!allowLiveRegistryScan)
            {
                return default;
            }

            // A radiating threat is itself a passive detection source. Fall back to the live
            // unit registry so an emitter need not have a fresh four-second HQ track before a
            // Viper II defensive jammer can react to it.
            foreach (Unit candidate in UnitRegistry.allUnits)
            {
                if (!IsValidEcmTarget(aircraft, candidate, maxRange))
                {
                    continue;
                }
                float distanceSquared =
                    (candidate.transform.position - aircraft.transform.position).sqrMagnitude;
                if (distanceSquared < closestDistanceSquared)
                {
                    closest = candidate;
                    closestDistanceSquared = distanceSquared;
                }
            }
            return new EcmTargetSelection { Target = closest };
        }

        private static bool TrySelectInboundMissileThreat(
            Aircraft aircraft,
            float maxRange,
            out EcmTargetSelection selection)
        {
            selection = default;
            List<Missile> knownMissiles = aircraft?.GetMissileWarningSystem()?.knownMissiles;
            if (knownMissiles == null)
            {
                return false;
            }

            float closestDistanceSquared = float.PositiveInfinity;
            foreach (Missile missile in knownMissiles)
            {
                if (missile == null || missile.disabled ||
                    missile.targetID != aircraft.persistentID ||
                    (aircraft.NetworkHQ != null && missile.NetworkHQ == aircraft.NetworkHQ))
                {
                    continue;
                }

                string seekerType = missile.GetSeekerType();
                bool isSarh = string.Equals(
                    seekerType,
                    "SARH",
                    StringComparison.OrdinalIgnoreCase);
                bool isArh = string.Equals(
                    seekerType,
                    "ARH",
                    StringComparison.OrdinalIgnoreCase);
                if (!isSarh && !isArh)
                {
                    continue;
                }

                Unit jamTarget = missile;
                if (isSarh)
                {
                    jamTarget = missile.radar?.GetAttachedUnit();
                    if (!IsValidEcmTarget(aircraft, jamTarget, maxRange))
                    {
                        continue;
                    }
                }
                else if (!IsValidMissileJamTarget(aircraft, missile, maxRange))
                {
                    continue;
                }

                float distanceSquared =
                    (missile.transform.position - aircraft.transform.position).sqrMagnitude;
                if (distanceSquared >= closestDistanceSquared)
                {
                    continue;
                }
                closestDistanceSquared = distanceSquared;
                selection = new EcmTargetSelection
                {
                    Target = jamTarget,
                    ThreatMissile = missile,
                    IsMissileThreat = true,
                    IsSarhSource = isSarh
                };
            }
            return selection.Target != null;
        }

        private static bool IsValidMissileJamTarget(
            Aircraft aircraft,
            Missile missile,
            float maxRange)
        {
            if (aircraft == null || missile == null || missile.disabled ||
                missile.targetID != aircraft.persistentID ||
                (aircraft.NetworkHQ != null && missile.NetworkHQ == aircraft.NetworkHQ))
            {
                return false;
            }
            return maxRange <= 0f ||
                   (missile.transform.position - aircraft.transform.position).sqrMagnitude <=
                   maxRange * maxRange;
        }

        internal static float GetPodMaximumRange(JammingPod pod)
        {
            return pod?.info?.targetRequirements.maxRange ?? 0f;
        }

        internal static bool IsValidEcmTarget(Aircraft aircraft, Unit target, float maxRange)
        {
            if (aircraft?.NetworkHQ == null || target == null || target.disabled ||
                target == aircraft || target.NetworkHQ == null ||
                target.NetworkHQ == aircraft.NetworkHQ || !target.HasRadarEmission())
            {
                return false;
            }
            return maxRange <= 0f ||
                   (target.transform.position - aircraft.transform.position).sqrMagnitude <=
                   maxRange * maxRange;
        }

        internal static AnimationCurve GetPodRangeFalloff(JammingPod pod)
        {
            return pod == null || JammingPodRangeFalloff == null
                ? null
                : JammingPodRangeFalloff.GetValue(pod) as AnimationCurve;
        }

        internal static Transform GetPodDirectionTransform(JammingPod pod)
        {
            return pod == null || JammingPodDirectionTransform == null
                ? null
                : JammingPodDirectionTransform.GetValue(pod) as Transform;
        }

        internal static float PulseLifetime => EcmPulseLifetime;

        private static WeaponMount CreateMount(WeaponMount template, JammerSpec spec)
        {
            WeaponInfo info = UnityEngine.Object.Instantiate(template.info);
            info.name = spec.Key + "_Info";
            info.weaponName = spec.DisplayName;
            info.shortName = spec.Code;
            info.description = spec.Description;
            info.hideFlags = HideFlags.HideAndDontSave;

            bool templateWasActive = template.prefab.activeSelf;
            GameObject prefabObject;
            try
            {
                if (templateWasActive)
                {
                    template.prefab.SetActive(false);
                }
                prefabObject = UnityEngine.Object.Instantiate(template.prefab);
            }
            finally
            {
                if (templateWasActive)
                {
                    template.prefab.SetActive(true);
                }
            }
            prefabObject.name = spec.Key + "_Prefab";
            prefabObject.hideFlags = HideFlags.HideAndDontSave;
            foreach (Weapon weapon in prefabObject.GetComponentsInChildren<Weapon>(true))
            {
                weapon.info = info;
            }
            prefabObject.SetActive(false);
            UnityEngine.Object.DontDestroyOnLoad(prefabObject);

            WeaponMount mount = UnityEngine.Object.Instantiate(template);
            mount.name = spec.Key;
            mount.jsonKey = spec.Key;
            mount.mountName = spec.DisplayName;
            mount.info = info;
            mount.prefab = prefabObject;
            mount.emptyCost = PurchaseCost;
            mount.emptyMass = template.mass * MassScale;
            mount.mass = mount.emptyMass;
            mount.emptyDrag = template.emptyDrag * DragScale;
            mount.drag = template.drag * DragScale;
            mount.hideFlags = HideFlags.HideAndDontSave;
            mount.dontAutomaticallyAddToEncyclopedia = false;
            return mount;
        }
    }

    internal sealed class F16ViperIIJammerController : MonoBehaviour
    {
        private sealed class ChannelState
        {
            internal Unit Target;
            internal Missile ThreatMissile;
            internal bool IsSarhSource;
            internal float LastJammingTick;
            internal float RewardAmount;
            internal float RewardCount;
            internal float NextJamLog;
        }

        private sealed class PodState
        {
            internal JammingPod Pod;
            internal ChannelState[] Channels;
        }

        private const float JammingTickInterval = 0.2f;
        private const float LiveRegistryScanInterval = 1f;
        private const float DiagnosticLogInterval = 15f;
        private const float RewardThreshold = 60f;
        private readonly List<PodState> pods = new List<PodState>();
        private Aircraft aircraft;
        private float lastEcmPulse = float.NegativeInfinity;
        private float nextNoTargetLog;
        private float nextLineOfSightLog;
        private float nextControllerTick;
        private float nextLiveRegistryScan;
        private bool loggedPulseRoute;

        internal int RegisteredPodCount
        {
            get
            {
                RemoveInvalidPods();
                return pods.Count;
            }
        }

        internal int RegisteredChannelCount
        {
            get
            {
                RemoveInvalidPods();
                int count = 0;
                foreach (PodState state in pods)
                {
                    count += state.Channels?.Length ?? 0;
                }
                return count;
            }
        }

        internal bool IsPulseActive =>
            aircraft != null &&
            Time.timeSinceLevelLoad - lastEcmPulse <= F16ViperIIJammers.PulseLifetime &&
            !F16ViperIIJammers.IsOjsRechargeLocked(aircraft);

        internal bool HasValidPodConfiguration
        {
            get
            {
                RemoveInvalidPods();
                foreach (PodState state in pods)
                {
                    if (F16ViperIIJammers.GetPodRangeFalloff(state.Pod) == null ||
                        F16ViperIIJammers.GetPodDirectionTransform(state.Pod) == null)
                    {
                        return false;
                    }
                }
                return pods.Count > 0;
            }
        }

        private void Awake()
        {
            aircraft = GetComponent<Aircraft>();
        }

        internal void Register(JammingPod pod)
        {
            if (pod == null || !F16ViperIIJammers.IsOjsPodInfo(pod.info))
            {
                return;
            }
            aircraft = aircraft ?? GetComponent<Aircraft>();
            foreach (PodState state in pods)
            {
                if (state.Pod == pod)
                {
                    pod.enabled = false;
                    return;
                }
            }
            pod.enabled = false;
            ChannelState[] channels = new ChannelState[F16ViperIIJammers.TargetChannelsPerPod];
            for (int i = 0; i < channels.Length; i++)
            {
                channels[i] = new ChannelState
                {
                    LastJammingTick = Time.timeSinceLevelLoad
                };
            }
            pods.Add(new PodState
            {
                Pod = pod,
                Channels = channels
            });
            Plugin.ModLogger?.LogInfo(
                "F-16VX aircraft controller registered physical jammer pod " +
                (pod.info?.weaponName ?? pod.name) + " with " + channels.Length +
                " independent target " +
                (channels.Length == 1 ? "channel." : "channels."));
        }

        internal int Pulse()
        {
            RemoveInvalidPods();
            if (aircraft == null || pods.Count == 0)
            {
                return 0;
            }
            bool wasActive = IsPulseActive;
            lastEcmPulse = Time.timeSinceLevelLoad;
            if (!wasActive)
            {
                nextControllerTick = 0f;
                nextLiveRegistryScan = 0f;
            }
            if (!loggedPulseRoute)
            {
                loggedPulseRoute = true;
                Plugin.ModLogger?.LogInfo(
                    "F-16VX Radar ECM pulse reached " + pods.Count +
                    " aircraft-controlled jammer pod(s).");
            }
            return pods.Count;
        }

        internal void Stop()
        {
            lastEcmPulse = float.NegativeInfinity;
            nextControllerTick = 0f;
            nextLiveRegistryScan = 0f;
            foreach (PodState state in pods)
            {
                if (state.Pod == null)
                {
                    continue;
                }
                state.Pod.enabled = false;
                state.Pod.SetTarget(null);
                foreach (ChannelState channel in state.Channels)
                {
                    channel.Target = null;
                    channel.ThreatMissile = null;
                    channel.IsSarhSource = false;
                }
            }
        }

        internal void SmokeTickNow()
        {
            TickController();
        }

        internal bool SmokeDispatchSingleChannel(Unit target)
        {
            RemoveInvalidPods();
            if (pods.Count != 1 || pods[0].Channels == null ||
                pods[0].Channels.Length != F16ViperIIJammers.TargetChannelsPerPod)
            {
                return false;
            }
            ApplyJam(pods[0].Channels[0], target, 1f, 0, accountReward: false);
            return true;
        }

        private void FixedUpdate()
        {
            TickController();
        }

        private void OnDestroy()
        {
            Stop();
        }

        private void TickController()
        {
            RemoveInvalidPods();
            if (!IsPulseActive)
            {
                return;
            }
            float now = Time.timeSinceLevelLoad;
            if (now < nextControllerTick)
            {
                return;
            }
            nextControllerTick = now + JammingTickInterval;
            foreach (PodState state in pods)
            {
                TickPod(state);
            }
        }

        private void TickPod(PodState state)
        {
            JammingPod pod = state.Pod;
            pod.enabled = false;
            ChannelState channel = state.Channels[0];
            Unit previousTarget = channel.Target;
            Missile previousThreat = channel.ThreatMissile;
            float now = Time.timeSinceLevelLoad;
            bool allowLiveRegistryScan = now >= nextLiveRegistryScan;
            if (allowLiveRegistryScan)
            {
                nextLiveRegistryScan = now + LiveRegistryScanInterval;
            }
            F16ViperIIJammers.EcmTargetSelection selection =
                F16ViperIIJammers.SelectEcmTarget(
                    aircraft,
                    pod,
                    previousTarget,
                    allowLiveRegistryScan);
            channel.Target = selection.Target;
            channel.ThreatMissile = selection.ThreatMissile;
            channel.IsSarhSource = selection.IsSarhSource;
            if ((selection.Target != previousTarget ||
                 selection.ThreatMissile != previousThreat) && selection.Target != null)
            {
                if (selection.IsMissileThreat)
                {
                    Plugin.ModLogger?.LogInfo(
                        "F-16VX OJS prioritized inbound " +
                        selection.ThreatMissile.GetSeekerType() + " missile " +
                        TargetName(selection.ThreatMissile) +
                        (selection.IsSarhSource
                            ? "; jamming its illuminating radar source " +
                              TargetName(selection.Target) + "."
                            : "; jamming the missile directly."));
                }
                else
                {
                    bool freshHqTrack = aircraft.NetworkHQ != null &&
                                        aircraft.NetworkHQ.IsTargetBeingTracked(selection.Target);
                    Plugin.ModLogger?.LogInfo(
                        "F-16VX jammer channel 1 acquired emitting target " +
                        TargetName(selection.Target) +
                        (freshHqTrack ? " from an HQ track." :
                         " from passive radar emission."));
                }
            }
            TickChannel(state, channel, selection.Target, 0);
            pod.SetTarget(selection.Target);

            Transform directionTransform =
                F16ViperIIJammers.GetPodDirectionTransform(pod);
            Unit primaryTarget = selection.Target;
            Vector3 forward = primaryTarget != null
                ? primaryTarget.transform.position - pod.transform.position
                : pod.transform.forward;
            if (directionTransform != null && forward.sqrMagnitude > 0.000001f)
            {
                directionTransform.rotation = Quaternion.LookRotation(forward);
            }
            if (primaryTarget == null)
            {
                if (Time.timeSinceLevelLoad >= nextNoTargetLog)
                {
                    nextNoTargetLog = Time.timeSinceLevelLoad + DiagnosticLogInterval;
                    Plugin.ModLogger?.LogDebug(
                        "F-16VX jammer is coupled to Radar ECM but found no inbound ARH/SARH " +
                        "threat or hostile emitting radar in range.");
                }
            }
        }

        private void TickChannel(
            PodState podState,
            ChannelState channel,
            Unit target,
            int channelIndex)
        {
            if (target == null)
            {
                return;
            }
            JammingPod pod = podState.Pod;
            Vector3 forward = target.transform.position - pod.transform.position;
            Transform directionTransform =
                F16ViperIIJammers.GetPodDirectionTransform(pod);
            Transform scanPoint = target.radar != null
                ? target.radar.GetScanPoint()
                : target.transform;
            Vector3 lineOrigin = directionTransform != null
                ? directionTransform.position
                : pod.transform.position;
            if (scanPoint == null || Physics.Linecast(
                    lineOrigin,
                    scanPoint.position,
                    out _,
                    PhysicsLayers.StaticsMask))
            {
                if (Time.timeSinceLevelLoad >= nextLineOfSightLog)
                {
                    nextLineOfSightLog = Time.timeSinceLevelLoad + DiagnosticLogInterval;
                    Plugin.ModLogger?.LogDebug(
                        "F-16VX jammer channel " + (channelIndex + 1) +
                        " has no line of sight to " + TargetName(target) + ".");
                }
                return;
            }
            if (Time.timeSinceLevelLoad - channel.LastJammingTick < JammingTickInterval ||
                NetworkManagerNuclearOption.i == null ||
                !NetworkManagerNuclearOption.i.Server.Active)
            {
                return;
            }

            AnimationCurve rangeFalloff = F16ViperIIJammers.GetPodRangeFalloff(pod);
            if (rangeFalloff == null)
            {
                return;
            }
            float jamAmount = rangeFalloff.Evaluate(forward.magnitude);
            channel.LastJammingTick = Time.timeSinceLevelLoad;
            ApplyJam(channel, target, jamAmount, channelIndex, accountReward: true);
        }

        private void ApplyJam(
            ChannelState channel,
            Unit target,
            float jamAmount,
            int channelIndex,
            bool accountReward)
        {
            target.Jam(new Unit.JamEventArgs
            {
                jamAmount = jamAmount,
                jammingUnit = aircraft
            });
            if (accountReward && Time.timeSinceLevelLoad >= channel.NextJamLog)
            {
                channel.NextJamLog = Time.timeSinceLevelLoad + DiagnosticLogInterval;
                Plugin.ModLogger?.LogDebug(
                    "F-16VX jammer channel " + (channelIndex + 1) +
                    " applied Unit.Jam to " + TargetName(target) +
                    " at strength " + jamAmount.ToString("0.000") + ".");
            }

            if (accountReward && target.HasRadarEmission() && aircraft.Player != null &&
                target.NetworkHQ != null && target.NetworkHQ != aircraft.NetworkHQ)
            {
                channel.RewardCount += jamAmount * JammingTickInterval;
                channel.RewardAmount +=
                    0.0001f * jamAmount * Mathf.Sqrt(target.definition.value);
                if (channel.RewardCount > RewardThreshold)
                {
                    aircraft.NetworkHQ.ReportJammingAction(
                        aircraft.Player,
                        target,
                        channel.RewardAmount);
                    channel.RewardAmount = 0f;
                    channel.RewardCount = 0f;
                }
            }
        }

        private void RemoveInvalidPods()
        {
            aircraft = aircraft ?? GetComponent<Aircraft>();
            for (int i = pods.Count - 1; i >= 0; i--)
            {
                JammingPod pod = pods[i].Pod;
                if (pod == null || pod.attachedUnit != aircraft ||
                    !F16ViperIIJammers.IsOjsPodInfo(pod.info))
                {
                    pods.RemoveAt(i);
                }
            }
        }

        private static string TargetName(Unit target)
        {
            return target?.definition?.unitName ?? target?.name ?? "unknown target";
        }
    }

    internal sealed class F16ViperIIOjsRechargeLock : MonoBehaviour
    {
        internal bool Locked;
    }

    internal sealed class F16ViperIIPowerSupplyRegistration : MonoBehaviour
    {
        private PowerSupply powerSupply;

        internal void Initialize(PowerSupply registeredPowerSupply)
        {
            powerSupply = registeredPowerSupply;
        }

        private void OnDestroy()
        {
            F16ViperIIJammers.UnregisterViperPowerSupply(powerSupply);
            powerSupply = null;
        }
    }

    [HarmonyPatch(typeof(WeaponManager), nameof(WeaponManager.RegisterWeapon))]
    internal static class F16ViperIIJammerWeaponInfoPatch
    {
        private static bool Prefix(
            WeaponManager __instance,
            Weapon weapon,
            WeaponMount weaponMount,
            Hardpoint hardpoint)
        {
            return !F16ViperIIJammers.AttachAsEcmPod(
                __instance,
                weapon,
                weaponMount,
                hardpoint);
        }
    }

    [HarmonyPatch(typeof(RadarJammer), nameof(RadarJammer.Fire))]
    internal static class F16ViperIIJammerEcmActivationPatch
    {
        private static void Prefix(RadarJammer __instance, out bool __state)
        {
            __state = F16ViperIIJammers.BeginViperEcmPowerTick(__instance);
        }

        private static void Postfix(RadarJammer __instance)
        {
            F16ViperIIJammers.ActivatePodsFromEcm(__instance?.aircraft);
        }

        private static Exception Finalizer(bool __state, Exception __exception)
        {
            F16ViperIIJammers.EndViperEcmPowerTick(__state);
            return __exception;
        }
    }

    [HarmonyPatch]
    internal static class F16ViperIIOjsRadarNotificationPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(Aircraft),
                "UserCode_RpcToggleRadar_1325449311",
                new[] { typeof(bool) });
        }

        private static bool Prefix(Aircraft __instance, bool activated)
        {
            return !F16ViperIIJammers.TryHandleOjsLowPowerRadarRpc(
                __instance,
                activated);
        }
    }

    [HarmonyPatch]
    internal static class F16ViperIIJammerFixedUpdatePatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(JammingPod), "FixedUpdate", Type.EmptyTypes);
        }

        private static bool Prefix(JammingPod __instance)
        {
            return F16ViperIIJammers.RunOriginalPodUpdate(__instance);
        }
    }

    [HarmonyPatch]
    internal static class F16ViperIIJammerEcmLifetimePatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(JammingPod), "LateUpdate", Type.EmptyTypes);
        }

        private static bool Prefix(JammingPod __instance)
        {
            return F16ViperIIJammers.RunOriginalPodUpdate(__instance);
        }
    }

    [HarmonyPatch(typeof(PowerSupply), nameof(PowerSupply.DrawPower))]
    internal static class F16ViperIIJammerFreePowerPatch
    {
        private static bool Prefix(
            PowerSupply __instance,
            ref float powerRequested,
            ref float __result,
            out F16ViperIIJammers.EcmPowerDrawState __state)
        {
            __state = default;
            __state = F16ViperIIJammers.BeginViperEcmPowerDraw(
                __instance,
                ref powerRequested);
            if (__state.Blocked)
            {
                __result = 0f;
                return false;
            }
            return true;
        }

        private static void Postfix(
            PowerSupply __instance,
            ref float __result,
            F16ViperIIJammers.EcmPowerDrawState __state)
        {
            F16ViperIIJammers.CompleteViperEcmPowerDraw(
                __instance,
                ref __result,
                __state);
        }
    }

    [HarmonyPatch]
    internal static class F16ViperIIAdjRechargePatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(PowerSupply), "FixedUpdate", Type.EmptyTypes);
        }

        private static void Prefix(
            PowerSupply __instance,
            out F16ViperIIJammers.AdjRechargeState __state)
        {
            __state = F16ViperIIJammers.BeginAdjRecharge(__instance);
        }

        private static Exception Finalizer(
            PowerSupply __instance,
            ref F16ViperIIJammers.AdjRechargeState __state,
            Exception __exception)
        {
            F16ViperIIJammers.EndAdjRecharge(__instance, ref __state);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(Hardpoint), nameof(Hardpoint.SpawnMount))]
    internal static class F16ViperIIJammerPhysicalScalePatch
    {
        private static void Prefix(WeaponMount weaponMount, out bool __state)
        {
            __state = F16ViperIIJammers.ActivatePrefabForSpawn(weaponMount);
        }

        private static void Postfix(
            WeaponMount weaponMount,
            GameObject __result,
            ref bool __state)
        {
            F16ViperIIJammers.ScaleSpawnedMount(__result, weaponMount);
            F16ViperIIJammers.RestorePrefabAfterSpawn(weaponMount, ref __state);
        }

        private static Exception Finalizer(
            WeaponMount weaponMount,
            ref bool __state,
            Exception __exception)
        {
            F16ViperIIJammers.RestorePrefabAfterSpawn(weaponMount, ref __state);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(Unit), nameof(Unit.Jam))]
    internal static class F16ViperIIJammerSingleChannelProofPatch
    {
        private static bool Prefix(Unit __instance, Unit.JamEventArgs args)
        {
            return F16ViperIIJammers.RunOriginalUnitJam(__instance, args);
        }
    }

    [HarmonyPatch(typeof(WeaponChecker), nameof(WeaponChecker.VetLoadout))]
    internal static class F16ViperIIJammerServerValidationPatch
    {
        private static void Prefix(
            AircraftDefinition definition,
            Loadout requestedLoadout,
            out F16ViperIIJammers.ServerValidationState __state)
        {
            F16ViperII.SanitizeRestrictedWeapons(definition, requestedLoadout);
            __state = F16ViperIIJammers.BeginServerValidation(definition, requestedLoadout);
        }

        private static Exception Finalizer(
            F16ViperIIJammers.ServerValidationState __state,
            Exception __exception)
        {
            F16ViperIIJammers.EndServerValidation(__state);
            return __exception;
        }
    }
}

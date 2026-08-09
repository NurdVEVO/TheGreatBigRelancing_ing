using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using NuclearOption.SavedMission;
using UnityEngine;

namespace TheGreatBigRebalancing.Balance
{
    internal static class F16ViperII
    {
        private sealed class WeaponAlias
        {
            internal WeaponAlias(
                string sourceCode,
                string sourceName,
                string displayName,
                string shortName)
            {
                SourceCode = sourceCode;
                SourceName = sourceName;
                DisplayName = displayName;
                ShortName = shortName;
            }

            internal string SourceCode { get; }
            internal string SourceName { get; }
            internal string DisplayName { get; }
            internal string ShortName { get; }
        }

        private sealed class RegistrationSnapshot
        {
            internal List<AircraftDefinition> AircraftDefinitions;
            internal List<WeaponMount> WeaponMounts;
            internal Dictionary<WeaponInfo, WeaponInfo> WeaponInfos;
            internal Dictionary<WeaponMount, WeaponMount> WeaponPrefabs;
            internal Dictionary<string, WeaponAlias> WeaponAliases;
            internal AircraftDefinition SourceDefinition;
            internal AircraftDefinition ViperDefinition;
            internal WeaponMount SixJdamMount;
        }

        private const string SourceDefinitionKey = "Aryx_F16M_KingViper";
        private const string DefinitionKey = "TGBR_F16VX_ViperII";
        private const string DisplayName = "F-16VX Viper II";
        private const string AircraftCode = "F-16VX";
        private const float PurchaseCost = 70f;
        private const float CapacitorCapacity = 500f;
        private const float FlareAmmoMultiplier = 2.5f;
        private const int InnerWingFlarePointCount = 2;
        private const string WeaponPrefabKeyPrefix = "TGBR_F16VX_";
        private const string SixJdamSourceKey = "TGBR_F16VX_bomb_250_triple";
        private const string SixJdamMountKey =
            "TGBR_WORKSHOP_TGBR_F16VX_bomb_250_x6";
        private const string SixJdamMountName = "GBU-38 JDAM x6";
        private const string SixJdamWeaponName = "GBU-38 JDAM";
        private const string SixJdamShortName = "GBU-38";
        private const string FlarePointNamePrefix = "TGBR Viper II Inner Wing Flare Point";
        private const string LeftFlarePointName = FlarePointNamePrefix + " Left";
        private const string RightFlarePointName = FlarePointNamePrefix + " Right";

        private static readonly MethodInfo EncyclopediaAfterLoad =
            AccessTools.Method(typeof(Encyclopedia), "AfterLoad", Type.EmptyTypes);
        private static readonly FieldInfo SelectorAircraftList =
            AccessTools.Field(typeof(AircraftSelectionMenu), "aircraftSelection");
        private static readonly FieldInfo SelectorIndex =
            AccessTools.Field(typeof(AircraftSelectionMenu), "selectionIndex");
        private static readonly FieldInfo PowerSupplyMaxCharge =
            AccessTools.Field(typeof(PowerSupply), "maxCharge");
        private static readonly FieldInfo FlareMaxAmmo =
            AccessTools.Field(typeof(FlareEjector), "maxAmmo");
        private static readonly FieldInfo FlareEjectionPoints =
            AccessTools.Field(typeof(FlareEjector), "ejectionPoints");
        private static readonly FieldInfo FlareEjectionGrouping =
            AccessTools.Field(typeof(FlareEjector), "ejectionGrouping");
        private static readonly FieldInfo FlareEjectionVelocity =
            AccessTools.Field(typeof(FlareEjector), "ejectionVelocity");
        private static readonly FieldInfo FlareEjectionIndex =
            AccessTools.Field(typeof(FlareEjector), "ejectionIndex");
        private static readonly FieldInfo CountermeasureStations =
            AccessTools.Field(typeof(CountermeasureManager), "countermeasureStations");
        private static readonly FieldInfo WeaponSelectorHardpointSet =
            AccessTools.Field(typeof(WeaponSelector), "hardpointSet");

        private static readonly WeaponAlias[] WeaponAliases =
        {
            new WeaponAlias("PAB-250LR", "PAB-250LR", "GBU-38ER", "GBU-38ER"),
            new WeaponAlias("PAB-80LR", "PAB-80LR", "GBU-53/B StormBreaker", "GBU-53/B"),
            new WeaponAlias("PAB-250", "PAB-250", "GBU-38 JDAM", "GBU-38"),
            new WeaponAlias("PAB-125", "PAB-125", "GBU-29", "GBU-29"),
            new WeaponAlias("GPO-500", "GPO-500", "GBU-16 Paveway 2", "GBU-16"),
            new WeaponAlias("GPO-2P", "GPO-2P Auger", "GBU-10", "GBU-10"),
            new WeaponAlias("AGM-68", "AGM-68", "AGM-65K Maverick", "AGM-65K"),
            new WeaponAlias("AGM-48", "AGM-48", "AGM-114L Longbow Hellfire", "AGM-114L"),
            new WeaponAlias("MMR-S3", "MMR-S3", "AIM-9X Sidewinder", "AIM-9X"),
            new WeaponAlias("AAM-29", "AAM-29 Scythe", "AIM-170 Scythe 2", "AIM-170")
        };

        private static readonly HashSet<HardpointSet> ViperHardpointSets =
            new HashSet<HardpointSet>();
        private static readonly Dictionary<WeaponInfo, WeaponInfo> ViperWeaponInfos =
            new Dictionary<WeaponInfo, WeaponInfo>();
        private static readonly Dictionary<WeaponMount, WeaponMount> ViperWeaponPrefabs =
            new Dictionary<WeaponMount, WeaponMount>();
        private static readonly Dictionary<string, WeaponAlias> ViperWeaponAliasesByKey =
            new Dictionary<string, WeaponAlias>(StringComparer.OrdinalIgnoreCase);

        private static Encyclopedia pendingEncyclopedia;
        private static AircraftDefinition sourceDefinition;
        private static AircraftDefinition viperDefinition;
        private static WeaponMount sixJdamMount;
        private static float nextAttempt;
        private static float deadline;
        private static bool rebuildingEncyclopedia;
        private static bool loggedSuccess;
        private static bool loggedConfiguredInstance;
        private static bool loggedMissingPowerSupply;
        private static bool loggedFlareConfigurationFailure;
        private static int variantCreationDepth;
        private static int networkRegistrationListIndex = -1;

        internal static void RegisterEncyclopedia(Encyclopedia encyclopedia)
        {
            if (rebuildingEncyclopedia || encyclopedia?.aircraft == null)
            {
                return;
            }
            if (viperDefinition != null)
            {
                ApplySixJdamMetadata(sixJdamMount);
                return;
            }

            pendingEncyclopedia = encyclopedia;
            nextAttempt = 0f;
            deadline = Time.unscaledTime + 60f;
            TryApplyPending(force: true);
        }

        internal static void TryApplyPending(bool force = false)
        {
            if (viperDefinition != null || pendingEncyclopedia == null ||
                (!force && Time.unscaledTime < nextAttempt))
            {
                return;
            }

            if (TryCreateVariant(pendingEncyclopedia))
            {
                pendingEncyclopedia = null;
                return;
            }

            if (Time.unscaledTime >= deadline)
            {
                pendingEncyclopedia = null;
                Plugin.ModLogger?.LogWarning(
                    "F-16VX Viper II was not added because the Aryx F-16M King Viper definition " +
                    "or the stock jammer/centerline assets were unavailable.");
                return;
            }

            nextAttempt = Time.unscaledTime + 0.5f;
        }

        internal static bool IsVariant(AircraftDefinition definition)
        {
            return definition != null &&
                   string.Equals(definition.jsonKey, DefinitionKey, StringComparison.OrdinalIgnoreCase);
        }

        internal static bool IsVariantKey(string key)
        {
            return string.Equals(key, DefinitionKey, StringComparison.OrdinalIgnoreCase);
        }

        internal static AircraftDefinition[] AddVariantWhenSourceAvailable(
            AircraftDefinition[] availableAircraft)
        {
            if (availableAircraft == null || viperDefinition == null || sourceDefinition == null)
            {
                return availableAircraft;
            }

            bool sourceAvailable = false;
            foreach (AircraftDefinition definition in availableAircraft)
            {
                if (definition == viperDefinition)
                {
                    return availableAircraft;
                }
                if (definition == sourceDefinition)
                {
                    sourceAvailable = true;
                }
            }

            if (!sourceAvailable)
            {
                return availableAircraft;
            }

            AircraftDefinition[] expanded = new AircraftDefinition[availableAircraft.Length + 1];
            Array.Copy(availableAircraft, expanded, availableAircraft.Length);
            expanded[expanded.Length - 1] = viperDefinition;
            return expanded;
        }

        internal static void ApplyRuntimeRestrictions(WeaponManager weaponManager)
        {
            Aircraft aircraft = weaponManager?.GetComponentInParent<Aircraft>();
            if (aircraft == null || !IsVariant(aircraft.definition))
            {
                return;
            }
            ApplyInstanceConfiguration(aircraft, weaponManager);
        }

        internal static void PrepareRuntimeLoadout(WeaponManager weaponManager)
        {
            Aircraft aircraft = weaponManager?.GetComponentInParent<Aircraft>();
            if (aircraft == null || !IsVariant(aircraft.definition))
            {
                return;
            }
            ConfigureWeaponOptions(weaponManager);
            UseViperWeaponPrefabs(aircraft.loadout);
            SanitizeRestrictedWeapons(aircraft.loadout);
        }

        internal static void SanitizeRestrictedWeapons(
            AircraftDefinition definition,
            Loadout loadout)
        {
            if (IsVariant(definition))
            {
                UseViperWeaponPrefabs(loadout);
                SanitizeRestrictedWeapons(loadout);
            }
        }

        internal static void ActivateSpawnedWeaponPrefab(
            WeaponMount weaponMount,
            GameObject spawnedPrefab)
        {
            if (spawnedPrefab != null && IsViperWeaponPrefab(weaponMount))
            {
                spawnedPrefab.SetActive(true);
            }
        }

        internal static void SanitizeSelectorValue(
            WeaponSelector selector,
            ref WeaponMount weaponMount)
        {
            HardpointSet hardpointSet = WeaponSelectorHardpointSet?.GetValue(selector) as HardpointSet;
            if (hardpointSet == null || !ViperHardpointSets.Contains(hardpointSet))
            {
                return;
            }
            if (IsRestrictedWeapon(weaponMount))
            {
                weaponMount = null;
            }
            else
            {
                weaponMount = GetViperWeaponPrefab(weaponMount);
            }
        }

        internal static GameObject ConfigureCreatedInstance(GameObject instance)
        {
            if (instance == null || variantCreationDepth <= 0 || viperDefinition == null)
            {
                return instance;
            }

            Aircraft aircraft = instance.GetComponent<Aircraft>() ??
                                instance.GetComponentInChildren<Aircraft>(true);
            if (aircraft == null || aircraft.definition != sourceDefinition)
            {
                return instance;
            }

            ((Unit)aircraft).definition = viperDefinition;
            ApplyInstanceConfiguration(aircraft, aircraft.weaponManager);
            LogConfiguredInstance();
            return instance;
        }

        internal static void ConfigureNetworkClient(Aircraft aircraft)
        {
            if (aircraft == null || viperDefinition == null || IsVariant(aircraft.definition) ||
                !Contains(aircraft.NetworkunitName, DisplayName))
            {
                return;
            }

            ((Unit)aircraft).definition = viperDefinition;
            ApplyInstanceConfiguration(aircraft, aircraft.weaponManager);
            LogConfiguredInstance();
        }

        internal static bool EnterVariantContext(AircraftDefinition definition)
        {
            if (!IsVariant(definition))
            {
                return false;
            }
            variantCreationDepth++;
            return true;
        }

        internal static bool EnterVariantContext(string definitionKey)
        {
            if (!IsVariantKey(definitionKey))
            {
                return false;
            }
            variantCreationDepth++;
            return true;
        }

        internal static bool EnterSelectorContext(AircraftSelectionMenu selector)
        {
            if (selector == null || SelectorAircraftList == null || SelectorIndex == null)
            {
                return false;
            }

            List<AircraftDefinition> definitions =
                SelectorAircraftList.GetValue(selector) as List<AircraftDefinition>;
            int index = (int)SelectorIndex.GetValue(selector);
            return definitions != null && index >= 0 && index < definitions.Count &&
                   EnterVariantContext(definitions[index]);
        }

        internal static void ExitVariantContext(bool entered)
        {
            if (entered && variantCreationDepth > 0)
            {
                variantCreationDepth--;
            }
        }

        internal static void ExcludeSharedPrefabFromNetworkRegistration()
        {
            TryApplyPending(force: true);
            Encyclopedia encyclopedia = Encyclopedia.i;
            if (encyclopedia?.aircraft == null || viperDefinition == null ||
                networkRegistrationListIndex >= 0)
            {
                return;
            }

            networkRegistrationListIndex = encyclopedia.aircraft.IndexOf(viperDefinition);
            if (networkRegistrationListIndex >= 0)
            {
                encyclopedia.aircraft.RemoveAt(networkRegistrationListIndex);
            }
        }

        internal static void RestoreAfterNetworkRegistration()
        {
            if (networkRegistrationListIndex < 0 || viperDefinition == null)
            {
                return;
            }

            Encyclopedia encyclopedia = Encyclopedia.i;
            if (encyclopedia?.aircraft != null && !encyclopedia.aircraft.Contains(viperDefinition))
            {
                int index = Mathf.Clamp(
                    networkRegistrationListIndex,
                    0,
                    encyclopedia.aircraft.Count);
                encyclopedia.aircraft.Insert(index, viperDefinition);
            }
            networkRegistrationListIndex = -1;
        }

        private static bool TryCreateVariant(Encyclopedia encyclopedia)
        {
            AircraftDefinition existing = encyclopedia.aircraft.Find(IsVariant);
            if (existing != null)
            {
                viperDefinition = existing;
                sourceDefinition = encyclopedia.aircraft.Find(IsSource);
                return sourceDefinition != null;
            }

            AircraftDefinition source = encyclopedia.aircraft.Find(IsSource);
            if (source?.unitPrefab == null || source.aircraftParameters == null)
            {
                return false;
            }

            Aircraft sourceAircraft = source.unitPrefab.GetComponent<Aircraft>() ??
                                      source.unitPrefab.GetComponentInChildren<Aircraft>(true);
            WeaponManager sourceWeaponManager = sourceAircraft?.weaponManager ??
                                                source.unitPrefab.GetComponentInChildren<WeaponManager>(true);
            int centerlineIndex = FindCenterlineIndex(sourceWeaponManager);
            if (sourceAircraft == null || sourceWeaponManager == null || centerlineIndex < 0)
            {
                LogPylonNames(sourceWeaponManager);
                return false;
            }

            RegistrationSnapshot snapshot = CaptureRegistrationSnapshot(encyclopedia);
            try
            {
                if (!F16ViperIIJammers.TryRegister(encyclopedia))
                {
                    RollBackRegistration(encyclopedia, snapshot);
                    return false;
                }
                if (!TryRegisterWeaponPrefabs(
                        encyclopedia,
                        sourceWeaponManager,
                        out string weaponPrefabFailure))
                {
                    Plugin.ModLogger?.LogError(
                        "F-16VX weapon-prefab registration failed: " + weaponPrefabFailure);
                    RollBackRegistration(encyclopedia, snapshot);
                    return false;
                }

                AircraftDefinition variant = UnityEngine.Object.Instantiate(source);
                variant.name = DefinitionKey;
                variant.jsonKey = DefinitionKey;
                variant.unitName = DisplayName;
                variant.code = AircraftCode;
                variant.value = PurchaseCost;
                variant.description =
                    "The Lockheed-Boeing joint-production F-16VX Viper II is the USAF-serving " +
                    "variant of the F-16M King Viper, incorporating NATO-wide upgrades and " +
                    "lessons learned from the F-15EX Eagle II.\n\n" +
                    "It features a sophisticated centerline pylon used specifically for " +
                    "electronic-warfare equipment that otherwise cannot fit within the aircraft. " +
                    "Its bolt-on launchers (BOL) increase the total flare capacity and dispense " +
                    "four flares per cycle.";
                variant.hideFlags = HideFlags.HideAndDontSave;
                variant.aircraftParameters = CloneParameters(source.aircraftParameters, centerlineIndex);

                // Blueprinter owns this asset-backed prefab. Sharing it avoids cloning an already-awoken
                // runtime instance; creation-context patches assign the variant definition to each clone.
                variant.unitPrefab = source.unitPrefab;

                sourceDefinition = source;
                viperDefinition = variant;
                encyclopedia.aircraft.Add(variant);
                RebuildEncyclopedia(encyclopedia);
                ApplySixJdamMetadata(sixJdamMount);

                if (!ValidateVariant(encyclopedia, centerlineIndex, out string failure))
                {
                    Plugin.ModLogger?.LogError("F-16VX Viper II validation failed: " + failure);
                    RollBackRegistration(encyclopedia, snapshot);
                    return false;
                }

                RunSmokeProbeIfRequested();

                if (!loggedSuccess)
                {
                    loggedSuccess = true;
                    Plugin.ModLogger?.LogInfo(
                        "F-16VX Viper II validation passed: distinct aircraft/save identity, $70m " +
                        "purchase value, Blueprinter asset-backed prefab, and three $27m centerline " +
                        "suites; OJS has one threat-prioritized Unit.Jam channel while ADJ/MiM have none; " +
                        "ten NATO-named weapon families use registered Viper II prefab clones, " +
                        "with a permanent six-store GBU-38 rack limited to the inner-wing pylons.");
                }
                return true;
            }
            catch (Exception exception)
            {
                RollBackRegistration(encyclopedia, snapshot);
                Plugin.ModLogger?.LogError(
                    "F-16VX Viper II registration threw and was rolled back: " +
                    (exception.InnerException?.Message ?? exception.Message));
                return false;
            }
        }

        private static RegistrationSnapshot CaptureRegistrationSnapshot(Encyclopedia encyclopedia)
        {
            return new RegistrationSnapshot
            {
                AircraftDefinitions = new List<AircraftDefinition>(encyclopedia.aircraft),
                WeaponMounts = new List<WeaponMount>(encyclopedia.weaponMounts),
                WeaponInfos = new Dictionary<WeaponInfo, WeaponInfo>(ViperWeaponInfos),
                WeaponPrefabs = new Dictionary<WeaponMount, WeaponMount>(ViperWeaponPrefabs),
                WeaponAliases = new Dictionary<string, WeaponAlias>(
                    ViperWeaponAliasesByKey,
                    StringComparer.OrdinalIgnoreCase),
                SourceDefinition = sourceDefinition,
                ViperDefinition = viperDefinition,
                SixJdamMount = sixJdamMount
            };
        }

        private static void RollBackRegistration(
            Encyclopedia encyclopedia,
            RegistrationSnapshot snapshot)
        {
            if (encyclopedia == null || snapshot == null ||
                !RegistrationChanged(encyclopedia, snapshot))
            {
                return;
            }

            List<WeaponMount> addedMounts = new List<WeaponMount>();
            foreach (WeaponMount mount in encyclopedia.weaponMounts)
            {
                if (mount != null && !snapshot.WeaponMounts.Contains(mount))
                {
                    addedMounts.Add(mount);
                }
            }
            List<AircraftDefinition> addedDefinitions = new List<AircraftDefinition>();
            foreach (AircraftDefinition definition in encyclopedia.aircraft)
            {
                if (definition != null && !snapshot.AircraftDefinitions.Contains(definition))
                {
                    addedDefinitions.Add(definition);
                }
            }
            HashSet<WeaponInfo> addedInfos = new HashSet<WeaponInfo>();
            foreach (WeaponInfo info in ViperWeaponInfos.Values)
            {
                if (info != null && !snapshot.WeaponInfos.ContainsValue(info))
                {
                    addedInfos.Add(info);
                }
            }

            encyclopedia.aircraft.Clear();
            encyclopedia.aircraft.AddRange(snapshot.AircraftDefinitions);
            encyclopedia.weaponMounts.Clear();
            encyclopedia.weaponMounts.AddRange(snapshot.WeaponMounts);

            RestoreDictionary(ViperWeaponInfos, snapshot.WeaponInfos);
            RestoreDictionary(ViperWeaponPrefabs, snapshot.WeaponPrefabs);
            RestoreDictionary(ViperWeaponAliasesByKey, snapshot.WeaponAliases);
            sourceDefinition = snapshot.SourceDefinition;
            viperDefinition = snapshot.ViperDefinition;
            sixJdamMount = snapshot.SixJdamMount;
            F16ViperIIJammers.RebindRegistration(encyclopedia);

            try
            {
                RebuildEncyclopedia(encyclopedia);
            }
            catch (Exception exception)
            {
                Plugin.ModLogger?.LogError(
                    "F-16VX rollback could not rebuild Encyclopedia lookups: " +
                    (exception.InnerException?.Message ?? exception.Message));
            }

            HashSet<WeaponInfo> retainedInfos = new HashSet<WeaponInfo>();
            foreach (WeaponMount mount in snapshot.WeaponMounts)
            {
                if (mount?.info != null)
                {
                    retainedInfos.Add(mount.info);
                }
            }
            foreach (WeaponInfo info in snapshot.WeaponInfos.Values)
            {
                if (info != null)
                {
                    retainedInfos.Add(info);
                }
            }
            HashSet<WeaponInfo> removedInfos = addedInfos;
            foreach (WeaponMount mount in addedMounts)
            {
                if (mount?.prefab != null)
                {
                    UnityEngine.Object.Destroy(mount.prefab);
                }
                if (mount?.info != null && !retainedInfos.Contains(mount.info))
                {
                    removedInfos.Add(mount.info);
                }
                if (mount != null)
                {
                    UnityEngine.Object.Destroy(mount);
                }
            }
            foreach (WeaponInfo info in removedInfos)
            {
                UnityEngine.Object.Destroy(info);
            }
            foreach (AircraftDefinition definition in addedDefinitions)
            {
                if (definition?.aircraftParameters != null &&
                    !snapshot.AircraftDefinitions.Exists(candidate =>
                        candidate?.aircraftParameters == definition.aircraftParameters))
                {
                    UnityEngine.Object.Destroy(definition.aircraftParameters);
                }
                if (definition != null)
                {
                    UnityEngine.Object.Destroy(definition);
                }
            }
        }

        private static bool RegistrationChanged(
            Encyclopedia encyclopedia,
            RegistrationSnapshot snapshot)
        {
            return encyclopedia.aircraft.Count != snapshot.AircraftDefinitions.Count ||
                   encyclopedia.weaponMounts.Count != snapshot.WeaponMounts.Count ||
                   ViperWeaponInfos.Count != snapshot.WeaponInfos.Count ||
                   ViperWeaponPrefabs.Count != snapshot.WeaponPrefabs.Count ||
                   ViperWeaponAliasesByKey.Count != snapshot.WeaponAliases.Count ||
                   sourceDefinition != snapshot.SourceDefinition ||
                   viperDefinition != snapshot.ViperDefinition ||
                   sixJdamMount != snapshot.SixJdamMount;
        }

        private static void RestoreDictionary<TKey, TValue>(
            IDictionary<TKey, TValue> destination,
            IDictionary<TKey, TValue> source)
        {
            destination.Clear();
            foreach (KeyValuePair<TKey, TValue> entry in source)
            {
                destination.Add(entry.Key, entry.Value);
            }
        }

        private static AircraftParameters CloneParameters(
            AircraftParameters source,
            int centerlineIndex)
        {
            AircraftParameters clone = UnityEngine.Object.Instantiate(source);
            clone.name = DefinitionKey + "_Parameters";
            clone.aircraftName = DisplayName;
            clone.hideFlags = HideFlags.HideAndDontSave;

            clone.loadouts = new List<Loadout>();
            if (source.loadouts != null)
            {
                foreach (Loadout loadout in source.loadouts)
                {
                    clone.loadouts.Add(CloneLoadout(loadout, centerlineIndex));
                }
            }

            if (source.StandardLoadouts == null)
            {
                clone.StandardLoadouts = Array.Empty<StandardLoadout>();
            }
            else
            {
                clone.StandardLoadouts = new StandardLoadout[source.StandardLoadouts.Length];
                for (int i = 0; i < source.StandardLoadouts.Length; i++)
                {
                    StandardLoadout original = source.StandardLoadouts[i];
                    if (original == null)
                    {
                        continue;
                    }
                    clone.StandardLoadouts[i] = new StandardLoadout
                    {
                        disabled = original.disabled,
                        Name = original.Name,
                        FuelRatio = original.FuelRatio,
                        loadout = CloneLoadout(original.loadout, centerlineIndex)
                    };
                }
            }
            return clone;
        }

        private static Loadout CloneLoadout(Loadout source, int centerlineIndex)
        {
            Loadout clone = new Loadout
            {
                weapons = source?.weapons != null
                    ? new List<WeaponMount>(source.weapons)
                    : new List<WeaponMount>()
            };
            UseViperWeaponPrefabs(clone);
            ClearLoadoutCenterline(clone, centerlineIndex);
            SanitizeRestrictedWeapons(clone);
            return clone;
        }

        private static void ApplyInstanceConfiguration(
            Aircraft aircraft,
            WeaponManager weaponManager)
        {
            int centerlineIndex = FindCenterlineIndex(weaponManager);
            if (centerlineIndex < 0)
            {
                return;
            }
            F16ViperIIJammers.ConfigureCenterline(
                weaponManager.hardpointSets[centerlineIndex]);
            ClearLoadoutCenterline(aircraft.loadout, centerlineIndex);
            ConfigureWeaponOptions(weaponManager);
            UseViperWeaponPrefabs(aircraft.loadout);
            SanitizeRestrictedWeapons(aircraft.loadout);
            SetCapacitorCapacity(aircraft);
            ConfigureFlares(aircraft, weaponManager);
        }

        private static void ConfigureWeaponOptions(WeaponManager weaponManager)
        {
            if (weaponManager?.hardpointSets == null)
            {
                return;
            }
            foreach (HardpointSet set in weaponManager.hardpointSets)
            {
                if (set == null)
                {
                    continue;
                }
                ViperHardpointSets.Add(set);
                if (IsInnerWingPylon(set) && set.weaponOptions == null)
                {
                    set.weaponOptions = new List<WeaponMount>();
                }
                if (set.weaponOptions != null)
                {
                    for (int i = 0; i < set.weaponOptions.Count; i++)
                    {
                        set.weaponOptions[i] = GetViperWeaponPrefab(set.weaponOptions[i]);
                    }
                }
                set.weaponOptions?.RemoveAll(IsRestrictedWeapon);
                if (IsRestrictedWeapon(set.weaponMount))
                {
                    set.RemoveMounts();
                }
                else
                {
                    set.weaponMount = GetViperWeaponPrefab(set.weaponMount);
                }
                if (set.weaponOptions != null)
                {
                    set.weaponOptions.RemoveAll(mount =>
                        mount != null && string.Equals(
                            mount.jsonKey,
                            SixJdamMountKey,
                            StringComparison.OrdinalIgnoreCase));
                    if (IsInnerWingPylon(set) && sixJdamMount != null)
                    {
                        set.weaponOptions.Add(sixJdamMount);
                    }
                }
            }
        }

        internal static bool IsInnerWingPylon(HardpointSet set)
        {
            string name = set?.name ?? string.Empty;
            return name.IndexOf("inner", StringComparison.OrdinalIgnoreCase) >= 0 &&
                   name.IndexOf("wing", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void UseViperWeaponPrefabs(Loadout loadout)
        {
            if (loadout?.weapons == null)
            {
                return;
            }
            for (int i = 0; i < loadout.weapons.Count; i++)
            {
                loadout.weapons[i] = GetViperWeaponPrefab(loadout.weapons[i]);
            }
        }

        private static WeaponMount GetViperWeaponPrefab(WeaponMount source)
        {
            if (source == null || IsViperWeaponPrefab(source))
            {
                return source;
            }
            return ViperWeaponPrefabs.TryGetValue(source, out WeaponMount prefab)
                ? prefab
                : source;
        }

        internal static bool IsViperWeaponPrefab(WeaponMount mount)
        {
            return mount != null &&
                   ViperWeaponAliasesByKey.ContainsKey(mount.jsonKey ?? string.Empty);
        }

        internal static WeaponMount NormalizeWeaponMountForServerValidation(
            WeaponMount mount)
        {
            return GetViperWeaponPrefab(mount);
        }

        internal static WeaponMount SixJdamMountForValidation => sixJdamMount;

        internal static List<WeaponMount> BuildServerValidationOptions(
            HardpointSet hardpointSet)
        {
            List<WeaponMount> options = new List<WeaponMount>();
            List<WeaponMount> sourceOptions = hardpointSet?.weaponOptions;
            if (sourceOptions == null)
            {
                sourceOptions = new List<WeaponMount>();
            }
            foreach (WeaponMount source in sourceOptions)
            {
                if (!IsRestrictedWeapon(source))
                {
                    options.Add(GetViperWeaponPrefab(source));
                }
            }
            if (IsInnerWingPylon(hardpointSet) && sixJdamMount != null &&
                !options.Contains(sixJdamMount))
            {
                options.Add(sixJdamMount);
            }
            return options;
        }

        private static int SanitizeRestrictedWeapons(Loadout loadout)
        {
            if (loadout?.weapons == null)
            {
                return 0;
            }
            int removed = 0;
            for (int i = 0; i < loadout.weapons.Count; i++)
            {
                if (IsRestrictedWeapon(loadout.weapons[i]))
                {
                    loadout.weapons[i] = null;
                    removed++;
                }
            }
            return removed;
        }

        private static bool IsRestrictedWeapon(WeaponMount mount)
        {
            if (mount == null)
            {
                return false;
            }
            string shortName = mount.info?.shortName;
            string weaponName = mount.info?.weaponName;
            if (string.Equals(shortName, "IRM-S2", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(shortName, "AAM-36", StringComparison.OrdinalIgnoreCase) ||
                StartsWithWeaponName(weaponName, "IRM-S2") ||
                StartsWithWeaponName(weaponName, "AAM-36"))
            {
                return true;
            }
            string key = mount.jsonKey ?? string.Empty;
            return key.IndexOf("AAM3", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   key.IndexOf("AAM4", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool TryGetWeaponAlias(WeaponInfo info, out WeaponAlias alias)
        {
            alias = null;
            if (info == null)
            {
                return false;
            }
            foreach (WeaponAlias candidate in WeaponAliases)
            {
                if (string.Equals(info.shortName, candidate.SourceCode, StringComparison.OrdinalIgnoreCase) ||
                    StartsWithWeaponName(info.weaponName, candidate.SourceName))
                {
                    alias = candidate;
                    return true;
                }
            }
            return false;
        }

        private static bool StartsWithWeaponName(string value, string weaponName)
        {
            return value != null &&
                   (string.Equals(value, weaponName, StringComparison.OrdinalIgnoreCase) ||
                    value.StartsWith(weaponName + " ", StringComparison.OrdinalIgnoreCase));
        }

        private static string GetViperMountDisplayName(string mountName)
        {
            if (string.IsNullOrEmpty(mountName))
            {
                return mountName;
            }
            foreach (WeaponAlias alias in WeaponAliases)
            {
                if (string.Equals(mountName, alias.SourceName, StringComparison.OrdinalIgnoreCase))
                {
                    return alias.DisplayName;
                }
                string prefix = alias.SourceName + " x";
                if (mountName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return alias.DisplayName + mountName.Substring(alias.SourceName.Length);
                }
            }
            return mountName;
        }

        private static bool TryRegisterWeaponPrefabs(
            Encyclopedia encyclopedia,
            WeaponManager sourceWeaponManager,
            out string failure)
        {
            failure = string.Empty;
            if (encyclopedia?.weaponMounts == null || sourceWeaponManager?.hardpointSets == null)
            {
                failure = "the encyclopedia weapon list or King Viper hardpoints are unavailable";
                return false;
            }

            HashSet<string> registeredAliases =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (HardpointSet set in sourceWeaponManager.hardpointSets)
            {
                if (set?.weaponOptions == null)
                {
                    continue;
                }
                foreach (WeaponMount sourceMount in set.weaponOptions)
                {
                    if (sourceMount == null ||
                        !TryGetWeaponAlias(sourceMount.info, out WeaponAlias alias))
                    {
                        continue;
                    }
                    registeredAliases.Add(alias.SourceCode);
                    if (ViperWeaponPrefabs.ContainsKey(sourceMount))
                    {
                        continue;
                    }

                    string key = WeaponPrefabKeyPrefix + sourceMount.jsonKey;
                    WeaponMount prefab = encyclopedia.weaponMounts.Find(
                        candidate => candidate != null && string.Equals(
                            candidate.jsonKey,
                            key,
                            StringComparison.OrdinalIgnoreCase));
                    if (prefab == null)
                    {
                        if (!TryCreateWeaponPrefab(sourceMount, alias, key, out prefab, out failure))
                        {
                            return false;
                        }
                        encyclopedia.weaponMounts.Add(prefab);
                    }
                    ViperWeaponPrefabs.Add(sourceMount, prefab);
                    ViperWeaponAliasesByKey[prefab.jsonKey] = alias;
                }
            }

            foreach (WeaponAlias alias in WeaponAliases)
            {
                if (!registeredAliases.Contains(alias.SourceCode))
                {
                    failure = alias.SourceCode + " was not found on the King Viper";
                    return false;
                }
            }
            if (!TryRegisterSixJdamMount(encyclopedia, out failure))
            {
                return false;
            }
            return true;
        }

        private static bool TryCreateWeaponPrefab(
            WeaponMount sourceMount,
            WeaponAlias alias,
            string key,
            out WeaponMount prefab,
            out string failure)
        {
            prefab = null;
            failure = string.Empty;
            if (sourceMount?.prefab == null || sourceMount.info == null)
            {
                failure = (sourceMount?.jsonKey ?? alias.SourceCode) +
                          " has no mount prefab or weapon information";
                return false;
            }

            WeaponInfo info = GetViperWeaponInfo(sourceMount.info, alias);
            bool sourceWasActive = sourceMount.prefab.activeSelf;
            GameObject prefabObject;
            try
            {
                if (sourceWasActive)
                {
                    sourceMount.prefab.SetActive(false);
                }
                prefabObject = UnityEngine.Object.Instantiate(sourceMount.prefab);
            }
            finally
            {
                if (sourceWasActive)
                {
                    sourceMount.prefab.SetActive(true);
                }
            }
            prefabObject.name = key + "_Prefab";
            prefabObject.hideFlags = HideFlags.HideAndDontSave;
            foreach (Weapon weapon in prefabObject.GetComponentsInChildren<Weapon>(true))
            {
                weapon.info = info;
            }
            prefabObject.SetActive(false);
            UnityEngine.Object.DontDestroyOnLoad(prefabObject);

            prefab = UnityEngine.Object.Instantiate(sourceMount);
            prefab.name = key;
            prefab.jsonKey = key;
            prefab.mountName = GetViperMountDisplayName(sourceMount.mountName);
            prefab.info = info;
            prefab.prefab = prefabObject;
            prefab.hideFlags = HideFlags.HideAndDontSave;
            prefab.dontAutomaticallyAddToEncyclopedia = false;
            return true;
        }

        private static bool TryRegisterSixJdamMount(
            Encyclopedia encyclopedia,
            out string failure)
        {
            failure = string.Empty;
            WeaponMount source = encyclopedia.weaponMounts.Find(candidate =>
                candidate != null && string.Equals(
                    candidate.jsonKey,
                    SixJdamSourceKey,
                    StringComparison.OrdinalIgnoreCase));
            if (source == null || !ViperWeaponAliasesByKey.TryGetValue(
                    source.jsonKey ?? string.Empty,
                    out WeaponAlias alias))
            {
                failure = SixJdamSourceKey +
                          " was not registered as the Viper II three-store GBU-38 source";
                return false;
            }

            WeaponMount registered = encyclopedia.weaponMounts.Find(candidate =>
                candidate != null && string.Equals(
                    candidate.jsonKey,
                    SixJdamMountKey,
                    StringComparison.OrdinalIgnoreCase));
            if (registered == null)
            {
                if (!TryCreateSixJdamMount(source, out registered, out failure))
                {
                    return false;
                }
                encyclopedia.weaponMounts.Add(registered);
            }

            sixJdamMount = registered;
            ViperWeaponAliasesByKey[registered.jsonKey] = alias;
            return true;
        }

        private static bool TryCreateSixJdamMount(
            WeaponMount source,
            out WeaponMount mount,
            out string failure)
        {
            mount = null;
            failure = string.Empty;
            if (source?.prefab == null || source.info == null)
            {
                failure = "the Viper II three-store GBU-38 source is incomplete";
                return false;
            }

            WeaponInfo info = UnityEngine.Object.Instantiate(source.info);
            info.name = SixJdamMountKey + "_Info";
            info.weaponName = SixJdamWeaponName;
            info.shortName = SixJdamShortName;
            info.costPerRound = 0.05f;
            info.hideFlags = HideFlags.HideAndDontSave;

            bool sourceWasActive = source.prefab.activeSelf;
            GameObject prefabObject = null;
            try
            {
                if (sourceWasActive)
                {
                    source.prefab.SetActive(false);
                }
                prefabObject = UnityEngine.Object.Instantiate(source.prefab);
                prefabObject.name = SixJdamMountKey + "_Prefab";
                prefabObject.hideFlags = HideFlags.HideAndDontSave;
                BuildSixJdamHierarchy(prefabObject);
                foreach (Weapon weapon in prefabObject.GetComponentsInChildren<Weapon>(true))
                {
                    weapon.info = info;
                }
                prefabObject.SetActive(false);
                UnityEngine.Object.DontDestroyOnLoad(prefabObject);

                mount = UnityEngine.Object.Instantiate(source);
                mount.name = SixJdamMountKey;
                mount.jsonKey = SixJdamMountKey;
                mount.info = info;
                mount.prefab = prefabObject;
                ApplySixJdamMetadata(mount);
                mount.hideFlags = HideFlags.HideAndDontSave;
                mount.dontAutomaticallyAddToEncyclopedia = false;
                return true;
            }
            catch (Exception exception)
            {
                if (prefabObject != null)
                {
                    UnityEngine.Object.Destroy(prefabObject);
                }
                UnityEngine.Object.Destroy(info);
                if (mount != null)
                {
                    UnityEngine.Object.Destroy(mount);
                    mount = null;
                }
                failure = "six-store GBU-38 creation failed: " + exception.Message;
                return false;
            }
            finally
            {
                if (sourceWasActive)
                {
                    source.prefab.SetActive(true);
                }
            }
        }

        private static void ApplySixJdamMetadata(WeaponMount mount)
        {
            if (mount?.info == null)
            {
                return;
            }
            mount.name = SixJdamMountKey;
            mount.jsonKey = SixJdamMountKey;
            mount.mountName = SixJdamMountName;
            mount.info.name = SixJdamMountKey + "_Info";
            mount.info.weaponName = SixJdamWeaponName;
            mount.info.shortName = SixJdamShortName;
            mount.info.costPerRound = 0.05f;
            mount.ammo = 6;
            mount.mass = 3000f;
            mount.drag = 0.9f;
            mount.RCS = 0.7f;
            mount.emptyCost = 0f;
            mount.emptyMass = 0f;
            mount.emptyDrag = 0.02f;
            mount.emptyRCS = 0.007f;
        }

        private static void BuildSixJdamHierarchy(GameObject prefab)
        {
            Transform root = prefab.transform;
            List<Transform> sourceChildren = new List<Transform>();
            for (int i = 0; i < root.childCount; i++)
            {
                sourceChildren.Add(root.GetChild(i));
            }

            GameObject assemblyObject = new GameObject("TGBR Rack Assembly 0");
            Transform foreAssembly = assemblyObject.transform;
            foreAssembly.SetParent(root, false);
            for (int i = 0; i < sourceChildren.Count; i++)
            {
                sourceChildren[i].SetParent(foreAssembly, false);
            }
            ApplySixJdamAssemblyChildren(foreAssembly);
            SetLocalTransform(
                foreAssembly,
                new Vector3(0f, 0f, 1f),
                Vector3.zero,
                Vector3.one);

            Transform aftAssembly = UnityEngine.Object.Instantiate(
                foreAssembly.gameObject,
                root,
                false).transform;
            aftAssembly.name = "TGBR Rack Assembly 1";
            SetLocalTransform(
                aftAssembly,
                new Vector3(0f, 0f, -1.3f),
                Vector3.zero,
                Vector3.one);
            SetLocalTransform(root, Vector3.zero, Vector3.zero, Vector3.one);

            if (root.childCount != 2 ||
                prefab.GetComponentsInChildren<Weapon>(true).Length != 6)
            {
                throw new InvalidOperationException(
                    "the supplied two-rack/six-store hierarchy could not be reproduced");
            }
        }

        private static void ApplySixJdamAssemblyChildren(Transform assembly)
        {
            List<Transform> stores = new List<Transform>();
            List<Transform> adapters = new List<Transform>();
            for (int i = 0; i < assembly.childCount; i++)
            {
                Transform child = assembly.GetChild(i);
                if (child.GetComponentInChildren<Weapon>(true) != null)
                {
                    stores.Add(child);
                }
                else
                {
                    adapters.Add(child);
                }
            }
            if (stores.Count != 3 || adapters.Count != 1)
            {
                throw new InvalidOperationException(
                    "the GBU-38 x3 source does not contain three stores and one adapter");
            }

            SetLocalTransform(stores[0], new Vector3(0f, -0.493f, 0.14f),
                Vector3.zero, Vector3.one);
            SetLocalTransform(stores[1], new Vector3(-0.288f, -0.216f, 0.14f),
                new Vector3(0f, 0f, 300f), Vector3.one);
            SetLocalTransform(stores[2], new Vector3(0.288f, -0.216f, 0.14f),
                new Vector3(0f, 0f, 60f), Vector3.one);
            SetLocalTransform(adapters[0], new Vector3(0f, 0.002f, 0.065f),
                Vector3.zero, Vector3.one);
        }

        private static void SetLocalTransform(
            Transform transform,
            Vector3 position,
            Vector3 rotation,
            Vector3 scale)
        {
            transform.localPosition = position;
            transform.localRotation = Quaternion.Euler(rotation);
            transform.localScale = scale;
        }

        private static WeaponInfo GetViperWeaponInfo(WeaponInfo source, WeaponAlias alias)
        {
            if (!ViperWeaponInfos.TryGetValue(source, out WeaponInfo info))
            {
                info = UnityEngine.Object.Instantiate(source);
                info.name = WeaponPrefabKeyPrefix + alias.ShortName + "_Info";
                info.weaponName = alias.DisplayName;
                info.shortName = alias.ShortName;
                info.hideFlags = HideFlags.HideAndDontSave;
                ViperWeaponInfos.Add(source, info);
            }
            return info;
        }

        private static void SetCapacitorCapacity(Aircraft aircraft)
        {
            PowerSupply powerSupply = aircraft?.GetComponentInChildren<PowerSupply>(true);
            if (powerSupply == null || PowerSupplyMaxCharge == null)
            {
                if (!loggedMissingPowerSupply)
                {
                    loggedMissingPowerSupply = true;
                    Plugin.ModLogger?.LogError(
                        "F-16VX Viper II could not locate PowerSupply.maxCharge.");
                }
                return;
            }
            PowerSupplyMaxCharge.SetValue(powerSupply, CapacitorCapacity);
            F16ViperIIJammers.RegisterViperPowerSupply(aircraft, powerSupply);
        }

        private static float GetCapacitorCapacity(Aircraft aircraft)
        {
            PowerSupply powerSupply = aircraft?.GetComponentInChildren<PowerSupply>(true);
            if (powerSupply == null || PowerSupplyMaxCharge == null)
            {
                return -1f;
            }
            return (float)PowerSupplyMaxCharge.GetValue(powerSupply);
        }

        private static void ConfigureFlares(Aircraft aircraft, WeaponManager weaponManager)
        {
            FlareEjector flare = aircraft?.GetComponentInChildren<FlareEjector>(true);
            if (flare == null || FlareMaxAmmo == null || FlareEjectionPoints == null ||
                FlareEjectionGrouping == null || FlareEjectionIndex == null)
            {
                LogFlareConfigurationFailure("the flare ejector or its runtime fields were unavailable");
                return;
            }

            FlareEjector.EjectionPoint[] points =
                FlareEjectionPoints.GetValue(flare) as FlareEjector.EjectionPoint[];
            if (points == null || points.Length == 0 || points[0]?.transform == null)
            {
                LogFlareConfigurationFailure("the original flare launch point was unavailable");
                return;
            }

            bool alreadyConfigured = HasConfiguredInnerWingPair(points);
            if (alreadyConfigured)
            {
                FlareEjectionIndex.SetValue(flare, 0);
                SynchronizeCountermeasureStation(aircraft.countermeasureManager, flare);
                return;
            }

            if (!TryFindInnerWingHardpoints(
                    weaponManager,
                    out Hardpoint leftHardpoint,
                    out Hardpoint rightHardpoint))
            {
                LogFlareConfigurationFailure("both inner-wing hardpoints could not be identified");
                return;
            }

            FlareEjector.EjectionPoint originalPoint = points[0];
            bool ammoWasAlreadyExpanded = HasGeneratedFlarePoint(points);
            int originalGrouping = Mathf.Max(
                1,
                (int)FlareEjectionGrouping.GetValue(flare));
            FlareEjector.EjectionPoint leftPoint =
                CreateFlarePoint(leftHardpoint, originalPoint, LeftFlarePointName);
            FlareEjector.EjectionPoint rightPoint =
                CreateFlarePoint(rightHardpoint, originalPoint, RightFlarePointName);
            FlareEjector.EjectionPoint[] augmentedPoints = BuildAugmentedFlareCycle(
                points,
                originalGrouping,
                leftPoint,
                rightPoint);

            int expandedAmmo = ammoWasAlreadyExpanded
                ? flare.ammo
                : Mathf.Max(0, Mathf.RoundToInt(flare.ammo * FlareAmmoMultiplier));
            flare.ammo = expandedAmmo;
            FlareMaxAmmo.SetValue(flare, expandedAmmo);
            FlareEjectionPoints.SetValue(flare, augmentedPoints);
            FlareEjectionGrouping.SetValue(
                flare,
                originalGrouping + InnerWingFlarePointCount);
            FlareEjectionIndex.SetValue(flare, 0);
            SynchronizeCountermeasureStation(aircraft.countermeasureManager, flare);
        }

        private static FlareEjector.EjectionPoint CreateFlarePoint(
            Hardpoint hardpoint,
            FlareEjector.EjectionPoint originalPoint,
            string name)
        {
            GameObject launchPoint = new GameObject(name);
            launchPoint.transform.SetParent(hardpoint.transform, false);
            launchPoint.transform.localPosition = Vector3.zero;
            launchPoint.transform.rotation = originalPoint.transform.rotation;
            return new FlareEjector.EjectionPoint
            {
                part = hardpoint.part ?? originalPoint.part,
                transform = launchPoint.transform
            };
        }

        private static FlareEjector.EjectionPoint[] BuildAugmentedFlareCycle(
            FlareEjector.EjectionPoint[] originalPoints,
            int originalGrouping,
            FlareEjector.EjectionPoint leftPoint,
            FlareEjector.EjectionPoint rightPoint)
        {
            int cycleSteps = originalPoints.Length /
                             GreatestCommonDivisor(originalPoints.Length, originalGrouping);
            int augmentedGrouping = originalGrouping + InnerWingFlarePointCount;
            FlareEjector.EjectionPoint[] cycle =
                new FlareEjector.EjectionPoint[cycleSteps * augmentedGrouping];
            int destination = 0;
            for (int step = 0; step < cycleSteps; step++)
            {
                int originalStart = step * originalGrouping;
                for (int i = 0; i < originalGrouping; i++)
                {
                    cycle[destination++] =
                        originalPoints[(originalStart + i) % originalPoints.Length];
                }
                cycle[destination++] = leftPoint;
                cycle[destination++] = rightPoint;
            }
            return cycle;
        }

        private static int GreatestCommonDivisor(int left, int right)
        {
            while (right != 0)
            {
                int remainder = left % right;
                left = right;
                right = remainder;
            }
            return Mathf.Max(1, Mathf.Abs(left));
        }

        private static bool FlareCycleMatchesStock(
            FlareEjector.EjectionPoint[] augmentedPoints,
            FlareEjector.EjectionPoint[] stockPoints,
            int stockGrouping)
        {
            if (augmentedPoints == null || stockPoints == null || stockPoints.Length == 0 ||
                stockGrouping <= 0)
            {
                return false;
            }
            int cycleSteps = stockPoints.Length /
                             GreatestCommonDivisor(stockPoints.Length, stockGrouping);
            int augmentedGrouping = stockGrouping + InnerWingFlarePointCount;
            if (augmentedPoints.Length != cycleSteps * augmentedGrouping)
            {
                return false;
            }
            for (int step = 0; step < cycleSteps; step++)
            {
                int destination = step * augmentedGrouping;
                int stockStart = step * stockGrouping;
                for (int i = 0; i < stockGrouping; i++)
                {
                    Transform actual = augmentedPoints[destination + i]?.transform;
                    Transform expected = stockPoints[(stockStart + i) % stockPoints.Length]?.transform;
                    if (actual == null || expected == null || actual.name != expected.name ||
                        Vector3.Distance(actual.localPosition, expected.localPosition) >= 0.001f)
                    {
                        return false;
                    }
                }
                if (augmentedPoints[destination + stockGrouping]?.transform?.name !=
                        LeftFlarePointName ||
                    augmentedPoints[destination + stockGrouping + 1]?.transform?.name !=
                        RightFlarePointName)
                {
                    return false;
                }
            }
            return true;
        }

        private static bool TryFindInnerWingHardpoints(
            WeaponManager weaponManager,
            out Hardpoint left,
            out Hardpoint right)
        {
            left = null;
            right = null;
            if (weaponManager?.hardpointSets == null)
            {
                return false;
            }

            foreach (HardpointSet set in weaponManager.hardpointSets)
            {
                if (set?.hardpoints == null || !Contains(set.name, "inner") ||
                    !Contains(set.name, "wing"))
                {
                    continue;
                }

                foreach (Hardpoint candidate in set.hardpoints)
                {
                    if (candidate?.transform == null)
                    {
                        continue;
                    }
                    if (left == null ||
                        candidate.transform.position.x < left.transform.position.x)
                    {
                        left = candidate;
                    }
                    if (right == null ||
                        candidate.transform.position.x > right.transform.position.x)
                    {
                        right = candidate;
                    }
                }
            }
            return left != null && right != null && left != right;
        }

        private static bool HasConfiguredInnerWingPair(FlareEjector.EjectionPoint[] points)
        {
            if (points == null || points.Length < InnerWingFlarePointCount)
            {
                return false;
            }
            bool left = false;
            bool right = false;
            foreach (FlareEjector.EjectionPoint point in points)
            {
                left |= point?.transform?.name == LeftFlarePointName;
                right |= point?.transform?.name == RightFlarePointName;
            }
            return left && right;
        }

        private static bool HasGeneratedFlarePoint(FlareEjector.EjectionPoint[] points)
        {
            foreach (FlareEjector.EjectionPoint point in points)
            {
                if (point?.transform?.name != null &&
                    point.transform.name.StartsWith(
                        FlarePointNamePrefix,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private static void SynchronizeCountermeasureStation(
            CountermeasureManager manager,
            FlareEjector flare)
        {
            IList stations = CountermeasureStations?.GetValue(manager) as IList;
            if (stations == null)
            {
                LogFlareConfigurationFailure("the countermeasure station list was unavailable");
                return;
            }

            foreach (object station in stations)
            {
                if (station == null)
                {
                    continue;
                }
                Type stationType = station.GetType();
                FieldInfo countermeasuresField = AccessTools.Field(stationType, "countermeasures");
                IList countermeasures = countermeasuresField?.GetValue(station) as IList;
                if (countermeasures == null || !countermeasures.Contains(flare))
                {
                    continue;
                }

                int totalAmmo = 0;
                foreach (object countermeasure in countermeasures)
                {
                    if (countermeasure is Countermeasure item)
                    {
                        totalAmmo += item.ammo;
                    }
                }
                AccessTools.Field(stationType, "ammo")?.SetValue(station, totalAmmo);
                AccessTools.Field(stationType, "maxAmmo")?.SetValue(station, totalAmmo);
                return;
            }
            LogFlareConfigurationFailure("the flare countermeasure station was not found");
        }

        private static void LogFlareConfigurationFailure(string reason)
        {
            if (loggedFlareConfigurationFailure)
            {
                return;
            }
            loggedFlareConfigurationFailure = true;
            Plugin.ModLogger?.LogError("F-16VX Viper II flare configuration failed: " + reason + ".");
        }

        private static void ClearLoadoutCenterline(Loadout loadout, int centerlineIndex)
        {
            if (loadout?.weapons != null && centerlineIndex >= 0 &&
                centerlineIndex < loadout.weapons.Count &&
                !F16ViperIIJammers.IsViperJammer(loadout.weapons[centerlineIndex]))
            {
                loadout.weapons[centerlineIndex] = null;
            }
        }

        internal static int FindCenterlineIndex(WeaponManager weaponManager)
        {
            if (weaponManager?.hardpointSets == null)
            {
                return -1;
            }
            for (int i = 0; i < weaponManager.hardpointSets.Length; i++)
            {
                string name = weaponManager.hardpointSets[i]?.name;
                if (Contains(name, "centerline") || Contains(name, "centreline"))
                {
                    return i;
                }
            }
            return -1;
        }

        private static bool Contains(string value, string expected)
        {
            return value != null &&
                   value.IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsSource(AircraftDefinition definition)
        {
            return definition != null &&
                   string.Equals(definition.jsonKey, SourceDefinitionKey, StringComparison.OrdinalIgnoreCase);
        }

        private static void RebuildEncyclopedia(Encyclopedia encyclopedia)
        {
            if (EncyclopediaAfterLoad == null)
            {
                Plugin.ModLogger?.LogError("Could not locate Encyclopedia.AfterLoad for F-16VX registration.");
                return;
            }
            rebuildingEncyclopedia = true;
            try
            {
                EncyclopediaAfterLoad.Invoke(encyclopedia, null);
            }
            finally
            {
                rebuildingEncyclopedia = false;
            }
        }

        private static bool ValidateVariant(
            Encyclopedia encyclopedia,
            int centerlineIndex,
            out string failure)
        {
            failure = string.Empty;
            if (viperDefinition == null || sourceDefinition == null ||
                viperDefinition == sourceDefinition ||
                viperDefinition.unitPrefab != sourceDefinition.unitPrefab)
            {
                failure = "definition was not cloned over the Blueprinter asset prefab";
                return false;
            }
            if (!string.Equals(viperDefinition.jsonKey, DefinitionKey, StringComparison.Ordinal) ||
                !string.Equals(viperDefinition.unitName, DisplayName, StringComparison.Ordinal) ||
                !Mathf.Approximately(viperDefinition.value, PurchaseCost))
            {
                failure = "identity, display name, or purchase value is incorrect";
                return false;
            }
            if (!F16ViperIIJammers.ValidateRegistration(encyclopedia, out failure))
            {
                return false;
            }
            if (!F16ViperIIJammers.ValidateLookups(encyclopedia, out failure))
            {
                return false;
            }
            if (!ValidateWeaponPrefabRegistration(encyclopedia, out failure))
            {
                return false;
            }

            Aircraft sourceAircraft = sourceDefinition.unitPrefab.GetComponent<Aircraft>() ??
                                      sourceDefinition.unitPrefab.GetComponentInChildren<Aircraft>(true);
            WeaponManager sourceManager = sourceAircraft?.weaponManager;
            if (sourceAircraft == null || sourceAircraft.definition != sourceDefinition ||
                sourceManager == null || centerlineIndex < 0 ||
                centerlineIndex >= sourceManager.hardpointSets.Length ||
                sourceManager.hardpointSets[centerlineIndex].weaponOptions == null ||
                sourceManager.hardpointSets[centerlineIndex].weaponOptions.Count == 0)
            {
                failure = "the original King Viper prefab was modified or its centerline is unavailable";
                return false;
            }
            return true;
        }

        private static bool ValidateWeaponPrefabRegistration(
            Encyclopedia encyclopedia,
            out string failure)
        {
            failure = string.Empty;
            if (ViperWeaponPrefabs.Count == 0 || Encyclopedia.WeaponLookup == null ||
                encyclopedia?.weaponMounts == null || encyclopedia.IndexLookup == null)
            {
                failure = "Viper II weapon prefabs or encyclopedia lookups are unavailable";
                return false;
            }

            HashSet<string> registeredAliases =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<WeaponMount, WeaponMount> entry in ViperWeaponPrefabs)
            {
                WeaponMount source = entry.Key;
                WeaponMount prefab = entry.Value;
                if (!TryGetWeaponAlias(source?.info, out WeaponAlias alias))
                {
                    failure = "a Viper II prefab no longer maps to a declared weapon alias";
                    return false;
                }
                registeredAliases.Add(alias.SourceCode);
                string expectedKey = WeaponPrefabKeyPrefix + source.jsonKey;
                Weapon[] weapons = prefab?.prefab?.GetComponentsInChildren<Weapon>(true);
                if (prefab == null || prefab == source || prefab.prefab == null ||
                    prefab.prefab == source.prefab || prefab.info == null ||
                    prefab.info == source.info ||
                    !string.Equals(prefab.jsonKey, expectedKey, StringComparison.Ordinal) ||
                    !string.Equals(
                        prefab.mountName,
                        GetViperMountDisplayName(source.mountName),
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        prefab.info.weaponName,
                        alias.DisplayName,
                        StringComparison.Ordinal) ||
                    !string.Equals(prefab.info.shortName, alias.ShortName, StringComparison.Ordinal) ||
                    prefab.prefab.activeSelf || weapons == null || weapons.Length == 0 ||
                    !encyclopedia.weaponMounts.Contains(prefab) ||
                    !Encyclopedia.WeaponLookup.TryGetValue(expectedKey, out WeaponMount lookup) ||
                    lookup != prefab || !encyclopedia.IndexLookup.Contains(prefab))
                {
                    failure = alias.SourceCode +
                              " is not an isolated, registered Viper II weapon prefab";
                    return false;
                }
                foreach (Weapon weapon in weapons)
                {
                    if (weapon.info != prefab.info)
                    {
                        failure = alias.SourceCode +
                                  " prefab contains a weapon using shared vanilla metadata";
                        return false;
                    }
                }
            }

            foreach (WeaponAlias alias in WeaponAliases)
            {
                if (!registeredAliases.Contains(alias.SourceCode))
                {
                    failure = alias.SourceCode + " has no registered Viper II weapon prefab";
                    return false;
                }
            }
            return ValidateSixJdamRegistration(encyclopedia, out failure);
        }

        private static bool ValidateSixJdamRegistration(
            Encyclopedia encyclopedia,
            out string failure)
        {
            failure = string.Empty;
            WeaponMount source = encyclopedia?.weaponMounts?.Find(candidate =>
                candidate != null && string.Equals(
                    candidate.jsonKey,
                    SixJdamSourceKey,
                    StringComparison.OrdinalIgnoreCase));
            Weapon[] weapons = sixJdamMount?.prefab?
                .GetComponentsInChildren<Weapon>(true);
            Transform root = sixJdamMount?.prefab?.transform;
            List<string> invalid = new List<string>();
            if (source == null || sixJdamMount == null || sixJdamMount == source)
            {
                invalid.Add("mount/source identity");
            }
            if (sixJdamMount?.prefab == null || sixJdamMount.prefab == source?.prefab ||
                sixJdamMount.info == null || sixJdamMount.info == source?.info)
            {
                invalid.Add("prefab/info isolation");
            }
            if (!string.Equals(sixJdamMount?.jsonKey, SixJdamMountKey, StringComparison.Ordinal) ||
                !string.Equals(sixJdamMount?.mountName, SixJdamMountName, StringComparison.Ordinal) ||
                !string.Equals(sixJdamMount?.info?.weaponName, SixJdamWeaponName,
                    StringComparison.Ordinal) ||
                !string.Equals(sixJdamMount?.info?.shortName, SixJdamShortName,
                    StringComparison.Ordinal) ||
                !Mathf.Approximately(sixJdamMount?.info?.costPerRound ?? -1f, 0.05f))
            {
                invalid.Add("names/cost[json=" + (sixJdamMount?.jsonKey ?? "<null>") +
                    ", mount=" + (sixJdamMount?.mountName ?? "<null>") +
                    ", weapon=" + (sixJdamMount?.info?.weaponName ?? "<null>") +
                    ", short=" + (sixJdamMount?.info?.shortName ?? "<null>") +
                    ", cost=" + (sixJdamMount?.info?.costPerRound ?? -1f) + "]");
            }
            if (sixJdamMount == null || sixJdamMount.ammo != 6 ||
                !Mathf.Approximately(sixJdamMount.mass, 3000f) ||
                !Mathf.Approximately(sixJdamMount.drag, 0.9f) ||
                !Mathf.Approximately(sixJdamMount.RCS, 0.7f) ||
                !Mathf.Approximately(sixJdamMount.emptyCost, 0f) ||
                !Mathf.Approximately(sixJdamMount.emptyMass, 0f) ||
                !Mathf.Approximately(sixJdamMount.emptyDrag, 0.02f) ||
                !Mathf.Approximately(sixJdamMount.emptyRCS, 0.007f))
            {
                invalid.Add("ammo/physical values[ammo=" + (sixJdamMount?.ammo ?? -1) +
                    ", mass=" + (sixJdamMount?.mass ?? -1f) +
                    ", drag=" + (sixJdamMount?.drag ?? -1f) +
                    ", rcs=" + (sixJdamMount?.RCS ?? -1f) +
                    ", emptyCost=" + (sixJdamMount?.emptyCost ?? -1f) +
                    ", emptyMass=" + (sixJdamMount?.emptyMass ?? -1f) +
                    ", emptyDrag=" + (sixJdamMount?.emptyDrag ?? -1f) +
                    ", emptyRcs=" + (sixJdamMount?.emptyRCS ?? -1f) + "]");
            }
            if (sixJdamMount?.prefab == null || sixJdamMount.prefab.activeSelf ||
                weapons == null || weapons.Length != 6 || root == null || root.childCount != 2)
            {
                invalid.Add("inactive six-store/two-rack hierarchy");
            }
            if (root == null || !VectorApproximately(root.localPosition, Vector3.zero) ||
                Quaternion.Angle(root.localRotation, Quaternion.identity) > 0.01f ||
                !VectorApproximately(root.localScale, Vector3.one))
            {
                invalid.Add("mount-root transform");
            }
            bool lookupValid = Encyclopedia.WeaponLookup != null &&
                Encyclopedia.WeaponLookup.TryGetValue(
                    SixJdamMountKey,
                    out WeaponMount lookup) &&
                lookup == sixJdamMount && encyclopedia.IndexLookup != null &&
                encyclopedia.IndexLookup.Contains(sixJdamMount);
            if (!lookupValid)
            {
                invalid.Add("encyclopedia lookup/index");
            }
            if (!ViperWeaponAliasesByKey.ContainsKey(SixJdamMountKey))
            {
                invalid.Add("Viper alias registration");
            }
            if (invalid.Count > 0)
            {
                failure = "the permanent GBU-38 x6 failed: " + string.Join(", ", invalid);
                return false;
            }
            foreach (Weapon weapon in weapons)
            {
                if (weapon.info != sixJdamMount.info)
                {
                    failure = "a permanent GBU-38 x6 store retained shared weapon metadata";
                    return false;
                }
            }
            if (!ValidateSixJdamAssembly(
                    root.GetChild(0),
                    "TGBR Rack Assembly 0",
                    new Vector3(0f, 0f, 1f),
                    out failure) ||
                !ValidateSixJdamAssembly(
                    root.GetChild(1),
                    "TGBR Rack Assembly 1",
                    new Vector3(0f, 0f, -1.3f),
                    out failure))
            {
                return false;
            }
            return true;
        }

        private static bool ValidateSixJdamAssembly(
            Transform assembly,
            string expectedName,
            Vector3 expectedPosition,
            out string failure)
        {
            failure = string.Empty;
            if (assembly == null || assembly.name != expectedName ||
                !VectorApproximately(assembly.localPosition, expectedPosition) ||
                Quaternion.Angle(assembly.localRotation, Quaternion.identity) > 0.01f ||
                !VectorApproximately(assembly.localScale, Vector3.one))
            {
                failure = expectedName + " transform does not match the exported specification";
                return false;
            }
            List<Transform> stores = new List<Transform>();
            List<Transform> adapters = new List<Transform>();
            for (int i = 0; i < assembly.childCount; i++)
            {
                Transform child = assembly.GetChild(i);
                if (child.GetComponentInChildren<Weapon>(true) != null)
                {
                    stores.Add(child);
                }
                else
                {
                    adapters.Add(child);
                }
            }
            if (stores.Count != 3 || adapters.Count != 1 ||
                !TransformApproximately(stores[0],
                    new Vector3(0f, -0.493f, 0.14f), Vector3.zero) ||
                !TransformApproximately(stores[1],
                    new Vector3(-0.288f, -0.216f, 0.14f),
                    new Vector3(0f, 0f, 300f)) ||
                !TransformApproximately(stores[2],
                    new Vector3(0.288f, -0.216f, 0.14f),
                    new Vector3(0f, 0f, 60f)) ||
                !TransformApproximately(adapters[0],
                    new Vector3(0f, 0.002f, 0.065f), Vector3.zero))
            {
                failure = expectedName +
                          " store or adapter transforms do not match the exported specification";
                return false;
            }
            return true;
        }

        private static bool TransformApproximately(
            Transform transform,
            Vector3 position,
            Vector3 rotation)
        {
            return transform != null &&
                   VectorApproximately(transform.localPosition, position) &&
                   Quaternion.Angle(transform.localRotation, Quaternion.Euler(rotation)) < 0.01f &&
                   VectorApproximately(transform.localScale, Vector3.one);
        }

        private static bool VectorApproximately(Vector3 left, Vector3 right)
        {
            return (left - right).sqrMagnitude < 0.000001f;
        }

        private static bool ValidateWeaponConfiguration(
            WeaponManager weaponManager,
            out string failure)
        {
            failure = string.Empty;
            if (weaponManager?.hardpointSets == null)
            {
                failure = "weapon manager is unavailable";
                return false;
            }
            HashSet<string> foundAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool foundSixJdamOnInnerWing = false;
            foreach (HardpointSet set in weaponManager.hardpointSets)
            {
                if (set?.weaponOptions == null)
                {
                    continue;
                }
                foreach (WeaponMount mount in set.weaponOptions)
                {
                    if (mount != null && string.Equals(
                            mount.jsonKey,
                            SixJdamMountKey,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        if (!IsInnerWingPylon(set))
                        {
                            failure = "GBU-38 JDAM x6 was available outside the inner-wing pylons";
                            return false;
                        }
                        foundSixJdamOnInnerWing = true;
                    }
                    if (IsRestrictedWeapon(mount))
                    {
                        failure = "IRM-S2 or AAM-36 remained in Viper II weapon options";
                        return false;
                    }
                    if (mount != null && ViperWeaponAliasesByKey.TryGetValue(
                            mount.jsonKey ?? string.Empty,
                            out WeaponAlias alias))
                    {
                        foundAliases.Add(alias.SourceCode);
                        if (!mount.mountName.StartsWith(
                                alias.DisplayName,
                                StringComparison.Ordinal))
                        {
                            failure = alias.SourceCode +
                                      " did not resolve to its Viper II weapon prefab";
                            return false;
                        }
                    }
                    else if (TryGetWeaponAlias(mount?.info, out WeaponAlias vanillaAlias))
                    {
                        failure = vanillaAlias.SourceCode +
                                  " remained a vanilla mount on a Viper II hardpoint";
                        return false;
                    }
                }
            }
            foreach (WeaponAlias alias in WeaponAliases)
            {
                if (!foundAliases.Contains(alias.SourceCode))
                {
                    failure = alias.SourceCode + " was not found on a Viper II hardpoint";
                    return false;
                }
            }
            if (!foundSixJdamOnInnerWing)
            {
                failure = "GBU-38 JDAM x6 was not available on the inner-wing pylons";
                return false;
            }
            return true;
        }

        private static bool ValidateSourceWeaponIsolation(
            WeaponManager sourceManager,
            out string failure)
        {
            failure = string.Empty;
            if (sourceManager?.hardpointSets == null)
            {
                failure = "King Viper weapon manager is unavailable";
                return false;
            }
            WeaponMount restrictedMount = null;
            WeaponMount vanillaAliasMount = null;
            int restrictedIndex = -1;
            for (int i = 0; i < sourceManager.hardpointSets.Length; i++)
            {
                HardpointSet set = sourceManager.hardpointSets[i];
                if (set == null || ViperHardpointSets.Contains(set))
                {
                    failure = "Viper II hardpoint filtering leaked into the King Viper prefab";
                    return false;
                }
                foreach (WeaponMount mount in set.weaponOptions ?? new List<WeaponMount>())
                {
                    if (restrictedMount == null && IsRestrictedWeapon(mount))
                    {
                        restrictedMount = mount;
                        restrictedIndex = i;
                    }
                    if (TryGetWeaponAlias(mount?.info, out WeaponAlias alias))
                    {
                        vanillaAliasMount = vanillaAliasMount ?? mount;
                        if (string.Equals(
                                mount.info.weaponName,
                                alias.DisplayName,
                                StringComparison.Ordinal))
                        {
                            failure = alias.SourceCode +
                                      " was globally renamed on the King Viper";
                            return false;
                        }
                    }
                }
            }
            if (restrictedMount == null || vanillaAliasMount == null)
            {
                failure = "the King Viper no longer retained its restricted or aliased weapons";
                return false;
            }

            Loadout hostileRequest = new Loadout { weapons = new List<WeaponMount>() };
            for (int i = 0; i < sourceManager.hardpointSets.Length; i++)
            {
                hostileRequest.weapons.Add(i == restrictedIndex ? restrictedMount : null);
            }
            SanitizeRestrictedWeapons(viperDefinition, hostileRequest);
            if (hostileRequest.weapons[restrictedIndex] != null)
            {
                failure = "server-side Viper II restriction did not clear a forbidden mount";
                return false;
            }

            Loadout vanillaAliasRequest = new Loadout
            {
                weapons = new List<WeaponMount> { vanillaAliasMount }
            };
            SanitizeRestrictedWeapons(viperDefinition, vanillaAliasRequest);
            if (vanillaAliasRequest.weapons[0] == vanillaAliasMount ||
                !IsViperWeaponPrefab(vanillaAliasRequest.weapons[0]))
            {
                failure = "server-side Viper II loadout normalization retained a vanilla weapon";
                return false;
            }
            return true;
        }

        private static void LogConfiguredInstance()
        {
            if (loggedConfiguredInstance)
            {
                return;
            }
            loggedConfiguredInstance = true;
            Plugin.ModLogger?.LogInfo(
                "F-16VX Viper II instance configured successfully from the Blueprinter asset prefab.");
        }

        private static bool SmokeValidateSixJdamDeployment(
            Aircraft aircraft,
            WeaponManager weaponManager,
            out string failure)
        {
            failure = string.Empty;
            HardpointSet innerWing = null;
            foreach (HardpointSet set in weaponManager?.hardpointSets ??
                     Array.Empty<HardpointSet>())
            {
                if (IsInnerWingPylon(set))
                {
                    innerWing = set;
                    break;
                }
            }
            if (aircraft == null || innerWing?.hardpoints == null ||
                innerWing.hardpoints.Count == 0 || sixJdamMount == null ||
                innerWing.weaponOptions == null ||
                !innerWing.weaponOptions.Contains(sixJdamMount) ||
                !WeaponChecker.MountAllowedHardpoint(sixJdamMount, innerWing))
            {
                failure = "the permanent GBU-38 x6 is not accepted by the inner-wing pylon";
                return false;
            }

            Hardpoint hardpoint = innerWing.hardpoints[0];
            GameObject spawned = null;
            try
            {
                spawned = hardpoint.SpawnMount(aircraft, sixJdamMount);
                Weapon[] stores = spawned?.GetComponentsInChildren<Weapon>(true);
                if (spawned == null || !spawned.activeInHierarchy ||
                    stores == null || stores.Length != 6 ||
                    spawned.transform.childCount != 2)
                {
                    failure = "the permanent GBU-38 x6 disappeared or had the wrong hierarchy " +
                              "during physical hardpoint spawning";
                    return false;
                }
                foreach (Weapon store in stores)
                {
                    if (store.info != sixJdamMount.info)
                    {
                        failure = "a physically spawned GBU-38 x6 store used the wrong metadata";
                        return false;
                    }
                }
                return true;
            }
            catch (Exception exception)
            {
                failure = "physical GBU-38 x6 deployment threw: " + exception.Message;
                return false;
            }
            finally
            {
                hardpoint?.RemoveMount();
            }
        }

        private static void RunSmokeProbeIfRequested()
        {
            bool requested = false;
            foreach (string argument in Environment.GetCommandLineArgs())
            {
                if (string.Equals(
                        argument,
                        "--tgbr-gui-smoke-test",
                        StringComparison.OrdinalIgnoreCase))
                {
                    requested = true;
                    break;
                }
            }
            if (!requested)
            {
                return;
            }

            GameObject probe = null;
            bool entered = EnterVariantContext(viperDefinition);
            try
            {
                probe = SmokeInstantiateVariant();
            }
            finally
            {
                ExitVariantContext(entered);
            }

            Aircraft aircraft = probe?.GetComponent<Aircraft>() ??
                                probe?.GetComponentInChildren<Aircraft>(true);
            WeaponManager weaponManager = aircraft?.weaponManager;
            int centerlineIndex = FindCenterlineIndex(weaponManager);
            Renderer[] renderers = probe?.GetComponentsInChildren<Renderer>(true) ??
                                   Array.Empty<Renderer>();
            int visibleRenderers = 0;
            foreach (Renderer renderer in renderers)
            {
                if (renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy)
                {
                    visibleRenderers++;
                }
            }
            Type rigidbodyType = AccessTools.TypeByName("UnityEngine.Rigidbody");
            bool hasRigidbody = rigidbodyType != null &&
                                probe?.GetComponentInChildren(rigidbodyType, true) != null;
            string centerlineFailure = centerlineIndex < 0
                ? "centerline pylon was not found"
                : string.Empty;
            bool centerlineJammersPassed = centerlineIndex >= 0 &&
                F16ViperIIJammers.ValidateCenterline(
                    weaponManager.hardpointSets[centerlineIndex],
                    out centerlineFailure);
            bool spawnedJammerInfoPassed =
                F16ViperIIJammers.SmokeValidateSpawnedWeaponInfo(
                    out string spawnedJammerFailure);
            bool serverVettingPassed =
                F16ViperIIJammers.SmokeValidateServerVetting(
                    viperDefinition,
                    out string serverVettingFailure);
            string physicalJammerMountFailure = centerlineIndex < 0
                ? "centerline pylon was not found"
                : string.Empty;
            bool physicalJammerMountPassed = centerlineIndex >= 0 &&
                F16ViperIIJammers.SmokeValidatePhysicalCenterlineMount(
                    aircraft,
                    weaponManager.hardpointSets[centerlineIndex],
                    out physicalJammerMountFailure);
            Plugin.ModLogger?.LogInfo(
                "F-16VX smoke stage: physical jammer=" + physicalJammerMountPassed + ".");
            bool weaponConfigurationPassed = ValidateWeaponConfiguration(
                weaponManager,
                out string weaponConfigurationFailure);
            Plugin.ModLogger?.LogInfo(
                "F-16VX smoke stage: weapon prefabs=" + weaponConfigurationPassed +
                ", detail=" + (weaponConfigurationFailure ?? string.Empty) + ".");
            bool sixJdamDeploymentPassed = SmokeValidateSixJdamDeployment(
                aircraft,
                weaponManager,
                out string sixJdamDeploymentFailure);
            Plugin.ModLogger?.LogInfo(
                "F-16VX smoke stage: GBU-38 x6 physical deployment=" +
                sixJdamDeploymentPassed + ", detail=" +
                (sixJdamDeploymentFailure ?? string.Empty) + ".");
            Aircraft sourceAircraftForWeapons = sourceDefinition?.unitPrefab?
                .GetComponent<Aircraft>() ?? sourceDefinition?.unitPrefab?
                .GetComponentInChildren<Aircraft>(true);
            bool sourceWeaponIsolationPassed = ValidateSourceWeaponIsolation(
                sourceAircraftForWeapons?.weaponManager,
                out string sourceWeaponIsolationFailure);
            Plugin.ModLogger?.LogInfo(
                "F-16VX smoke stage: King Viper isolation=" +
                sourceWeaponIsolationPassed + ", detail=" +
                (sourceWeaponIsolationFailure ?? string.Empty) + ".");

            FlareEjector sourceFlare = sourceDefinition?.unitPrefab?
                .GetComponentInChildren<FlareEjector>(true);
            FlareEjector probeFlare = aircraft?.GetComponentInChildren<FlareEjector>(true);
            FlareEjector.EjectionPoint[] sourceFlarePoints =
                FlareEjectionPoints?.GetValue(sourceFlare) as FlareEjector.EjectionPoint[];
            FlareEjector.EjectionPoint[] probeFlarePoints =
                FlareEjectionPoints?.GetValue(probeFlare) as FlareEjector.EjectionPoint[];
            int flareGrouping = probeFlare != null && FlareEjectionGrouping != null
                ? (int)FlareEjectionGrouping.GetValue(probeFlare)
                : -1;
            int sourceFlareGrouping = sourceFlare != null && FlareEjectionGrouping != null
                ? (int)FlareEjectionGrouping.GetValue(sourceFlare)
                : -1;
            int flareMaximum = probeFlare != null && FlareMaxAmmo != null
                ? (int)FlareMaxAmmo.GetValue(probeFlare)
                : -1;
            float sourceFlareVelocity = sourceFlare != null && FlareEjectionVelocity != null
                ? (float)FlareEjectionVelocity.GetValue(sourceFlare)
                : float.NaN;
            float probeFlareVelocity = probeFlare != null && FlareEjectionVelocity != null
                ? (float)FlareEjectionVelocity.GetValue(probeFlare)
                : float.NaN;
            bool foundInnerWingPair = TryFindInnerWingHardpoints(
                weaponManager,
                out Hardpoint leftInnerWing,
                out Hardpoint rightInnerWing);
            int firstWingPointIndex = sourceFlareGrouping;
            bool flareConfigurationPassed = sourceFlare != null && probeFlare != null &&
                probeFlare.ammo ==
                    Mathf.RoundToInt(sourceFlare.ammo * FlareAmmoMultiplier) &&
                flareMaximum == probeFlare.ammo &&
                probeFlarePoints != null &&
                flareGrouping == sourceFlareGrouping + InnerWingFlarePointCount &&
                HasConfiguredInnerWingPair(probeFlarePoints) &&
                FlareCycleMatchesStock(
                    probeFlarePoints,
                    sourceFlarePoints,
                    sourceFlareGrouping) &&
                foundInnerWingPair &&
                probeFlarePoints[firstWingPointIndex].transform.parent ==
                    leftInnerWing.transform &&
                probeFlarePoints[firstWingPointIndex + 1].transform.parent ==
                    rightInnerWing.transform &&
                Quaternion.Angle(
                    probeFlarePoints[firstWingPointIndex].transform.rotation,
                    sourceFlarePoints[0].transform.rotation) < 0.01f &&
                Quaternion.Angle(
                    probeFlarePoints[firstWingPointIndex + 1].transform.rotation,
                    sourceFlarePoints[0].transform.rotation) < 0.01f &&
                Mathf.Approximately(sourceFlareVelocity, probeFlareVelocity);
            Plugin.ModLogger?.LogInfo(
                "F-16VX smoke stage: flare configuration=" + flareConfigurationPassed + ".");

            bool passed = probe != null && probe.activeInHierarchy &&
                          aircraft != null && IsVariant(aircraft.definition) &&
                          hasRigidbody &&
                          visibleRenderers > 0 && centerlineJammersPassed &&
                          spawnedJammerInfoPassed &&
                          serverVettingPassed &&
                          physicalJammerMountPassed &&
                          weaponConfigurationPassed &&
                          sixJdamDeploymentPassed &&
                          sourceWeaponIsolationPassed &&
                          flareConfigurationPassed &&
                          Mathf.Approximately(
                              GetCapacitorCapacity(aircraft),
                              CapacitorCapacity);
            if (passed)
            {
                Plugin.ModLogger?.LogInfo(
                    "F-16VX Blueprinter instance smoke test passed: active aircraft, " +
                    visibleRenderers + " visible renderer(s), rigidbody, three $27m centerline " +
                    "jammer suites and NATO weapon prefabs that survive server validation, " +
                    "physically mount at " +
                    "72% radial/90% length scale, stay out of weapon stations, and register " +
                    "zero Unit.Jam channels for ADJ/MiM and one independently proven channel " +
                    "for OJS through authoritative missing-pod recovery without invoking " +
                    "the vanilla weapon lifecycle or adding electrical draw; " +
                    "Radar ECM consumes 160% of stock charge; ADJ reduces that cost by 30% " +
                    "and raises capacitor generation to 130%; OJS halves the adjusted cost " +
                    "to 80% of stock, " +
                    "holds full effect through depletion, auto-disarms radar, and remains " +
                    "locked until 300 kJ; " +
                    "ten registered Viper II-only NATO weapon families plus the permanent " +
                    "six-store GBU-38 inner-wing rack, and no " +
                    "IRM-S2/AAM-36; " +
                    "King Viper weapon data remains isolated; " +
                    CapacitorCapacity + " kJ capacitor; flares " +
                    sourceFlare.ammo + " -> " +
                    probeFlare.ammo + " with the stock " + sourceFlareGrouping + "-flare cycle " +
                    "plus simultaneous left/right inner-wing points, original velocity " +
                    probeFlareVelocity + ", and " + flareGrouping + " consumed per dispense.");
            }
            else
            {
                Plugin.ModLogger?.LogError(
                    "F-16VX Blueprinter instance smoke test failed: active=" +
                    (probe != null && probe.activeInHierarchy) + ", definition=" +
                    (aircraft?.definition?.jsonKey ?? "<none>") + ", visibleRenderers=" +
                    visibleRenderers + ", centerlineIndex=" + centerlineIndex +
                    ", centerlineJammers=" + centerlineJammersPassed +
                    ", centerlineFailure=" + (centerlineFailure ?? "<none>") +
                    ", spawnedJammerInfo=" + spawnedJammerInfoPassed +
                    ", spawnedJammerFailure=" + (spawnedJammerFailure ?? "<none>") +
                    ", serverVetting=" + serverVettingPassed +
                    ", serverVettingFailure=" + (serverVettingFailure ?? "<none>") +
                    ", physicalJammerMount=" + physicalJammerMountPassed +
                    ", physicalJammerMountFailure=" +
                    (physicalJammerMountFailure ?? "<none>") +
                    ", weaponConfiguration=" + weaponConfigurationPassed +
                    ", weaponConfigurationFailure=" +
                    (weaponConfigurationFailure ?? "<none>") +
                    ", sixJdamDeployment=" + sixJdamDeploymentPassed +
                    ", sixJdamDeploymentFailure=" +
                    (sixJdamDeploymentFailure ?? "<none>") +
                    ", sourceWeaponIsolation=" + sourceWeaponIsolationPassed +
                    ", sourceWeaponIsolationFailure=" +
                    (sourceWeaponIsolationFailure ?? "<none>") +
                    ", capacitor=" + GetCapacitorCapacity(aircraft) + ", flareAmmo=" +
                    (probeFlare?.ammo ?? -1) + ", flareMax=" + flareMaximum +
                    ", flarePoints=" + (probeFlarePoints?.Length ?? -1) +
                    ", sourceFlareGrouping=" + sourceFlareGrouping +
                    ", flareGrouping=" + flareGrouping + ", sourceFlareVelocity=" +
                    sourceFlareVelocity + ", probeFlareVelocity=" + probeFlareVelocity + ".");
            }

            if (probe != null)
            {
                UnityEngine.Object.Destroy(probe);
            }
        }

        private static GameObject SmokeInstantiateVariant()
        {
            return UnityEngine.Object.Instantiate(sourceDefinition.unitPrefab);
        }

        private static void LogPylonNames(WeaponManager weaponManager)
        {
            if (weaponManager?.hardpointSets == null)
            {
                return;
            }
            List<string> names = new List<string>();
            foreach (HardpointSet set in weaponManager.hardpointSets)
            {
                names.Add(set?.name ?? "<null>");
            }
            Plugin.ModLogger?.LogWarning(
                "F-16M centerline pylon was not identified. Pylons: " + string.Join(", ", names));
        }
    }

    [HarmonyPatch]
    internal static class F16ViperIIEncyclopediaPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Encyclopedia), "AfterLoad", Type.EmptyTypes);
        }

        private static void Postfix(Encyclopedia __instance)
        {
            F16ViperII.RegisterEncyclopedia(__instance);
        }
    }

    [HarmonyPatch(typeof(Hangar), nameof(Hangar.GetAvailableAircraft))]
    internal static class F16ViperIIHangarAvailabilityPatch
    {
        private static void Postfix(ref AircraftDefinition[] __result)
        {
            __result = F16ViperII.AddVariantWhenSourceAvailable(__result);
        }
    }

    [HarmonyPatch(typeof(Hangar), nameof(Hangar.CanSpawnAircraft))]
    internal static class F16ViperIIHangarSpawnPatch
    {
        private static void Postfix(Hangar __instance, AircraftDefinition definition, ref bool __result)
        {
            if (!__result && F16ViperII.IsVariant(definition))
            {
                __result = Array.IndexOf(__instance.GetAvailableAircraft(), definition) >= 0;
            }
        }
    }

    [HarmonyPatch(typeof(WeaponManager), "Awake")]
    internal static class F16ViperIIWeaponManagerPatch
    {
        private static void Prefix(WeaponManager __instance)
        {
            F16ViperII.ApplyRuntimeRestrictions(__instance);
        }
    }

    [HarmonyPatch(typeof(WeaponManager), nameof(WeaponManager.SpawnWeapons))]
    internal static class F16ViperIIRuntimeLoadoutPatch
    {
        private static void Prefix(WeaponManager __instance)
        {
            F16ViperII.PrepareRuntimeLoadout(__instance);
        }

        private static void Postfix(WeaponManager __instance)
        {
            F16ViperIIJammers.FinalizeRuntimeDeployment(__instance);
        }
    }

    [HarmonyPatch(typeof(WeaponSelector), nameof(WeaponSelector.SetValue))]
    internal static class F16ViperIIWeaponSelectorValuePatch
    {
        private static void Prefix(WeaponSelector __instance, ref WeaponMount weaponMount)
        {
            F16ViperII.SanitizeSelectorValue(__instance, ref weaponMount);
        }
    }

    [HarmonyPatch(typeof(Hardpoint), nameof(Hardpoint.SpawnMount))]
    internal static class F16ViperIIWeaponPrefabActivationPatch
    {
        private static void Postfix(WeaponMount weaponMount, GameObject __result)
        {
            F16ViperII.ActivateSpawnedWeaponPrefab(weaponMount, __result);
        }
    }

    [HarmonyPatch]
    internal static class F16ViperIINetworkRegistrationPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                AccessTools.TypeByName("NuclearOption.Networking.NetworkManagerNuclearOption"),
                "RegisterPrefabs",
                Type.EmptyTypes);
        }

        private static void Prefix()
        {
            F16ViperII.ExcludeSharedPrefabFromNetworkRegistration();
        }

        private static Exception Finalizer(Exception __exception)
        {
            F16ViperII.RestoreAfterNetworkRegistration();
            return __exception;
        }
    }

    [HarmonyPatch]
    internal static class F16ViperIICreationContextPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(
                typeof(AircraftSelectionMenu),
                "SpawnPreview",
                Type.EmptyTypes);

            Type previewGenerator = AccessTools.TypeByName(
                "NuclearOption.MissionEditorScripts.Buttons.UnitPreviewGenerator");
            yield return AccessTools.Method(
                previewGenerator,
                "Render",
                new[] { typeof(UnitDefinition) });

            Type newUnitPanel = AccessTools.TypeByName(
                "NuclearOption.MissionEditorScripts.Buttons.NewUnitPanel");
            yield return AccessTools.Method(
                newUnitPanel,
                "SpawnUnit",
                new[] { typeof(UnitDefinition) });

            yield return AccessTools.Method(
                typeof(EncyclopediaBrowser),
                nameof(EncyclopediaBrowser.SpawnAircraft),
                new[] { typeof(UnitDefinition) });

            foreach (MethodInfo method in AccessTools.GetDeclaredMethods(typeof(Hangar)))
            {
                if (method.Name == "SpawnAircraft" && method.ReturnType == typeof(void))
                {
                    yield return method;
                }
            }

            foreach (MethodInfo method in AccessTools.GetDeclaredMethods(typeof(Spawner)))
            {
                ParameterInfo[] parameters = method.GetParameters();
                if (method.Name == "TrySpawnAircraft" && parameters.Length == 3 &&
                    parameters[0].ParameterType == typeof(SavedAircraft))
                {
                    yield return method;
                }
            }
        }

        private static void Prefix(MethodBase __originalMethod, object __instance, object[] __args, out bool __state)
        {
            __state = false;
            if (__originalMethod.DeclaringType == typeof(AircraftSelectionMenu))
            {
                __state = F16ViperII.EnterSelectorContext(__instance as AircraftSelectionMenu);
                return;
            }

            foreach (object argument in __args)
            {
                if (argument is AircraftDefinition aircraftDefinition)
                {
                    __state = F16ViperII.EnterVariantContext(aircraftDefinition);
                    return;
                }
                if (argument is UnitDefinition unitDefinition)
                {
                    __state = F16ViperII.EnterVariantContext(
                        unitDefinition as AircraftDefinition);
                    return;
                }
                if (argument is SavedAircraft savedAircraft)
                {
                    __state = F16ViperII.EnterVariantContext(savedAircraft.type);
                    return;
                }
            }
        }

        private static Exception Finalizer(bool __state, Exception __exception)
        {
            F16ViperII.ExitVariantContext(__state);
            return __exception;
        }
    }

    [HarmonyPatch]
    internal static class F16ViperIIInstanceConfigurationPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(
                typeof(F16ViperII),
                "SmokeInstantiateVariant",
                Type.EmptyTypes);

            yield return AccessTools.Method(
                typeof(AircraftSelectionMenu),
                "SpawnPreview",
                Type.EmptyTypes);

            foreach (MethodInfo method in AccessTools.GetDeclaredMethods(typeof(Spawner)))
            {
                if (method.Name == nameof(Spawner.SpawnAircraft) &&
                    method.ReturnType == typeof(Aircraft) &&
                    method.GetParameters().Length == 13)
                {
                    yield return method;
                }
            }

            Type previewGenerator = AccessTools.TypeByName(
                "NuclearOption.MissionEditorScripts.Buttons.UnitPreviewGenerator");
            yield return AccessTools.Method(
                previewGenerator,
                "Render",
                new[] { typeof(UnitDefinition) });

            Type newUnitPanel = AccessTools.TypeByName(
                "NuclearOption.MissionEditorScripts.Buttons.NewUnitPanel");
            yield return AccessTools.Method(
                newUnitPanel,
                "SpawnUnit",
                new[] { typeof(UnitDefinition) });
        }

        private static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo configure = AccessTools.Method(
                typeof(F16ViperII),
                nameof(F16ViperII.ConfigureCreatedInstance));
            foreach (CodeInstruction instruction in instructions)
            {
                yield return instruction;
                if ((instruction.opcode == System.Reflection.Emit.OpCodes.Call ||
                     instruction.opcode == System.Reflection.Emit.OpCodes.Callvirt) &&
                    instruction.operand is MethodInfo called &&
                    called.Name == nameof(UnityEngine.Object.Instantiate) &&
                    called.ReturnType == typeof(GameObject))
                {
                    yield return new CodeInstruction(
                        System.Reflection.Emit.OpCodes.Call,
                        configure);
                }
            }
        }
    }

    [HarmonyPatch]
    internal static class F16ViperIIClientDefinitionPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Aircraft), "OnStartClient", Type.EmptyTypes);
        }

        private static void Prefix(Aircraft __instance)
        {
            F16ViperII.ConfigureNetworkClient(__instance);
        }
    }
}

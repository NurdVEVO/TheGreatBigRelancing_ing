using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace TheGreatBigRebalancing.Tools
{
    /// <summary>
    /// Creates session-only, fully functional weapon-mount prototypes for balancing work.
    /// Printed output is intended to be promoted into deterministic mod code before release,
    /// so arbitrary local workshop state can never silently become multiplayer content.
    /// </summary>
    internal sealed class WeaponMountWorkshop : IDisposable
    {
        private enum WorkshopBuildMode
        {
            IndividualStores,
            CompleteRackCopies
        }

        private const int MaximumStores = 64;
        private const int MaximumRackAssemblies = 16;
        private const string RackAssemblyNamePrefix = "TGBR Rack Assembly ";
        private const string StoreAssemblyName = "TGBR Individual Store Assembly";
        private static readonly MethodInfo EncyclopediaAfterLoad =
            AccessTools.Method(typeof(Encyclopedia), "AfterLoad", Type.EmptyTypes);
        private static readonly HashSet<WeaponMount> PrototypeMounts =
            new HashSet<WeaponMount>();
        private static readonly Dictionary<WeaponMount, List<GameObject>> SpawnedInstances =
            new Dictionary<WeaponMount, List<GameObject>>();

        private sealed class HierarchyNode
        {
            internal Transform Transform;
            internal string Role;
            internal int Depth;
        }

        private sealed class PrototypeRecord
        {
            internal WeaponMount SourceMount;
            internal WorkshopDefinition Definition;
        }

        private readonly ManualLogSource log;
        private readonly List<WeaponMount> ownedPrototypes = new List<WeaponMount>();
        private readonly Dictionary<WeaponMount, PrototypeRecord> prototypeRecords =
            new Dictionary<WeaponMount, PrototypeRecord>();
        private readonly List<HierarchyNode> hierarchyNodes = new List<HierarchyNode>();

        private WeaponMount sourceMount;
        private WeaponMount prototypeMount;
        private WorkshopBuildMode buildMode = WorkshopBuildMode.IndividualStores;
        private int selectedSourceStoreIndex;
        private string jsonKey = string.Empty;
        private string mountName = string.Empty;
        private string weaponName = string.Empty;
        private string shortName = string.Empty;
        private string individualStoreCount = "1";
        private string rackAssemblyCount = "2";
        private string assemblyOffsetX = "0";
        private string assemblyOffsetY = "0";
        private string assemblyOffsetZ = "-0.65";
        private string perStoreCost = "0";
        private string fullMass = "0";
        private string fullDrag = "0";
        private string fullRcs = "0";
        private string emptyCost = "0";
        private string emptyMass = "0";
        private string emptyDrag = "0";
        private string emptyRcs = "0";
        private bool keepSourceAdapters = true;

        internal WeaponMountWorkshop(ManualLogSource log)
        {
            this.log = log;
        }

        internal static bool IsPrototypeMount(WeaponMount mount)
        {
            return mount != null && PrototypeMounts.Contains(mount);
        }

        internal static void ActivateSpawnedPrototype(WeaponMount mount, GameObject spawned)
        {
            if (spawned != null && IsPrototypeMount(mount))
            {
                spawned.SetActive(true);
                if (!SpawnedInstances.TryGetValue(mount, out List<GameObject> instances))
                {
                    instances = new List<GameObject>();
                    SpawnedInstances.Add(mount, instances);
                }
                instances.RemoveAll(instance => instance == null);
                if (!instances.Contains(spawned))
                {
                    instances.Add(spawned);
                }
            }
        }

        private bool SelectMountSearchResult(WeaponMount selected, out string result)
        {
            if (IsPrototypeMount(selected))
            {
                return SelectPrototype(selected, out result);
            }

            SelectSource(selected);
            result = "Mount workshop source selected: " + Label(selected) + ".";
            return true;
        }

        private void SelectSource(WeaponMount selected)
        {
            sourceMount = selected;
            int existingCount = Math.Max(1, CountStores(selected));
            buildMode = WorkshopBuildMode.IndividualStores;
            selectedSourceStoreIndex = 0;
            const int targetCount = 1;
            float scale = targetCount / (float)existingCount;

            jsonKey = MakeUniquePrototypeKey(
                MakePrototypeKey(selected?.jsonKey, targetCount));
            mountName = WithStoreCount(selected?.mountName ?? selected?.name, targetCount);
            weaponName = selected?.info?.weaponName ?? selected?.mountName ?? "Custom weapon";
            shortName = selected?.info?.shortName ?? weaponName;
            individualStoreCount = targetCount.ToString(CultureInfo.InvariantCulture);
            rackAssemblyCount = "2";
            keepSourceAdapters = true;
            perStoreCost = Format(selected?.info?.costPerRound ?? 0f);
            fullMass = Format((selected?.mass ?? 0f) * scale);
            fullDrag = Format((selected?.drag ?? 0f) * scale);
            fullRcs = Format((selected?.RCS ?? 0f) * scale);
            emptyCost = Format(selected?.emptyCost ?? 0f);
            emptyMass = Format(selected?.emptyMass ?? 0f);
            emptyDrag = Format(selected?.emptyDrag ?? 0f);
            emptyRcs = Format(selected?.emptyRCS ?? 0f);
            prototypeMount = null;
            hierarchyNodes.Clear();
        }

        private bool SelectPrototype(WeaponMount selected, out string result)
        {
            result = string.Empty;
            if (selected == null ||
                !prototypeRecords.TryGetValue(selected, out PrototypeRecord record) ||
                record?.SourceMount == null)
            {
                result = "The selected preview no longer has its source-mount record.";
                return false;
            }

            sourceMount = record.SourceMount;
            prototypeMount = selected;
            buildMode = record.Definition.BuildMode;
            selectedSourceStoreIndex = record.Definition.SourceStoreIndex;
            jsonKey = selected.jsonKey ?? string.Empty;
            mountName = selected.mountName ?? selected.name;
            weaponName = selected.info?.weaponName ?? mountName;
            shortName = selected.info?.shortName ?? weaponName;
            individualStoreCount = record.Definition.TotalStores.ToString(
                CultureInfo.InvariantCulture);
            rackAssemblyCount = record.Definition.RackAssemblyCount.ToString(
                CultureInfo.InvariantCulture);
            keepSourceAdapters = record.Definition.KeepSourceAdapters;
            SetVectorTextFromFields(record.Definition.AssemblyOffset);
            perStoreCost = Format(selected.info?.costPerRound ?? 0f);
            fullMass = Format(selected.mass);
            fullDrag = Format(selected.drag);
            fullRcs = Format(selected.RCS);
            emptyCost = Format(selected.emptyCost);
            emptyMass = Format(selected.emptyMass);
            emptyDrag = Format(selected.emptyDrag);
            emptyRcs = Format(selected.emptyRCS);
            RefreshHierarchyNodes();
            result = "Resumed editing created preview " + Label(selected) + ".";
            return true;
        }

        private void SetVectorTextFromFields(Vector3 assemblyOffset)
        {
            assemblyOffsetX = Format(assemblyOffset.x);
            assemblyOffsetY = Format(assemblyOffset.y);
            assemblyOffsetZ = Format(assemblyOffset.z);
        }

        private bool TryCreatePrototype(
            HardpointSet selectedSet,
            List<WeaponMount> availableMounts,
            out string result)
        {
            result = string.Empty;
            if (selectedSet == null)
            {
                result = "Select a destination pylon set before creating the preview.";
                return false;
            }
            if (!TryReadDefinition(out WorkshopDefinition definition, out result))
            {
                return false;
            }
            Encyclopedia encyclopedia = Encyclopedia.i;
            if (encyclopedia?.weaponMounts == null || EncyclopediaAfterLoad == null)
            {
                result = "Weapon workshop cannot access Encyclopedia weapon registration.";
                return false;
            }
            if (encyclopedia.weaponMounts.Exists(candidate => candidate != null &&
                    string.Equals(candidate.jsonKey, definition.JsonKey,
                        StringComparison.OrdinalIgnoreCase)))
            {
                result = "JSON key already exists: " + definition.JsonKey + ".";
                return false;
            }

            Weapon[] sourceWeapons = sourceMount?.prefab?
                .GetComponentsInChildren<Weapon>(true);
            if (sourceWeapons == null || sourceWeapons.Length == 0)
            {
                result = "The source mount prefab contains no physical Weapon components.";
                return false;
            }
            if (definition.BuildMode == WorkshopBuildMode.CompleteRackCopies &&
                definition.StoresPerAssembly != sourceWeapons.Length)
            {
                result = "The source mount hierarchy changed while the rack definition was open.";
                return false;
            }

            WeaponInfo clonedInfo = UnityEngine.Object.Instantiate(sourceMount.info);
            clonedInfo.name = definition.JsonKey + "_Info";
            clonedInfo.weaponName = definition.WeaponName;
            clonedInfo.shortName = definition.ShortName;
            clonedInfo.costPerRound = definition.PerStoreCost;
            clonedInfo.hideFlags = HideFlags.HideAndDontSave;

            GameObject clonedPrefab = null;
            WeaponMount clonedMount = null;
            try
            {
                clonedPrefab = CloneInactivePrefab(sourceMount.prefab);
                clonedPrefab.name = definition.JsonKey + "_Prefab";
                clonedPrefab.hideFlags = HideFlags.HideAndDontSave;
                if (definition.BuildMode == WorkshopBuildMode.IndividualStores)
                {
                    BuildIndividualStoreHierarchy(
                        clonedPrefab,
                        definition.SourceStoreIndex,
                        definition.TotalStores,
                        definition.AssemblyOffset,
                        definition.KeepSourceAdapters);
                }
                else
                {
                    BuildRackAssemblyHierarchy(
                        clonedPrefab,
                        definition.RackAssemblyCount,
                        definition.AssemblyOffset);
                }
                foreach (Weapon weapon in clonedPrefab.GetComponentsInChildren<Weapon>(true))
                {
                    weapon.info = clonedInfo;
                }
                clonedPrefab.SetActive(false);
                UnityEngine.Object.DontDestroyOnLoad(clonedPrefab);

                clonedMount = UnityEngine.Object.Instantiate(sourceMount);
                clonedMount.name = definition.JsonKey;
                clonedMount.jsonKey = definition.JsonKey;
                clonedMount.mountName = definition.MountName;
                clonedMount.info = clonedInfo;
                clonedMount.prefab = clonedPrefab;
                clonedMount.ammo = definition.TotalStores;
                ApplyPhysicalValues(clonedMount, definition);
                clonedMount.hideFlags = HideFlags.HideAndDontSave;
                clonedMount.dontAutomaticallyAddToEncyclopedia = false;

                encyclopedia.weaponMounts.Add(clonedMount);
                EncyclopediaAfterLoad.Invoke(encyclopedia, null);
                if (Encyclopedia.WeaponLookup == null ||
                    !Encyclopedia.WeaponLookup.TryGetValue(
                        definition.JsonKey,
                        out WeaponMount lookup) ||
                    lookup != clonedMount ||
                    encyclopedia.IndexLookup == null ||
                    !encyclopedia.IndexLookup.Contains(clonedMount))
                {
                    throw new InvalidOperationException(
                        "Encyclopedia lookup validation rejected the cloned mount");
                }
            }
            catch (Exception exception)
            {
                if (clonedMount != null)
                {
                    encyclopedia.weaponMounts.Remove(clonedMount);
                }
                bool lookupsRecovered = TryRebuildEncyclopedia(
                    encyclopedia,
                    out string recoveryFailure);
                if (clonedPrefab != null)
                {
                    UnityEngine.Object.Destroy(clonedPrefab);
                }
                UnityEngine.Object.Destroy(clonedInfo);
                if (clonedMount != null)
                {
                    UnityEngine.Object.Destroy(clonedMount);
                }
                result = "Weapon prototype creation failed: " +
                         (exception.InnerException?.Message ?? exception.Message) +
                         (lookupsRecovered
                             ? string.Empty
                             : " Encyclopedia rollback also failed: " + recoveryFailure + ".");
                log.LogError(result);
                return false;
            }

            PrototypeMounts.Add(clonedMount);
            ownedPrototypes.Add(clonedMount);
            prototypeRecords.Add(clonedMount, new PrototypeRecord
            {
                SourceMount = sourceMount,
                Definition = definition
            });
            prototypeMount = clonedMount;
            availableMounts.Add(clonedMount);
            availableMounts.Sort((left, right) => string.Compare(
                Label(left), Label(right), StringComparison.OrdinalIgnoreCase));
            selectedSet.weaponOptions = selectedSet.weaponOptions ?? new List<WeaponMount>();
            if (!selectedSet.weaponOptions.Contains(clonedMount))
            {
                selectedSet.weaponOptions.Add(clonedMount);
            }
            RefreshHierarchyNodes();
            result = "Created and registered session preview " + Label(clonedMount) +
                     " with " + definition.TotalStores + " mounted store" +
                     (definition.TotalStores == 1 ? string.Empty : "s") + " using " +
                     BuildModeLabel(definition.BuildMode) + " on " + selectedSet.name + ".";
            return true;
        }

        private bool TryApplyMetadata(out string result)
        {
            result = string.Empty;
            if (prototypeMount == null)
            {
                result = "No workshop prototype exists.";
                return false;
            }
            if (!TryReadDefinition(out WorkshopDefinition definition, out result))
            {
                return false;
            }
            if (!string.Equals(
                    definition.JsonKey,
                    prototypeMount.jsonKey,
                    StringComparison.Ordinal))
            {
                result = "A registered preview's JSON key cannot be changed. Create a new " +
                         "preview to use " + definition.JsonKey + ".";
                return false;
            }
            int physicalStores = CountStores(prototypeMount);
            if (definition.TotalStores != physicalStores)
            {
                result = "Store/rack count changes require creating a new preview; this one " +
                         "contains " + physicalStores + " mounted stores.";
                return false;
            }

            prototypeMount.mountName = definition.MountName;
            prototypeMount.ammo = physicalStores;
            ApplyPhysicalValues(prototypeMount, definition);
            prototypeMount.info.weaponName = definition.WeaponName;
            prototypeMount.info.shortName = definition.ShortName;
            prototypeMount.info.costPerRound = definition.PerStoreCost;
            foreach (Weapon weapon in prototypeMount.prefab.GetComponentsInChildren<Weapon>(true))
            {
                weapon.info = prototypeMount.info;
            }
            if (prototypeRecords.TryGetValue(prototypeMount, out PrototypeRecord record))
            {
                record.Definition = definition;
            }
            result = "Applied metadata and physical values to " + Label(prototypeMount) + ".";
            return true;
        }

        private void RefreshHierarchyNodes()
        {
            hierarchyNodes.Clear();
            Transform root = prototypeMount?.prefab?.transform;
            if (root == null)
            {
                return;
            }

            hierarchyNodes.Add(new HierarchyNode
            {
                Transform = root,
                Role = "Mount root",
                Depth = 0
            });
            for (int assemblyIndex = 0; assemblyIndex < root.childCount; assemblyIndex++)
            {
                Transform assembly = root.GetChild(assemblyIndex);
                hierarchyNodes.Add(new HierarchyNode
                {
                    Transform = assembly,
                    Role = assembly.name == StoreAssemblyName
                        ? "Store assembly"
                        : "Rack assembly",
                    Depth = 1
                });
                for (int childIndex = 0; childIndex < assembly.childCount; childIndex++)
                {
                    Transform child = assembly.GetChild(childIndex);
                    bool mountedStore = child.GetComponentInChildren<Weapon>(true) != null;
                    hierarchyNodes.Add(new HierarchyNode
                    {
                        Transform = child,
                        Role = mountedStore ? "Mounted store" : "Rack / adapter",
                        Depth = 2
                    });
                }
            }
        }

        private void ApplyHierarchyTransform(
            Transform target,
            Vector3 position,
            Vector3 rotation,
            Vector3 scale)
        {
            Transform root = prototypeMount?.prefab?.transform;
            int[] path = GetSiblingIndexPath(root, target);
            if (path == null)
            {
                return;
            }

            ApplyLocalTransform(target, position, rotation, scale);
            if (!SpawnedInstances.TryGetValue(
                    prototypeMount,
                    out List<GameObject> spawnedInstances))
            {
                return;
            }
            spawnedInstances.RemoveAll(instance => instance == null);
            for (int i = 0; i < spawnedInstances.Count; i++)
            {
                Transform counterpart = FollowSiblingIndexPath(
                    spawnedInstances[i].transform,
                    path);
                if (counterpart != null)
                {
                    ApplyLocalTransform(counterpart, position, rotation, scale);
                }
            }
        }

        private static void ApplyLocalTransform(
            Transform target,
            Vector3 position,
            Vector3 rotation,
            Vector3 scale)
        {
            if ((target.localPosition - position).sqrMagnitude > 0.00000001f)
            {
                target.localPosition = position;
            }
            Quaternion desiredRotation = Quaternion.Euler(rotation);
            if (Quaternion.Angle(target.localRotation, desiredRotation) > 0.001f)
            {
                target.localRotation = desiredRotation;
            }
            if ((target.localScale - scale).sqrMagnitude > 0.00000001f)
            {
                target.localScale = scale;
            }
        }

        private static int[] GetSiblingIndexPath(Transform root, Transform target)
        {
            if (root == null || target == null)
            {
                return null;
            }
            List<int> reversed = new List<int>();
            Transform current = target;
            while (current != root)
            {
                if (current.parent == null)
                {
                    return null;
                }
                reversed.Add(current.GetSiblingIndex());
                current = current.parent;
            }
            reversed.Reverse();
            return reversed.ToArray();
        }

        private static Transform FollowSiblingIndexPath(Transform root, int[] path)
        {
            Transform current = root;
            for (int i = 0; i < path.Length; i++)
            {
                if (current == null || path[i] < 0 || path[i] >= current.childCount)
                {
                    return null;
                }
                current = current.GetChild(path[i]);
            }
            return current;
        }

        private string PrintAndCopy(string aircraftLabel, HardpointSet selectedSet)
        {
            string specification = FormatSpecification(aircraftLabel, selectedSet);
            log.LogInfo(specification);
            try
            {
                GUIUtility.systemCopyBuffer = specification;
                return "Printed and copied the weapon prototype specification.";
            }
            catch (Exception exception)
            {
                log.LogWarning("Weapon specification clipboard copy failed: " + exception.Message);
                return "Printed weapon specification; clipboard copy failed: " + exception.Message;
            }
        }

        private string FormatSpecification(string aircraftLabel, HardpointSet selectedSet)
        {
            StringBuilder output = new StringBuilder();
            output.AppendLine("TGBR WEAPON MOUNT CONFIGURATION");
            output.AppendLine("Aircraft: " + aircraftLabel);
            output.AppendLine("Pylon: " + (selectedSet?.name ?? "<none>"));
            output.AppendLine("SourceJsonKey: " + (sourceMount?.jsonKey ?? "<none>"));
            output.AppendLine("JsonKey: " + (prototypeMount?.jsonKey ?? "<none>"));
            output.AppendLine("MountName: " + (prototypeMount?.mountName ?? "<none>"));
            output.AppendLine("WeaponName: " + (prototypeMount?.info?.weaponName ?? "<none>"));
            output.AppendLine("ShortName: " + (prototypeMount?.info?.shortName ?? "<none>"));
            output.AppendLine("Ammo: " + (prototypeMount?.ammo ?? 0));
            if (prototypeMount != null &&
                prototypeRecords.TryGetValue(prototypeMount, out PrototypeRecord record))
            {
                output.AppendLine("BuildMode: " + BuildModeLabel(record.Definition.BuildMode));
                output.AppendLine("KeepSourceAdapters: " +
                                  record.Definition.KeepSourceAdapters);
                output.AppendLine("SourceStoreIndex: " +
                                  record.Definition.SourceStoreIndex);
            }
            output.AppendLine("RackAssemblies: " +
                              (prototypeMount?.prefab?.transform.childCount ?? 0));
            output.AppendLine(
                "PerStoreCost: " + Format(prototypeMount?.info?.costPerRound ?? 0f));
            output.AppendLine("FullMass: " + Format(prototypeMount?.mass ?? 0f));
            output.AppendLine("FullDrag: " + Format(prototypeMount?.drag ?? 0f));
            output.AppendLine("FullRCS: " + Format(prototypeMount?.RCS ?? 0f));
            output.AppendLine("EmptyCost: " + Format(prototypeMount?.emptyCost ?? 0f));
            output.AppendLine("EmptyMass: " + Format(prototypeMount?.emptyMass ?? 0f));
            output.AppendLine("EmptyDrag: " + Format(prototypeMount?.emptyDrag ?? 0f));
            output.AppendLine("EmptyRCS: " + Format(prototypeMount?.emptyRCS ?? 0f));
            if (hierarchyNodes.Count == 0)
            {
                RefreshHierarchyNodes();
            }
            for (int i = 0; i < hierarchyNodes.Count; i++)
            {
                HierarchyNode node = hierarchyNodes[i];
                if (node?.Transform == null)
                {
                    continue;
                }
                Vector3 position = node.Transform.localPosition;
                Vector3 rotation = node.Transform.localEulerAngles;
                Vector3 scale = node.Transform.localScale;
                output.AppendLine("Node[" + i + "] " + node.Role + ": " +
                                  GetRelativePath(prototypeMount.prefab.transform, node.Transform));
                output.AppendLine("  Position: (" + Format(position.x) + ", " +
                                  Format(position.y) + ", " + Format(position.z) + ")");
                output.AppendLine("  Rotation: (" + Format(rotation.x) + ", " +
                                  Format(rotation.y) + ", " + Format(rotation.z) + ")");
                output.AppendLine("  Scale: (" + Format(scale.x) + ", " +
                                  Format(scale.y) + ", " + Format(scale.z) + ")");
            }
            return output.ToString().TrimEnd();
        }

        private static string BuildModeLabel(WorkshopBuildMode mode)
        {
            return mode == WorkshopBuildMode.IndividualStores
                ? "individual stores"
                : "complete rack copies";
        }

        private string MakeUniquePrototypeKey(string requested)
        {
            string baseKey = NormalizeKey(requested);
            if (string.IsNullOrEmpty(baseKey))
            {
                baseKey = "TGBR_WORKSHOP_NEW_WEAPON";
            }
            if (!JsonKeyExists(baseKey, null))
            {
                return baseKey;
            }
            for (int suffix = 2; suffix < 10000; suffix++)
            {
                string candidate = baseKey + "_" + suffix;
                if (!JsonKeyExists(candidate, null))
                {
                    return candidate;
                }
            }
            return baseKey + "_" + Guid.NewGuid().ToString("N");
        }

        private static bool JsonKeyExists(string key, WeaponMount excluded)
        {
            if (string.IsNullOrEmpty(key))
            {
                return false;
            }
            List<WeaponMount> mounts = Encyclopedia.i?.weaponMounts;
            return mounts != null && mounts.Exists(candidate =>
                candidate != null &&
                candidate != excluded &&
                string.Equals(candidate.jsonKey, key, StringComparison.OrdinalIgnoreCase));
        }

        private bool TryReadDefinition(
            out WorkshopDefinition definition,
            out string failure)
        {
            definition = default;
            failure = string.Empty;
            if (sourceMount?.prefab == null || sourceMount.info == null)
            {
                failure = "Select a source with both a mount prefab and WeaponInfo.";
                return false;
            }
            string normalizedKey = NormalizeKey(jsonKey);
            if (string.IsNullOrEmpty(normalizedKey) ||
                string.IsNullOrWhiteSpace(mountName) ||
                string.IsNullOrWhiteSpace(weaponName) ||
                string.IsNullOrWhiteSpace(shortName))
            {
                failure = "JSON key, mount name, weapon name, and short name are required.";
                return false;
            }
            int sourceStores = CountStores(sourceMount);
            if (sourceStores < 1)
            {
                failure = "The source mount has no mounted stores.";
                return false;
            }
            int parsedAssemblyCount;
            int totalStores;
            int storesPerAssembly;
            if (buildMode == WorkshopBuildMode.IndividualStores)
            {
                if (!CanIsolateIndividualStore(
                        sourceMount.prefab,
                        selectedSourceStoreIndex,
                        out failure))
                {
                    return false;
                }
                if (!int.TryParse(
                        individualStoreCount,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out totalStores) ||
                    totalStores < 1 || totalStores > MaximumStores)
                {
                    failure = "Individual store count must be between 1 and " +
                              MaximumStores + ".";
                    return false;
                }
                parsedAssemblyCount = 1;
                storesPerAssembly = totalStores;
            }
            else
            {
                if (!int.TryParse(
                        rackAssemblyCount,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out parsedAssemblyCount) ||
                    parsedAssemblyCount < 1 ||
                    parsedAssemblyCount > MaximumRackAssemblies ||
                    parsedAssemblyCount * sourceStores > MaximumStores)
                {
                    failure = "Rack copy count must produce between 1 and " + MaximumStores +
                              " total mounted stores.";
                    return false;
                }
                storesPerAssembly = sourceStores;
                totalStores = parsedAssemblyCount * sourceStores;
            }
            if (!TryParseFloat(perStoreCost, out float parsedPerStoreCost) ||
                !TryParseFloat(fullMass, out float parsedFullMass) ||
                !TryParseFloat(fullDrag, out float parsedFullDrag) ||
                !TryParseFloat(fullRcs, out float parsedFullRcs) ||
                !TryParseFloat(emptyCost, out float parsedEmptyCost) ||
                !TryParseFloat(emptyMass, out float parsedEmptyMass) ||
                !TryParseFloat(emptyDrag, out float parsedEmptyDrag) ||
                !TryParseFloat(emptyRcs, out float parsedEmptyRcs) ||
                !TryParseVector(
                    new[] { assemblyOffsetX, assemblyOffsetY, assemblyOffsetZ },
                    out Vector3 parsedAssemblyOffset))
            {
                failure = "Cost, physical values, and bank offsets must be valid numbers.";
                return false;
            }
            if (parsedPerStoreCost < 0f || parsedFullMass < 0f || parsedFullDrag < 0f ||
                parsedFullRcs < 0f || parsedEmptyCost < 0f || parsedEmptyMass < 0f ||
                parsedEmptyDrag < 0f || parsedEmptyRcs < 0f)
            {
                failure = "Cost, mass, drag, and RCS values cannot be negative.";
                return false;
            }

            jsonKey = normalizedKey;
            definition = new WorkshopDefinition
            {
                JsonKey = normalizedKey,
                MountName = mountName.Trim(),
                WeaponName = weaponName.Trim(),
                ShortName = shortName.Trim(),
                BuildMode = buildMode,
                KeepSourceAdapters = keepSourceAdapters,
                SourceStoreIndex = selectedSourceStoreIndex,
                RackAssemblyCount = parsedAssemblyCount,
                StoresPerAssembly = storesPerAssembly,
                TotalStores = totalStores,
                AssemblyOffset = parsedAssemblyOffset,
                PerStoreCost = parsedPerStoreCost,
                FullMass = parsedFullMass,
                FullDrag = parsedFullDrag,
                FullRcs = parsedFullRcs,
                EmptyCost = parsedEmptyCost,
                EmptyMass = parsedEmptyMass,
                EmptyDrag = parsedEmptyDrag,
                EmptyRcs = parsedEmptyRcs
            };
            return true;
        }

        private static GameObject CloneInactivePrefab(GameObject source)
        {
            bool sourceWasActive = source.activeSelf;
            try
            {
                if (sourceWasActive)
                {
                    source.SetActive(false);
                }
                return UnityEngine.Object.Instantiate(source);
            }
            finally
            {
                if (sourceWasActive)
                {
                    source.SetActive(true);
                }
            }
        }

        private static void BuildIndividualStoreHierarchy(
            GameObject prefab,
            int sourceStoreIndex,
            int storeCount,
            Vector3 storeOffset,
            bool keepAdapters)
        {
            Transform root = prefab.transform;
            Weapon[] sourceWeapons = prefab.GetComponentsInChildren<Weapon>(true);
            if (sourceWeapons.Length == 0)
            {
                throw new InvalidOperationException(
                    "Individual-store source contains no physical Weapon component");
            }

            if (sourceStoreIndex < 0 || sourceStoreIndex >= sourceWeapons.Length)
            {
                throw new InvalidOperationException(
                    "The selected source-store index is no longer valid");
            }

            Transform sourceStore = DirectChildBelow(
                root,
                sourceWeapons[sourceStoreIndex].transform);
            if (sourceStore == null ||
                sourceStore.GetComponentsInChildren<Weapon>(true).Length != 1)
            {
                throw new InvalidOperationException(
                    "Individual-store mode requires a separately rooted physical store");
            }

            List<Transform> sourceChildren = new List<Transform>();
            for (int i = 0; i < root.childCount; i++)
            {
                sourceChildren.Add(root.GetChild(i));
            }

            GameObject assemblyObject = new GameObject(StoreAssemblyName);
            Transform assembly = assemblyObject.transform;
            assembly.SetParent(root, false);
            assembly.localPosition = Vector3.zero;
            assembly.localRotation = Quaternion.identity;
            assembly.localScale = Vector3.one;

            for (int i = 0; i < sourceChildren.Count; i++)
            {
                Transform child = sourceChildren[i];
                bool containsWeapon =
                    child.GetComponentsInChildren<Weapon>(true).Length > 0;
                if (child == sourceStore || (!containsWeapon && keepAdapters))
                {
                    child.SetParent(assembly, false);
                    continue;
                }

                child.gameObject.SetActive(false);
                child.SetParent(null, false);
                UnityEngine.Object.Destroy(child.gameObject);
            }

            Vector3 originalPosition = sourceStore.localPosition;
            sourceStore.name = "TGBR Store 0";
            for (int storeIndex = 1; storeIndex < storeCount; storeIndex++)
            {
                Transform copy = UnityEngine.Object.Instantiate(
                    sourceStore.gameObject,
                    assembly,
                    false).transform;
                copy.name = "TGBR Store " + storeIndex;
                copy.localPosition = originalPosition + storeOffset * storeIndex;
                copy.localRotation = sourceStore.localRotation;
                copy.localScale = sourceStore.localScale;
            }

            int actualStores = prefab.GetComponentsInChildren<Weapon>(true).Length;
            if (root.childCount != 1 || actualStores != storeCount)
            {
                throw new InvalidOperationException(
                    "Individual-store hierarchy produced " + actualStores +
                    " stores; expected " + storeCount);
            }
        }

        private static bool CanIsolateIndividualStore(
            GameObject prefab,
            int sourceStoreIndex,
            out string failure)
        {
            failure = string.Empty;
            Weapon[] weapons = prefab?.GetComponentsInChildren<Weapon>(true);
            if (weapons == null || weapons.Length == 0)
            {
                failure = "The selected source contains no physical mounted store.";
                return false;
            }
            if (sourceStoreIndex < 0 || sourceStoreIndex >= weapons.Length)
            {
                failure = "Choose a valid physical source store.";
                return false;
            }
            Transform branch = DirectChildBelow(
                prefab.transform,
                weapons[sourceStoreIndex].transform);
            if (branch == null || branch.GetComponentsInChildren<Weapon>(true).Length != 1)
            {
                failure = "This source groups multiple weapons inside one inseparable hierarchy " +
                          "branch. Use Complete rack copies or choose a single-store source.";
                return false;
            }
            return true;
        }

        private static Transform DirectChildBelow(Transform root, Transform descendant)
        {
            if (root == null || descendant == null || descendant == root)
            {
                return null;
            }
            Transform current = descendant;
            while (current.parent != null && current.parent != root)
            {
                current = current.parent;
            }
            return current.parent == root ? current : null;
        }

        private static void BuildRackAssemblyHierarchy(
            GameObject prefab,
            int rackAssemblyCount,
            Vector3 assemblyOffset)
        {
            Transform root = prefab.transform;
            List<Transform> sourceChildren = new List<Transform>();
            for (int i = 0; i < root.childCount; i++)
            {
                sourceChildren.Add(root.GetChild(i));
            }

            GameObject assemblyObject = new GameObject(RackAssemblyNamePrefix + "0");
            Transform sourceAssembly = assemblyObject.transform;
            sourceAssembly.SetParent(root, false);
            sourceAssembly.localPosition = Vector3.zero;
            sourceAssembly.localRotation = Quaternion.identity;
            sourceAssembly.localScale = Vector3.one;
            for (int i = 0; i < sourceChildren.Count; i++)
            {
                sourceChildren[i].SetParent(sourceAssembly, false);
            }

            int storesPerAssembly = sourceAssembly.GetComponentsInChildren<Weapon>(true).Length;
            if (storesPerAssembly == 0)
            {
                throw new InvalidOperationException(
                    "Source rack assembly contains no mounted Weapon components");
            }

            for (int assemblyIndex = 1;
                 assemblyIndex < rackAssemblyCount;
                 assemblyIndex++)
            {
                Transform copy = UnityEngine.Object.Instantiate(
                    sourceAssembly.gameObject,
                    root,
                    false).transform;
                copy.name = RackAssemblyNamePrefix + assemblyIndex;
                copy.localPosition = assemblyOffset * assemblyIndex;
                copy.localRotation = sourceAssembly.localRotation;
                copy.localScale = sourceAssembly.localScale;
            }

            int expectedStores = storesPerAssembly * rackAssemblyCount;
            int actualStores = prefab.GetComponentsInChildren<Weapon>(true).Length;
            if (root.childCount != rackAssemblyCount || actualStores != expectedStores)
            {
                throw new InvalidOperationException(
                    "Rack hierarchy produced " + root.childCount + " assemblies and " +
                    actualStores + " stores; expected " + rackAssemblyCount + " and " +
                    expectedStores);
            }
        }

        private static void ApplyPhysicalValues(
            WeaponMount mount,
            WorkshopDefinition definition)
        {
            mount.mass = definition.FullMass;
            mount.drag = definition.FullDrag;
            mount.RCS = definition.FullRcs;
            mount.emptyCost = definition.EmptyCost;
            mount.emptyMass = definition.EmptyMass;
            mount.emptyDrag = definition.EmptyDrag;
            mount.emptyRCS = definition.EmptyRcs;
        }

        private bool TryRebuildEncyclopedia(
            Encyclopedia encyclopedia,
            out string failure)
        {
            failure = string.Empty;
            if (encyclopedia == null)
            {
                failure = "the Encyclopedia instance is unavailable";
                log.LogError("Weapon workshop could not rebuild Encyclopedia lookups: " + failure + ".");
                return false;
            }
            if (EncyclopediaAfterLoad == null)
            {
                failure = "Encyclopedia.AfterLoad was not found";
                log.LogError("Weapon workshop could not rebuild Encyclopedia lookups: " + failure + ".");
                return false;
            }
            try
            {
                EncyclopediaAfterLoad.Invoke(encyclopedia, null);
                return true;
            }
            catch (Exception exception)
            {
                failure = exception.InnerException?.Message ?? exception.Message;
                log.LogError(
                    "Weapon workshop could not rebuild Encyclopedia lookups: " + failure + ".");
                return false;
            }
        }

        private static int CountStores(WeaponMount mount)
        {
            return mount?.prefab == null
                ? 0
                : mount.prefab.GetComponentsInChildren<Weapon>(true).Length;
        }

        private static string MakePrototypeKey(string sourceKey, int count)
        {
            return NormalizeKey("TGBR_WORKSHOP_" + (sourceKey ?? "weapon") + "_x" + count);
        }

        private static string NormalizeKey(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }
            StringBuilder normalized = new StringBuilder();
            foreach (char character in value.Trim())
            {
                normalized.Append(char.IsLetterOrDigit(character) || character == '_'
                    ? character
                    : '_');
            }
            return normalized.ToString();
        }

        private static string WithStoreCount(string value, int count)
        {
            string source = string.IsNullOrWhiteSpace(value) ? "Custom weapon" : value.Trim();
            int marker = source.LastIndexOf(" x", StringComparison.OrdinalIgnoreCase);
            if (marker >= 0 && int.TryParse(
                    source.Substring(marker + 2),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out _))
            {
                source = source.Substring(0, marker);
            }
            return source + " x" + count;
        }

        private static string Label(WeaponMount mount)
        {
            if (mount == null)
            {
                return "<none>";
            }
            string label = string.IsNullOrEmpty(mount.mountName) ? mount.name : mount.mountName;
            return label + " [" + mount.jsonKey + "]";
        }

        private static string GetRelativePath(Transform root, Transform target)
        {
            if (root == null || target == null)
            {
                return "<none>";
            }
            if (root == target)
            {
                return root.name;
            }
            Stack<string> names = new Stack<string>();
            Transform current = target;
            while (current != null && current != root)
            {
                names.Push(current.name);
                current = current.parent;
            }
            if (current != root)
            {
                return "<outside mount root>";
            }
            return root.name + "/" + string.Join("/", names.ToArray());
        }

        private static bool TryParseFloat(string text, out float value)
        {
            return float.TryParse(
                text,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value) && !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool TryParseVector(string[] text, out Vector3 value)
        {
            value = Vector3.zero;
            if (text == null || text.Length != 3 ||
                !TryParseFloat(text[0], out float x) ||
                !TryParseFloat(text[1], out float y) ||
                !TryParseFloat(text[2], out float z))
            {
                return false;
            }
            value = new Vector3(x, y, z);
            return true;
        }

        private static string Format(float value)
        {
            return value.ToString("0.######", CultureInfo.InvariantCulture);
        }

        internal static bool RunSelfTest()
        {
            return EncyclopediaAfterLoad != null &&
                   NormalizeKey(" TGBR GBU-38/x6 ") == "TGBR_GBU_38_x6" &&
                   WithStoreCount("GBU-38 JDAM x3", 6) == "GBU-38 JDAM x6" &&
                   BuildModeLabel(WorkshopBuildMode.IndividualStores) ==
                       "individual stores" &&
                   TryParseVector(new[] { "0", "-0.2", "0.65" }, out Vector3 vector) &&
                   Mathf.Approximately(vector.y, -0.2f) &&
                   Mathf.Approximately(vector.z, 0.65f);
        }

        internal bool RunLiveSmokeTest(
            HardpointSet testSet,
            List<WeaponMount> availableMounts,
            out string failure)
        {
            failure = string.Empty;
            if (testSet == null || availableMounts == null)
            {
                failure = "test pylon or available-mount list is unavailable";
                return false;
            }

            WeaponMount candidate = availableMounts.Find(mount =>
                mount?.prefab != null &&
                mount.info != null &&
                CountStores(mount) == 3 &&
                Label(mount).IndexOf("GBU-38", StringComparison.OrdinalIgnoreCase) >= 0);
            if (candidate == null)
            {
                failure = "the Viper II three-store GBU-38 source mount was not registered";
                return false;
            }

            SelectSource(candidate);
            buildMode = WorkshopBuildMode.CompleteRackCopies;
            const int expectedAssemblies = 2;
            const int expectedStores = 6;
            rackAssemblyCount = expectedAssemblies.ToString(CultureInfo.InvariantCulture);
            jsonKey = NormalizeKey(
                "TGBR_WORKSHOP_SMOKE_" + candidate.jsonKey + "_x" + expectedStores);
            if (!TryCreatePrototype(testSet, availableMounts, out failure))
            {
                return false;
            }

            WeaponMount created = prototypeMount;
            bool reopenPassed = prototypeRecords.ContainsKey(created) &&
                                availableMounts.Contains(created) &&
                                SelectMountSearchResult(created, out _) &&
                                prototypeMount == created &&
                                sourceMount == candidate &&
                                hierarchyNodes.Count > 0;
            GameObject activationProbe = UnityEngine.Object.Instantiate(created.prefab);
            ActivateSpawnedPrototype(created, activationProbe);
            Transform firstAssembly = created.prefab.transform.childCount > 0
                ? created.prefab.transform.GetChild(0)
                : null;
            Transform firstSpawnedAssembly = activationProbe.transform.childCount > 0
                ? activationProbe.transform.GetChild(0)
                : null;
            Vector3 liveProbePosition = new Vector3(0.123f, -0.234f, 0.345f);
            if (firstAssembly != null)
            {
                ApplyHierarchyTransform(
                    firstAssembly,
                    liveProbePosition,
                    firstAssembly.localEulerAngles,
                    firstAssembly.localScale);
            }
            bool rackChildrenPresent = true;
            for (int assemblyIndex = 0;
                 assemblyIndex < created.prefab.transform.childCount;
                 assemblyIndex++)
            {
                Transform assembly = created.prefab.transform.GetChild(assemblyIndex);
                bool foundRackOrAdapter = false;
                for (int childIndex = 0; childIndex < assembly.childCount; childIndex++)
                {
                    if (assembly.GetChild(childIndex)
                            .GetComponentInChildren<Weapon>(true) == null)
                    {
                        foundRackOrAdapter = true;
                        break;
                    }
                }
                rackChildrenPresent &= foundRackOrAdapter;
            }
            bool passed = created != null &&
                          created.prefab != null &&
                          !created.prefab.activeSelf &&
                          created.prefab.transform.childCount == expectedAssemblies &&
                          CountStores(created) == expectedStores &&
                          created.ammo == expectedStores &&
                          rackChildrenPresent &&
                          reopenPassed &&
                          PrototypeMounts.Contains(created) &&
                          testSet.weaponOptions != null &&
                          testSet.weaponOptions.Contains(created) &&
                          Encyclopedia.WeaponLookup != null &&
                          Encyclopedia.WeaponLookup.TryGetValue(
                              created.jsonKey,
                              out WeaponMount lookup) &&
                          lookup == created &&
                          Encyclopedia.i?.IndexLookup != null &&
                          Encyclopedia.i.IndexLookup.Contains(created) &&
                          activationProbe.activeSelf &&
                          firstSpawnedAssembly != null &&
                          (firstSpawnedAssembly.localPosition - liveProbePosition).sqrMagnitude <
                              0.000001f;
            UnityEngine.Object.Destroy(activationProbe);

            string createdKey = created.jsonKey;
            RemovePrototype(created, availableMounts, testSet);
            bool cleanupPassed = !PrototypeMounts.Contains(created) &&
                                 !SpawnedInstances.ContainsKey(created) &&
                                 !ownedPrototypes.Contains(created) &&
                                 !prototypeRecords.ContainsKey(created) &&
                                 !availableMounts.Contains(created) &&
                                 (testSet.weaponOptions == null ||
                                  !testSet.weaponOptions.Contains(created)) &&
                                 (Encyclopedia.i?.weaponMounts == null ||
                                  !Encyclopedia.i.weaponMounts.Contains(created)) &&
                                 (Encyclopedia.WeaponLookup == null ||
                                  !Encyclopedia.WeaponLookup.ContainsKey(createdKey));
            prototypeMount = null;
            hierarchyNodes.Clear();
            if (!passed || !cleanupPassed)
            {
                failure = !passed
                    ? "two-rack/six-store hierarchy failed rack duplication, live transform, " +
                      "lookup, pylon, or activation validation"
                    : "temporary two-rack/six-store clone was not fully removed after validation";
                return false;
            }

            SelectSource(candidate);
            buildMode = WorkshopBuildMode.IndividualStores;
            individualStoreCount = "1";
            keepSourceAdapters = true;
            jsonKey = NormalizeKey(
                "TGBR_WORKSHOP_SMOKE_SINGLE_" + candidate.jsonKey);
            if (!TryCreatePrototype(testSet, availableMounts, out failure))
            {
                return false;
            }

            WeaponMount single = prototypeMount;
            Transform singleAssembly = single?.prefab?.transform.childCount == 1
                ? single.prefab.transform.GetChild(0)
                : null;
            bool singlePassed = single != null &&
                                CountStores(single) == 1 &&
                                single.ammo == 1 &&
                                singleAssembly != null &&
                                singleAssembly.name == StoreAssemblyName &&
                                testSet.weaponOptions.Contains(single) &&
                                prototypeRecords.TryGetValue(
                                    single,
                                    out PrototypeRecord singleRecord) &&
                                singleRecord.Definition.BuildMode ==
                                    WorkshopBuildMode.IndividualStores &&
                                Encyclopedia.WeaponLookup.TryGetValue(
                                    single.jsonKey,
                                    out WeaponMount singleLookup) &&
                                singleLookup == single;
            string singleKey = single?.jsonKey;
            RemovePrototype(single, availableMounts, testSet);
            bool singleCleanupPassed = single != null &&
                                       !availableMounts.Contains(single) &&
                                       !PrototypeMounts.Contains(single) &&
                                       !Encyclopedia.WeaponLookup.ContainsKey(singleKey);
            prototypeMount = null;
            hierarchyNodes.Clear();
            if (!singlePassed || !singleCleanupPassed)
            {
                failure = !singlePassed
                    ? "single-store construction failed hierarchy, lookup, or pylon validation"
                    : "temporary single-store clone was not fully removed after validation";
                return false;
            }

            return true;
        }

        public void Dispose()
        {
            Encyclopedia encyclopedia = Encyclopedia.i;
            bool removedAny = ownedPrototypes.Count > 0;
            for (int i = ownedPrototypes.Count - 1; i >= 0; i--)
            {
                DestroyPrototype(ownedPrototypes[i], encyclopedia);
            }
            ownedPrototypes.Clear();
            prototypeRecords.Clear();
            if (removedAny && encyclopedia != null)
            {
                TryRebuildEncyclopedia(encyclopedia, out _);
            }
        }

        private void RemovePrototype(
            WeaponMount prototype,
            List<WeaponMount> availableMounts,
            HardpointSet testSet)
        {
            testSet?.weaponOptions?.Remove(prototype);
            availableMounts?.Remove(prototype);
            ownedPrototypes.Remove(prototype);
            prototypeRecords.Remove(prototype);
            Encyclopedia encyclopedia = Encyclopedia.i;
            DestroyPrototype(prototype, encyclopedia);
            if (!TryRebuildEncyclopedia(encyclopedia, out string failure))
            {
                log.LogWarning(
                    "Prototype removed, but Encyclopedia lookup recovery failed: " + failure + ".");
            }
        }

        private static void DestroyPrototype(
            WeaponMount prototype,
            Encyclopedia encyclopedia)
        {
            PrototypeMounts.Remove(prototype);
            SpawnedInstances.Remove(prototype);
            encyclopedia?.weaponMounts?.Remove(prototype);
            if (prototype?.prefab != null)
            {
                UnityEngine.Object.Destroy(prototype.prefab);
            }
            if (prototype?.info != null)
            {
                UnityEngine.Object.Destroy(prototype.info);
            }
            if (prototype != null)
            {
                UnityEngine.Object.Destroy(prototype);
            }
        }

        private struct WorkshopDefinition
        {
            internal string JsonKey;
            internal string MountName;
            internal string WeaponName;
            internal string ShortName;
            internal WorkshopBuildMode BuildMode;
            internal bool KeepSourceAdapters;
            internal int SourceStoreIndex;
            internal int RackAssemblyCount;
            internal int StoresPerAssembly;
            internal int TotalStores;
            internal Vector3 AssemblyOffset;
            internal float PerStoreCost;
            internal float FullMass;
            internal float FullDrag;
            internal float FullRcs;
            internal float EmptyCost;
            internal float EmptyMass;
            internal float EmptyDrag;
            internal float EmptyRcs;
        }
    }

    [HarmonyPatch(typeof(Hardpoint), nameof(Hardpoint.SpawnMount))]
    internal static class WeaponMountWorkshopActivationPatch
    {
        private static void Postfix(WeaponMount weaponMount, GameObject __result)
        {
            WeaponMountWorkshop.ActivateSpawnedPrototype(weaponMount, __result);
        }
    }
}

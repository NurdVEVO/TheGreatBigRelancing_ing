using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace TheGreatBigRebalancing.Tools
{
    internal sealed partial class AircraftSelectorPylonEditor
    {
        private sealed class HardpointTemplate
        {
            internal bool CurrentAircraft;
            internal string AircraftName;
            internal string AircraftKey;
            internal string PylonName;
            internal int PylonIndex;
            internal int HardpointIndex;
            internal string Role;
            internal string RendererPath;
            internal Hardpoint SourceHardpoint;
            internal Renderer SourceRenderer;

            internal string Label =>
                (CurrentAircraft ? "THIS AIRCRAFT | " : string.Empty) +
                AircraftName + " | " + PylonName + " / HP " + HardpointIndex +
                " | " + Role + " | " + RendererPath;

            internal string ExportDescription =>
                (SourceRenderer == null
                    ? "No visual model"
                    : AircraftName + " (" + AircraftKey + ") | PylonSet[" + PylonIndex +
                      "] " + PylonName + " | HP[" + HardpointIndex + "] | " + Role +
                      " | " + RendererPath);
        }

        private sealed class PylonStationBinding
        {
            internal Transform AssemblyRoot;
            internal Renderer OriginalBaseRenderer;
            internal Renderer OriginalLegacyRenderer;
            internal Renderer TemplateSourceRenderer;
            internal GameObject ModelSlotObject;
            internal GameObject GeneratedModel;

            internal Transform ModelSlot => ModelSlotObject?.transform;
        }

        private struct TransformSnapshot
        {
            internal Transform Target;
            internal Transform Parent;
            internal Vector3 Position;
            internal Quaternion Rotation;
            internal Vector3 Scale;
        }

        private struct PylonVisualSnapshot
        {
            internal Transform Transform;
            internal Vector3 AnchorLocalPosition;
            internal Quaternion AnchorLocalRotation;
            internal Vector3 AnchorRelativeScale;
        }

        private const int MaximumHardpointsPerSet = 32;

        private static readonly FieldInfo SelectorAirbaseField =
            AccessTools.Field(typeof(AircraftSelectionMenu), "airbase");

        private static AircraftSelectorPylonEditor activeInstance;

        private readonly Dictionary<Hardpoint, TransformSnapshot> originalTransforms =
            new Dictionary<Hardpoint, TransformSnapshot>();
        private readonly Dictionary<Hardpoint, TransformSnapshot> originalAssemblyTransforms =
            new Dictionary<Hardpoint, TransformSnapshot>();
        private readonly Dictionary<Hardpoint, PylonStationBinding> pylonStationBindings =
            new Dictionary<Hardpoint, PylonStationBinding>();
        private readonly Dictionary<Hardpoint, string> hardpointTemplateOrigins =
            new Dictionary<Hardpoint, string>();
        private readonly HashSet<Hardpoint> createdHardpoints = new HashSet<Hardpoint>();
        private readonly List<HardpointTemplate> hardpointTemplates =
            new List<HardpointTemplate>();
        private readonly List<Transform> hardpointRootParentCandidates =
            new List<Transform>();
        private int selectedHardpointTemplateIndex;

        private bool freeCameraEnabled;
        private Vector3 freeCameraPosition;
        private float freeCameraYaw;
        private float freeCameraPitch;
        private float freeCameraSpeed = 10f;
        private float freeCameraFov = 50f;
        private Vector3 lastFreeCameraMousePosition;
        private bool freeCameraMouseCaptured;
        private bool structureLiveSmokeValidated;

        private void ApplyFreeCameraAfterGameUpdate(CameraStateManager cameraManager)
        {
            if (!freeCameraEnabled)
            {
                return;
            }
            if (!ShouldDraw() || selector == null || !selector.isActiveAndEnabled)
            {
                SetFreeCameraEnabled(false);
                return;
            }

            if (cameraManager == null || cameraManager.mainCamera == null ||
                cameraManager.currentState != cameraManager.selectionState)
            {
                SetFreeCameraEnabled(false);
                return;
            }

            UpdateFreeCameraInput(cameraManager);
            Quaternion rotation = Quaternion.Euler(freeCameraPitch, freeCameraYaw, 0f);
            cameraManager.transform.SetPositionAndRotation(freeCameraPosition, rotation);
            cameraManager.mainCamera.fieldOfView = freeCameraFov;
        }

        internal static void ApplyActiveFreeCamera(CameraStateManager cameraManager)
        {
            activeInstance?.ApplyFreeCameraAfterGameUpdate(cameraManager);
        }

        private void ResetStructureEditorForAircraft()
        {
            StopNearestPartFeedback();
            DisposePylonStationBindings(restoreOriginal: true);
            originalTransforms.Clear();
            originalAssemblyTransforms.Clear();
            hardpointTemplateOrigins.Clear();
            createdHardpoints.Clear();
            RebuildHardpointRootParentCandidates();

            if (weaponManager?.hardpointSets != null)
            {
                foreach (HardpointSet set in weaponManager.hardpointSets)
                {
                    if (set?.hardpoints == null)
                    {
                        continue;
                    }
                    foreach (Hardpoint hardpoint in set.hardpoints)
                    {
                        RememberOriginalTransform(hardpoint);
                    }
                }
            }

            RebuildHardpointTemplates();
            RefreshStructureSelection();
            ResetEditor2ForAircraft();
            if (smokeTestRequested && !structureLiveSmokeValidated)
            {
                structureLiveSmokeValidated = RunStructureEditorLiveSmokeTest(out string failure);
                if (structureLiveSmokeValidated)
                {
                    log.LogInfo(
                        "Pylon structure editor live smoke test passed: loaded-aircraft template " +
                        "discovery, immutable station binding, isolated model slots, passive " +
                        "mesh/material cloning, original-model restoration, Base variant construction, " +
                        "separate pylon-assembly/weapon-anchor movement, detached pylon-visual " +
                        "following, transform preservation, safe root reparenting/reset, and " +
                        "mutual pylon exclusion links were validated.");
                }
                else
                {
                    log.LogError("Pylon structure editor live smoke test failed: " + failure + ".");
                }
            }
            if (smokeTestRequested && !nearestPartFeedbackSmokeValidated)
            {
                nearestPartFeedbackSmokeValidated =
                    RunNearestPartFeedbackLiveSmokeTest(out string feedbackFailure);
                if (nearestPartFeedbackSmokeValidated)
                {
                    log.LogInfo(
                        "Nearest-UnitPart feedback live smoke test passed: geometry-aware " +
                        "selection, animated world line, whole-part highlight registration, " +
                        "and deterministic restoration were validated.");
                }
                else
                {
                    log.LogError(
                        "Nearest-UnitPart feedback live smoke test failed: " +
                        feedbackFailure + ".");
                }
            }
            if (freeCameraEnabled)
            {
                FocusFreeCameraOnAircraft();
            }
        }

        private void DisposeStructureEditor()
        {
            SetFreeCameraEnabled(false);
            DisposeEditor2();
            DisposePylonStationBindings(restoreOriginal: true);
            originalTransforms.Clear();
            originalAssemblyTransforms.Clear();
            hardpointTemplateOrigins.Clear();
            createdHardpoints.Clear();
            hardpointTemplates.Clear();
            hardpointRootParentCandidates.Clear();
        }

        private void RememberOriginalTransform(Hardpoint hardpoint)
        {
            if (hardpoint?.transform == null || originalTransforms.ContainsKey(hardpoint))
            {
                return;
            }
            originalTransforms.Add(hardpoint, new TransformSnapshot
            {
                Target = hardpoint.transform,
                Parent = hardpoint.transform.parent,
                Position = hardpoint.transform.localPosition,
                Rotation = hardpoint.transform.localRotation,
                Scale = hardpoint.transform.localScale
            });
            Transform assembly = ResolvePylonAssemblyRoot(hardpoint);
            if (assembly != null && assembly != hardpoint.transform &&
                !originalAssemblyTransforms.ContainsKey(hardpoint))
            {
                originalAssemblyTransforms.Add(hardpoint, new TransformSnapshot
                {
                    Target = assembly,
                    Parent = assembly.parent,
                    Position = assembly.localPosition,
                    Rotation = assembly.localRotation,
                    Scale = assembly.localScale
                });
            }
        }

        private void ResetHardpointTransform(Hardpoint hardpoint)
        {
            if (hardpoint?.transform == null)
            {
                return;
            }
            if (originalTransforms.TryGetValue(hardpoint, out TransformSnapshot snapshot))
            {
                MutateWeaponAnchorTransform(hardpoint, transform =>
                {
                    transform.SetParent(snapshot.Parent, false);
                    transform.localPosition = snapshot.Position;
                    transform.localRotation = snapshot.Rotation;
                    transform.localScale = snapshot.Scale;
                });
            }
            else if (resetPositions.TryGetValue(hardpoint, out Vector3 position))
            {
                SetHardpointLocalPosition(hardpoint, position);
            }
        }

        private void ResetPylonAssemblyTransform(Hardpoint hardpoint)
        {
            Transform assembly = ResolvePylonAssemblyRoot(hardpoint);
            if (assembly == null)
            {
                return;
            }
            if (assembly == hardpoint.transform)
            {
                if (originalTransforms.TryGetValue(
                        hardpoint,
                        out TransformSnapshot anchorSnapshot))
                {
                    MutatePylonAssemblyTransform(hardpoint, assembly, transform =>
                    {
                        transform.SetParent(anchorSnapshot.Parent, false);
                        transform.localPosition = anchorSnapshot.Position;
                        transform.localRotation = anchorSnapshot.Rotation;
                        transform.localScale = anchorSnapshot.Scale;
                    });
                }
                return;
            }
            if (!originalAssemblyTransforms.TryGetValue(
                    hardpoint,
                    out TransformSnapshot snapshot) ||
                snapshot.Target == null)
            {
                return;
            }
            MutatePylonAssemblyTransform(hardpoint, snapshot.Target, transform =>
            {
                transform.SetParent(snapshot.Parent, false);
                transform.localPosition = snapshot.Position;
                transform.localRotation = snapshot.Rotation;
                transform.localScale = snapshot.Scale;
            });
        }

        private void MirrorPylonAssemblyTransform(Hardpoint source, Hardpoint destination)
        {
            Transform sourceAssembly = ResolvePylonAssemblyRoot(source);
            Transform destinationAssembly = ResolvePylonAssemblyRoot(destination);
            if (sourceAssembly == null || destinationAssembly == null)
            {
                return;
            }
            MirrorTransformInAircraftSpace(
                sourceAssembly,
                mutation => MutatePylonAssemblyTransform(
                    destination,
                    destinationAssembly,
                    mutation));
        }

        private void MirrorWeaponAnchorTransform(Hardpoint source, Hardpoint destination)
        {
            if (source?.transform == null || destination?.transform == null)
            {
                return;
            }
            MirrorTransformInAircraftSpace(
                source.transform,
                mutation => MutateWeaponAnchorTransform(destination, mutation));
        }

        private void MirrorTransformInAircraftSpace(
            Transform source,
            Action<Action<Transform>> mutateDestination)
        {
            if (source == null || mutateDestination == null)
            {
                return;
            }
            Transform aircraftTransform = previewAircraft != null
                ? previewAircraft.transform
                : source.root;
            Vector3 aircraftPosition = aircraftTransform.InverseTransformPoint(source.position);
            Quaternion aircraftRotation =
                Quaternion.Inverse(aircraftTransform.rotation) * source.rotation;
            Vector3 destinationPosition = aircraftTransform.TransformPoint(
                MirrorAircraftPosition(aircraftPosition));
            Quaternion destinationRotation = aircraftTransform.rotation *
                                             MirrorAircraftRotation(aircraftRotation);
            Vector3 destinationScale = source.lossyScale;
            mutateDestination(transform =>
            {
                transform.SetPositionAndRotation(destinationPosition, destinationRotation);
                SetWorldScale(transform, destinationScale);
            });
        }

        private static Vector3 MirrorAircraftPosition(Vector3 position)
        {
            return new Vector3(-position.x, position.y, position.z);
        }

        private static Quaternion MirrorAircraftRotation(Quaternion rotation)
        {
            return rotation;
        }

        private void SetHardpointLocalPosition(Hardpoint hardpoint, Vector3 position)
        {
            MutateWeaponAnchorTransform(
                hardpoint,
                transform => transform.localPosition = position);
        }

        private void SetHardpointLocalTransform(
            Hardpoint hardpoint,
            Vector3 position,
            Quaternion rotation,
            Vector3 scale)
        {
            MutateWeaponAnchorTransform(hardpoint, transform =>
            {
                transform.localPosition = position;
                transform.localRotation = rotation;
                transform.localScale = scale;
            });
        }

        private static void MutateWeaponAnchorTransform(
            Hardpoint hardpoint,
            Action<Transform> mutation)
        {
            Transform anchor = hardpoint?.transform;
            if (anchor != null && mutation != null)
            {
                mutation(anchor);
            }
        }

        private void MutatePylonAssemblyTransform(
            Hardpoint hardpoint,
            Transform assembly,
            Action<Transform> mutation)
        {
            if (hardpoint == null || assembly == null || mutation == null)
            {
                return;
            }
            List<PylonVisualSnapshot> visuals = CapturePylonVisuals(hardpoint, assembly);
            Vector3 oldPosition = assembly.position;
            Quaternion oldRotation = assembly.rotation;
            Vector3 oldScale = assembly.lossyScale;
            mutation(assembly);
            if (Vector3.SqrMagnitude(assembly.position - oldPosition) < 0.0000000001f &&
                Quaternion.Angle(assembly.rotation, oldRotation) < 0.00001f &&
                Vector3.SqrMagnitude(assembly.lossyScale - oldScale) < 0.0000000001f)
            {
                return;
            }
            foreach (PylonVisualSnapshot visual in visuals)
            {
                if (visual.Transform == null)
                {
                    continue;
                }
                visual.Transform.SetPositionAndRotation(
                    assembly.TransformPoint(visual.AnchorLocalPosition),
                    assembly.rotation * visual.AnchorLocalRotation);
                SetWorldScale(
                    visual.Transform,
                    MultiplyComponents(assembly.lossyScale, visual.AnchorRelativeScale));
            }
        }

        private Transform ResolvePylonAssemblyRoot(Hardpoint hardpoint)
        {
            return GetOrCreatePylonStationBinding(hardpoint)?.AssemblyRoot;
        }

        private PylonStationBinding GetOrCreatePylonStationBinding(Hardpoint hardpoint)
        {
            Transform anchor = hardpoint?.transform;
            if (anchor == null)
            {
                return null;
            }
            if (pylonStationBindings.TryGetValue(
                    hardpoint,
                    out PylonStationBinding existing) &&
                existing?.AssemblyRoot != null)
            {
                return existing;
            }

            Renderer originalBase = GetBasePylonRenderer(hardpoint);
            PylonStationBinding created = CreatePylonStationBinding(
                hardpoint,
                DiscoverPylonAssemblyRoot(hardpoint),
                originalBase,
                hardpoint.Pylon);
            pylonStationBindings[hardpoint] = created;
            return created;
        }

        private PylonStationBinding CreatePylonStationBinding(
            Hardpoint hardpoint,
            Transform assembly,
            Renderer originalBase,
            Renderer originalLegacy)
        {
            Transform anchor = hardpoint?.transform;
            assembly = assembly ?? anchor;
            GameObject slot = new GameObject("TGBR Pylon Model Slot");
            slot.transform.SetParent(assembly, false);
            slot.transform.localPosition = Vector3.zero;
            slot.transform.localRotation = Quaternion.identity;
            slot.transform.localScale = Vector3.one;
            if (anchor != null)
            {
                slot.layer = anchor.gameObject.layer;
                slot.hideFlags = anchor.gameObject.hideFlags;
            }
            return new PylonStationBinding
            {
                AssemblyRoot = assembly,
                OriginalBaseRenderer = originalBase,
                OriginalLegacyRenderer = originalLegacy,
                TemplateSourceRenderer = originalBase ?? originalLegacy,
                ModelSlotObject = slot
            };
        }

        private Transform DiscoverPylonAssemblyRoot(Hardpoint hardpoint)
        {
            Transform anchor = hardpoint?.transform;
            if (anchor == null)
            {
                return null;
            }

            List<Renderer> references = GetReferencedPylonRenderers(hardpoint);
            Transform anchorFallback = anchor;
            foreach (Renderer renderer in references)
            {
                if (renderer == null || RendererUsedByOtherHardpoint(renderer, hardpoint))
                {
                    continue;
                }
                Transform candidate = FindLowestCommonAncestor(anchor, renderer.transform);
                if (!IsSafePylonAssemblyRoot(hardpoint, candidate))
                {
                    continue;
                }
                if (candidate != anchor)
                {
                    return candidate;
                }
                anchorFallback = candidate;
            }
            return anchorFallback;
        }

        private static List<Renderer> GetReferencedPylonRenderers(Hardpoint hardpoint)
        {
            List<Renderer> renderers = new List<Renderer>();
            AddUniqueRenderer(renderers, hardpoint?.Pylon);
            AddUniqueRenderer(renderers, GetBasePylonRenderer(hardpoint));
            if (hardpoint != null)
            {
                foreach (object variant in GetPylonOptions(hardpoint))
                {
                    AddUniqueRenderer(
                        renderers,
                        PylonOptionRendererField?.GetValue(variant) as Renderer);
                }
            }
            AddUniqueRenderer(renderers, hardpoint?.Plug);
            return renderers;
        }

        private static void AddUniqueRenderer(ICollection<Renderer> renderers, Renderer renderer)
        {
            if (renderer != null && !renderers.Contains(renderer))
            {
                renderers.Add(renderer);
            }
        }

        private static Transform FindLowestCommonAncestor(Transform left, Transform right)
        {
            if (left == null || right == null)
            {
                return null;
            }
            HashSet<Transform> leftAncestors = new HashSet<Transform>();
            Transform current = left;
            while (current != null)
            {
                leftAncestors.Add(current);
                current = current.parent;
            }
            current = right;
            while (current != null)
            {
                if (leftAncestors.Contains(current))
                {
                    return current;
                }
                current = current.parent;
            }
            return null;
        }

        private bool IsSafePylonAssemblyRoot(Hardpoint hardpoint, Transform candidate)
        {
            Transform anchor = hardpoint?.transform;
            if (anchor == null || candidate == null)
            {
                return false;
            }
            if (candidate == anchor)
            {
                return true;
            }
            if (previewAircraft != null && candidate == previewAircraft.transform)
            {
                return false;
            }
            Transform damagePart = hardpoint.part?.transform;
            if (!IsPylonAssemblyCandidateWithinScope(
                    candidate,
                    previewAircraft?.transform,
                    damagePart))
            {
                return false;
            }
            if (damagePart != null &&
                (candidate == damagePart || damagePart.IsChildOf(candidate)))
            {
                return false;
            }
            if (weaponManager?.hardpointSets != null)
            {
                foreach (HardpointSet set in weaponManager.hardpointSets)
                {
                    if (set?.hardpoints == null)
                    {
                        continue;
                    }
                    foreach (Hardpoint other in set.hardpoints)
                    {
                        Transform otherAnchor = other?.transform;
                        if (other == hardpoint || otherAnchor == null)
                        {
                            continue;
                        }
                        if (otherAnchor == candidate || otherAnchor.IsChildOf(candidate))
                        {
                            return false;
                        }
                    }
                }
            }
            return true;
        }

        private static bool IsPylonAssemblyCandidateWithinScope(
            Transform candidate,
            Transform aircraft,
            Transform assignedPart)
        {
            return candidate != null &&
                   (aircraft == null || candidate.IsChildOf(aircraft) ||
                    assignedPart != null && candidate.IsChildOf(assignedPart));
        }

        private void DisposePylonStationBindings(bool restoreOriginal)
        {
            foreach (KeyValuePair<Hardpoint, PylonStationBinding> entry in pylonStationBindings)
            {
                DisposePylonStationBinding(entry.Key, entry.Value, restoreOriginal);
            }
            pylonStationBindings.Clear();
        }

        private void DisposePylonStationBinding(
            Hardpoint hardpoint,
            PylonStationBinding binding,
            bool restoreOriginal)
        {
            if (binding == null)
            {
                return;
            }
            if (restoreOriginal && hardpoint != null)
            {
                RestoreOriginalPylonRendererReferences(hardpoint, binding);
            }
            if (binding.ModelSlotObject != null)
            {
                UnityEngine.Object.Destroy(binding.ModelSlotObject);
            }
            binding.GeneratedModel = null;
            binding.ModelSlotObject = null;
        }

        private List<PylonVisualSnapshot> CapturePylonVisuals(
            Hardpoint hardpoint,
            Transform anchor)
        {
            List<PylonVisualSnapshot> snapshots = new List<PylonVisualSnapshot>();
            HashSet<Transform> captured = new HashSet<Transform>();
            CapturePylonVisual(hardpoint, hardpoint.Pylon, anchor, captured, snapshots);
            CapturePylonVisual(hardpoint, hardpoint.Plug, anchor, captured, snapshots);
            Array variants = GetPylonOptions(hardpoint);
            foreach (object variant in variants)
            {
                CapturePylonVisual(
                    hardpoint,
                    PylonOptionRendererField?.GetValue(variant) as Renderer,
                    anchor,
                    captured,
                    snapshots);
            }
            return snapshots;
        }

        private void CapturePylonVisual(
            Hardpoint hardpoint,
            Renderer renderer,
            Transform anchor,
            ISet<Transform> captured,
            ICollection<PylonVisualSnapshot> snapshots)
        {
            Transform visual = renderer?.transform;
            if (visual == null || visual == anchor || visual.IsChildOf(anchor) ||
                anchor.IsChildOf(visual) || !captured.Add(visual) ||
                RendererUsedByOtherHardpoint(renderer, hardpoint))
            {
                return;
            }
            snapshots.Add(new PylonVisualSnapshot
            {
                Transform = visual,
                AnchorLocalPosition = anchor.InverseTransformPoint(visual.position),
                AnchorLocalRotation = Quaternion.Inverse(anchor.rotation) * visual.rotation,
                AnchorRelativeScale = DivideComponents(visual.lossyScale, anchor.lossyScale)
            });
        }

        private static Vector3 MultiplyComponents(Vector3 left, Vector3 right)
        {
            return new Vector3(left.x * right.x, left.y * right.y, left.z * right.z);
        }

        private static void SetWorldScale(Transform transform, Vector3 worldScale)
        {
            Transform parent = transform.parent;
            transform.localScale = parent == null
                ? worldScale
                : DivideComponents(worldScale, parent.lossyScale);
        }

        private void RebuildHardpointRootParentCandidates()
        {
            hardpointRootParentCandidates.Clear();
            if (previewAircraft == null)
            {
                return;
            }

            HashSet<Transform> hardpointRoots = new HashSet<Transform>();
            if (weaponManager?.hardpointSets != null)
            {
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
                            hardpointRoots.Add(hardpoint.transform);
                        }
                    }
                }
            }

            foreach (Transform candidate in previewAircraft.GetComponentsInChildren<Transform>(true))
            {
                if (candidate == null || IsInsideHardpointSubtree(candidate, hardpointRoots))
                {
                    continue;
                }
                hardpointRootParentCandidates.Add(candidate);
            }
            hardpointRootParentCandidates.Sort((left, right) => string.Compare(
                GetTransformPath(left),
                GetTransformPath(right),
                StringComparison.OrdinalIgnoreCase));
            hardpointRootParentCandidates.Remove(previewAircraft.transform);
            hardpointRootParentCandidates.Insert(0, previewAircraft.transform);
        }

        private static bool IsInsideHardpointSubtree(
            Transform candidate,
            ISet<Transform> hardpointRoots)
        {
            Transform current = candidate;
            while (current != null)
            {
                if (hardpointRoots.Contains(current))
                {
                    return true;
                }
                current = current.parent;
            }
            return false;
        }

        private bool IsSelectableHardpointRootParent(Hardpoint hardpoint, Transform candidate)
        {
            Transform root = hardpoint?.transform;
            return root != null &&
                   candidate != null &&
                   candidate != root &&
                   !candidate.IsChildOf(root) &&
                   hardpointRootParentCandidates.Contains(candidate);
        }

        private bool ReparentHardpointRoot(
            Hardpoint hardpoint,
            Transform newParent,
            out string failure)
        {
            failure = null;
            if (!IsSelectableHardpointRootParent(hardpoint, newParent))
            {
                failure = "the selected hierarchy node is not a safe aircraft root parent";
                return false;
            }
            ReparentHardpointRootPreservingWorld(hardpoint, newParent);
            if (hardpoint.transform.parent != newParent)
            {
                failure = "Unity did not retain the requested root parent";
                return false;
            }
            return true;
        }

        private void ReparentHardpointRootPreservingWorld(
            Hardpoint hardpoint,
            Transform newParent)
        {
            Transform root = hardpoint?.transform;
            if (root == null || newParent == null || newParent == root ||
                newParent.IsChildOf(root) || root.parent == newParent)
            {
                return;
            }

            Vector3 worldPosition = root.position;
            Quaternion worldRotation = root.rotation;
            Vector3 worldScale = root.lossyScale;
            MutateWeaponAnchorTransform(hardpoint, transform =>
            {
                transform.SetParent(newParent, false);
                transform.SetPositionAndRotation(worldPosition, worldRotation);
                SetWorldScale(transform, worldScale);
            });
        }

        private void RefreshStructureSelection()
        {
            SelectTemplateForHardpoint(SelectedHardpoint());
        }

        private void AddHardpoint(HardpointSet set, bool mirrorSelected)
        {
            if (set == null || previewAircraft == null || set.hardpoints.Count >= MaximumHardpointsPerSet)
            {
                status = "Cannot add another hardpoint to this pylon set.";
                return;
            }

            Hardpoint mirrorSource = mirrorSelected ? SelectedHardpoint() : null;
            Hardpoint behaviorSource = SelectedHardpoint();
            if (behaviorSource == null && set.hardpoints.Count > 0)
            {
                behaviorSource = set.hardpoints[0];
            }
            HardpointTemplate modelTemplate = SelectedHardpointTemplate();
            if (modelTemplate == null)
            {
                status = "Select a base pylon model template first.";
                return;
            }

            Transform sourceAnchor = behaviorSource?.transform;
            Transform sourceAssembly = ResolvePylonAssemblyRoot(behaviorSource);
            Transform parent = sourceAssembly != null && sourceAssembly != sourceAnchor
                ? sourceAssembly.parent
                : sourceAnchor?.parent ?? previewAircraft.transform;
            GameObject assemblyObject = new GameObject(
                "TGBR " + set.name + " Pylon Assembly " + (set.hardpoints.Count + 1));
            assemblyObject.transform.SetParent(parent, false);
            Transform placementSource = sourceAssembly ?? sourceAnchor;
            if (placementSource != null)
            {
                assemblyObject.layer = placementSource.gameObject.layer;
                assemblyObject.hideFlags = placementSource.gameObject.hideFlags;
                assemblyObject.transform.SetPositionAndRotation(
                    placementSource.position,
                    placementSource.rotation);
                SetWorldScale(assemblyObject.transform, placementSource.lossyScale);
            }
            GameObject mountPoint = new GameObject(
                "TGBR " + set.name + " Weapon Anchor " + (set.hardpoints.Count + 1));
            mountPoint.transform.SetParent(assemblyObject.transform, false);
            if (sourceAnchor != null && sourceAssembly != null && sourceAssembly != sourceAnchor)
            {
                mountPoint.transform.SetPositionAndRotation(
                    sourceAnchor.position,
                    sourceAnchor.rotation);
                SetWorldScale(mountPoint.transform, sourceAnchor.lossyScale);
            }
            if (sourceAnchor != null)
            {
                mountPoint.layer = sourceAnchor.gameObject.layer;
                mountPoint.hideFlags = sourceAnchor.gameObject.hideFlags;
            }

            Hardpoint hardpoint = new Hardpoint
            {
                transform = mountPoint.transform,
                part = behaviorSource?.part ?? (parts.Count > 0 ? parts[0] : null),
                bayDoors = behaviorSource?.bayDoors == null
                    ? Array.Empty<BayDoor>()
                    : (BayDoor[])behaviorSource.bayDoors.Clone(),
                doorOpenDuration = behaviorSource?.doorOpenDuration ?? 0f,
                Pylon = null,
                Plug = null,
                BuiltInWeapons = Array.Empty<Weapon>(),
                BuiltInTurrets = Array.Empty<Turret>(),
                HardpointIndex = -1
            };
            SetEmptyPylonOptions(hardpoint);
            set.hardpoints.Add(hardpoint);
            createdHardpoints.Add(hardpoint);
            pylonStationBindings[hardpoint] = CreatePylonStationBinding(
                hardpoint,
                assemblyObject.transform,
                originalBase: null,
                originalLegacy: null);
            RegisterEditor2Hardpoint(hardpoint, true);
            RememberOriginalTransform(hardpoint);
            selectedHardpointIndex = set.hardpoints.Count - 1;
            ApplySelectedTemplateToHardpoint(hardpoint);
            if (mirrorSource != null)
            {
                MirrorPylonAssemblyTransform(mirrorSource, hardpoint);
            }
            weaponUiRefreshPending = true;
            status = "Added HP " + selectedHardpointIndex + " to " + set.name +
                     (mirrorSource == null ? "." : " as a mirrored copy.");
        }

        private void RemoveSelectedHardpoint(HardpointSet set)
        {
            if (selectedHardpointIndex < 0)
            {
                status = "Select a hardpoint to remove.";
                return;
            }
            RemoveHardpoint(set, selectedHardpointIndex);
        }

        private void RemoveHardpoint(HardpointSet set, int index)
        {
            if (set?.hardpoints == null || index < 0 || index >= set.hardpoints.Count)
            {
                return;
            }
            Hardpoint hardpoint = set.hardpoints[index];
            set.hardpoints.RemoveAt(index);
            DisposeRemovedHardpoint(hardpoint);
            selectedHardpointIndex = set.hardpoints.Count == 0
                ? -1
                : Mathf.Clamp(index, 0, set.hardpoints.Count - 1);
            weaponUiRefreshPending = true;
            status = "Removed hardpoint " + index + " from " + set.name + ".";
        }

        private void DisposeRemovedHardpoint(Hardpoint hardpoint)
        {
            Transform assembly = ResolvePylonAssemblyRoot(hardpoint);
            pylonStationBindings.TryGetValue(
                hardpoint,
                out PylonStationBinding stationBinding);
            if (hardpoint != null)
            {
                try
                {
                    if (hardpoint.GetMount() != null)
                    {
                        hardpoint.RemoveMount();
                    }
                }
                catch (Exception exception)
                {
                    log.LogWarning("Removing preview hardpoint store failed: " + exception.Message);
                }
                DisableHardpointVisuals(hardpoint);
            }
            ForgetEditor2Hardpoint(hardpoint);
            if (hardpoint != null && createdHardpoints.Remove(hardpoint) && hardpoint.transform != null)
            {
                Transform createdRoot = assembly != null && assembly != hardpoint.transform
                    ? assembly
                    : hardpoint.transform;
                UnityEngine.Object.Destroy(createdRoot.gameObject);
            }
            else if (stationBinding != null)
            {
                DisposePylonStationBinding(hardpoint, stationBinding, restoreOriginal: false);
            }
            originalTransforms.Remove(hardpoint);
            originalAssemblyTransforms.Remove(hardpoint);
            pylonStationBindings.Remove(hardpoint);
            hardpointTemplateOrigins.Remove(hardpoint);
        }

        private static void SetEmptyPylonOptions(Hardpoint hardpoint)
        {
            if (PylonOptionsField == null || PylonOptionType == null)
            {
                return;
            }
            PylonOptionsField.SetValue(hardpoint, Array.CreateInstance(PylonOptionType, 0));
        }

        private void RebuildHardpointTemplates()
        {
            hardpointTemplates.Clear();
            hardpointTemplates.Add(new HardpointTemplate
            {
                CurrentAircraft = true,
                AircraftName = "No visual model",
                AircraftKey = "<none>",
                PylonName = "<none>",
                PylonIndex = -1,
                HardpointIndex = -1,
                Role = "None",
                RendererPath = "<none>"
            });

            HashSet<string> identities = new HashSet<string>(StringComparer.Ordinal);
            AddTemplatesFromManager(
                previewAircraft?.definition,
                weaponManager,
                currentAircraft: true,
                identities);

            Encyclopedia encyclopedia = Encyclopedia.i;
            if (encyclopedia?.aircraft != null)
            {
                foreach (AircraftDefinition definition in encyclopedia.aircraft)
                {
                    if (definition == null || definition == previewAircraft?.definition ||
                        definition.unitPrefab == null)
                    {
                        continue;
                    }
                    Aircraft aircraft = definition.unitPrefab.GetComponent<Aircraft>() ??
                                        definition.unitPrefab.GetComponentInChildren<Aircraft>(true);
                    WeaponManager manager = aircraft?.weaponManager ??
                                            definition.unitPrefab.GetComponentInChildren<WeaponManager>(true);
                    AddTemplatesFromManager(definition, manager, currentAircraft: false, identities);
                }
            }

            selectedHardpointTemplateIndex = FindPreferredTemplateIndex(SelectedHardpoint());
        }

        private void AddTemplatesFromManager(
            AircraftDefinition definition,
            WeaponManager manager,
            bool currentAircraft,
            ISet<string> identities)
        {
            if (manager?.hardpointSets == null)
            {
                return;
            }
            string aircraftName = definition?.unitName ?? "Unknown aircraft";
            string aircraftKey = definition?.jsonKey ?? "<unknown>";
            for (int setIndex = 0; setIndex < manager.hardpointSets.Length; setIndex++)
            {
                HardpointSet set = manager.hardpointSets[setIndex];
                if (set?.hardpoints == null)
                {
                    continue;
                }
                for (int hardpointIndex = 0; hardpointIndex < set.hardpoints.Count; hardpointIndex++)
                {
                    Hardpoint hardpoint = set.hardpoints[hardpointIndex];
                    AddHardpointTemplate(definition, aircraftName, aircraftKey, set, setIndex,
                        hardpoint, hardpointIndex, "Legacy Pylon", hardpoint?.Pylon,
                        currentAircraft, identities);
                    AddHardpointTemplate(definition, aircraftName, aircraftKey, set, setIndex,
                        hardpoint, hardpointIndex, "Plug", hardpoint?.Plug,
                        currentAircraft, identities);
                    Array variants = GetPylonOptions(hardpoint);
                    for (int variantIndex = 0; variantIndex < variants.Length; variantIndex++)
                    {
                        object variant = variants.GetValue(variantIndex);
                        Renderer renderer = PylonOptionRendererField?.GetValue(variant) as Renderer;
                        AddHardpointTemplate(definition, aircraftName, aircraftKey, set, setIndex,
                            hardpoint, hardpointIndex,
                            "Variant[" + variantIndex + "] " + VariantLabel(variant), renderer,
                            currentAircraft, identities);
                    }
                }
            }
        }

        private void AddHardpointTemplate(
            AircraftDefinition definition,
            string aircraftName,
            string aircraftKey,
            HardpointSet set,
            int setIndex,
            Hardpoint hardpoint,
            int hardpointIndex,
            string role,
            Renderer renderer,
            bool currentAircraft,
            ISet<string> identities)
        {
            if (renderer == null)
            {
                return;
            }
            string rendererPath = GetTransformPath(renderer.transform);
            string identity = aircraftKey + "|" + setIndex + "|" + hardpointIndex + "|" +
                              role + "|" + rendererPath;
            if (!identities.Add(identity))
            {
                return;
            }
            hardpointTemplates.Add(new HardpointTemplate
            {
                CurrentAircraft = currentAircraft,
                AircraftName = aircraftName,
                AircraftKey = aircraftKey,
                PylonName = set?.name ?? "<unnamed>",
                PylonIndex = setIndex,
                HardpointIndex = hardpointIndex,
                Role = role,
                RendererPath = rendererPath,
                SourceHardpoint = hardpoint,
                SourceRenderer = renderer
            });
        }

        private HardpointTemplate SelectedHardpointTemplate()
        {
            return selectedHardpointTemplateIndex >= 0 &&
                   selectedHardpointTemplateIndex < hardpointTemplates.Count
                ? hardpointTemplates[selectedHardpointTemplateIndex]
                : null;
        }

        private void SelectTemplateForHardpoint(Hardpoint hardpoint)
        {
            selectedHardpointTemplateIndex = FindPreferredTemplateIndex(hardpoint);
        }

        private int FindPreferredTemplateIndex(Hardpoint hardpoint)
        {
            if (hardpoint == null || hardpointTemplates.Count == 0)
            {
                return 0;
            }
            PylonStationBinding binding = GetOrCreatePylonStationBinding(hardpoint);
            Renderer renderer = binding?.TemplateSourceRenderer ??
                                GetBasePylonRenderer(hardpoint);
            if (binding != null && binding.TemplateSourceRenderer == null)
            {
                return 0;
            }
            if (renderer != null)
            {
                for (int index = 1; index < hardpointTemplates.Count; index++)
                {
                    HardpointTemplate template = hardpointTemplates[index];
                    if (template.SourceRenderer == renderer)
                    {
                        return index;
                    }
                }
            }
            for (int index = 1; index < hardpointTemplates.Count; index++)
            {
                if (hardpointTemplates[index].CurrentAircraft)
                {
                    return index;
                }
            }
            return 0;
        }

        private void ApplySelectedTemplateToHardpoint(Hardpoint hardpoint)
        {
            HardpointTemplate template = SelectedHardpointTemplate();
            if (hardpoint == null || template == null)
            {
                return;
            }

            PylonStationBinding binding = GetOrCreatePylonStationBinding(hardpoint);
            if (binding?.ModelSlot == null)
            {
                status = "Could not resolve a stable model slot for this station.";
                return;
            }

            bool selectingOriginal = template.SourceRenderer != null &&
                                     (template.SourceRenderer == binding.OriginalBaseRenderer ||
                                      template.SourceRenderer == binding.OriginalLegacyRenderer);
            Renderer replacement = null;
            if (!selectingOriginal && template.SourceRenderer != null)
            {
                replacement = CloneTemplateRenderer(
                    template,
                    hardpoint,
                    binding.ModelSlot);
                if (replacement == null)
                {
                    status = "The selected pylon model is not passive mesh geometry and was " +
                             "left unchanged.";
                    return;
                }
            }

            RemoveGeneratedPylonModel(binding);
            if (selectingOriginal)
            {
                RestoreOriginalPylonRendererReferences(hardpoint, binding);
            }
            else
            {
                SetRendererEnabledIfOwned(binding.OriginalBaseRenderer, hardpoint, false);
                SetRendererEnabledIfOwned(binding.OriginalLegacyRenderer, hardpoint, false);
                SetPylonRendererReferences(hardpoint, replacement, replacement);
                if (replacement != null)
                {
                    replacement.enabled = true;
                    binding.GeneratedModel = replacement.gameObject;
                }
            }
            binding.TemplateSourceRenderer = template.SourceRenderer;
            hardpointTemplateOrigins[hardpoint] = template.ExportDescription;
            status = "Applied pylon model template: " + template.ExportDescription + ".";
        }

        private Renderer CloneTemplateRenderer(
            HardpointTemplate template,
            Hardpoint destination,
            Transform modelParent = null)
        {
            if (template?.SourceRenderer == null || destination?.transform == null)
            {
                return null;
            }
            MeshRenderer sourceRenderer = template.SourceRenderer as MeshRenderer;
            MeshFilter sourceFilter = template.SourceRenderer.GetComponent<MeshFilter>();
            if (sourceRenderer == null || sourceFilter?.sharedMesh == null)
            {
                return null;
            }

            GameObject clone = new GameObject();
            clone.name = "TGBR Pylon Model - " + template.SourceRenderer.gameObject.name;
            clone.layer = template.SourceRenderer.gameObject.layer;
            clone.hideFlags = template.SourceRenderer.gameObject.hideFlags;
            MeshFilter clonedFilter = clone.AddComponent<MeshFilter>();
            clonedFilter.sharedMesh = sourceFilter.sharedMesh;
            MeshRenderer clonedRenderer = clone.AddComponent<MeshRenderer>();
            CopyPassiveRendererSettings(sourceRenderer, clonedRenderer);
            modelParent = modelParent ?? destination.transform;
            clone.transform.SetParent(modelParent, false);

            Transform sourceHardpoint = template.SourceHardpoint?.transform;
            if (sourceHardpoint != null)
            {
                Vector3 anchorPosition = sourceHardpoint.InverseTransformPoint(
                    template.SourceRenderer.transform.position);
                Quaternion anchorRotation = Quaternion.Inverse(sourceHardpoint.rotation) *
                                            template.SourceRenderer.transform.rotation;
                Vector3 anchorScale = DivideComponents(
                    template.SourceRenderer.transform.lossyScale,
                    sourceHardpoint.lossyScale);
                clone.transform.SetPositionAndRotation(
                    destination.transform.TransformPoint(anchorPosition),
                    destination.transform.rotation * anchorRotation);
                SetWorldScale(
                    clone.transform,
                    MultiplyComponents(destination.transform.lossyScale, anchorScale));
            }
            else
            {
                clone.transform.localPosition = template.SourceRenderer.transform.localPosition;
                clone.transform.localRotation = template.SourceRenderer.transform.localRotation;
                clone.transform.localScale = template.SourceRenderer.transform.localScale;
            }

            clonedRenderer.enabled = true;
            return clonedRenderer;
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

        private void RemoveGeneratedPylonModel(PylonStationBinding binding)
        {
            if (binding?.GeneratedModel == null)
            {
                return;
            }
            binding.GeneratedModel.SetActive(false);
            UnityEngine.Object.Destroy(binding.GeneratedModel);
            binding.GeneratedModel = null;
        }

        private void RestoreOriginalPylonRendererReferences(
            Hardpoint hardpoint,
            PylonStationBinding binding)
        {
            if (hardpoint == null || binding == null)
            {
                return;
            }
            RemoveGeneratedPylonModel(binding);
            SetPylonRendererReferences(
                hardpoint,
                binding.OriginalBaseRenderer,
                binding.OriginalLegacyRenderer);
            if (binding.OriginalBaseRenderer != null)
            {
                binding.OriginalBaseRenderer.enabled = true;
            }
            if (binding.OriginalLegacyRenderer != null)
            {
                binding.OriginalLegacyRenderer.enabled = true;
            }
        }

        private static void SetPylonRendererReferences(
            Hardpoint hardpoint,
            Renderer baseRenderer,
            Renderer legacyRenderer)
        {
            object baseVariant = GetOrCreateBaseVariant(hardpoint);
            if (baseVariant != null)
            {
                SetVariantBase(baseVariant);
                PylonOptionRendererField?.SetValue(baseVariant, baseRenderer);
            }
            hardpoint.Pylon = legacyRenderer;
        }

        private void SetRendererEnabledIfOwned(
            Renderer renderer,
            Hardpoint hardpoint,
            bool enabled)
        {
            if (renderer != null && !RendererUsedByOtherHardpoint(renderer, hardpoint))
            {
                renderer.enabled = enabled;
            }
        }

        private static Vector3 DivideComponents(Vector3 value, Vector3 divisor)
        {
            return new Vector3(
                Mathf.Abs(divisor.x) < 0.000001f ? value.x : value.x / divisor.x,
                Mathf.Abs(divisor.y) < 0.000001f ? value.y : value.y / divisor.y,
                Mathf.Abs(divisor.z) < 0.000001f ? value.z : value.z / divisor.z);
        }

        private static object GetOrCreateBaseVariant(Hardpoint hardpoint)
        {
            Array variants = GetPylonOptions(hardpoint);
            for (int index = 0; index < variants.Length; index++)
            {
                object variant = variants.GetValue(index);
                bool cargo = (bool)(PylonOptionCargoField?.GetValue(variant) ?? false);
                WeaponMount mount = PylonOptionMountField?.GetValue(variant) as WeaponMount;
                if (!cargo && mount == null)
                {
                    return variant;
                }
            }
            int createdIndex = AddPylonVariant(hardpoint);
            Array expanded = GetPylonOptions(hardpoint);
            return createdIndex >= 0 && createdIndex < expanded.Length
                ? expanded.GetValue(createdIndex)
                : null;
        }

        private static Renderer GetBasePylonRenderer(Hardpoint hardpoint)
        {
            Array variants = GetPylonOptions(hardpoint);
            for (int index = 0; index < variants.Length; index++)
            {
                object variant = variants.GetValue(index);
                bool cargo = (bool)(PylonOptionCargoField?.GetValue(variant) ?? false);
                WeaponMount mount = PylonOptionMountField?.GetValue(variant) as WeaponMount;
                if (!cargo && mount == null)
                {
                    return PylonOptionRendererField?.GetValue(variant) as Renderer;
                }
            }
            return hardpoint?.Pylon;
        }

        private bool RendererUsedByOtherHardpoint(Renderer renderer, Hardpoint excluded)
        {
            if (renderer == null || weaponManager?.hardpointSets == null)
            {
                return false;
            }
            foreach (HardpointSet set in weaponManager.hardpointSets)
            {
                if (set?.hardpoints == null)
                {
                    continue;
                }
                foreach (Hardpoint hardpoint in set.hardpoints)
                {
                    if (hardpoint == null || hardpoint == excluded)
                    {
                        continue;
                    }
                    if (hardpoint.Pylon == renderer || hardpoint.Plug == renderer)
                    {
                        return true;
                    }
                    Array variants = GetPylonOptions(hardpoint);
                    foreach (object variant in variants)
                    {
                        if (PylonOptionRendererField?.GetValue(variant) as Renderer == renderer)
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        private static void DisableHardpointVisuals(Hardpoint hardpoint)
        {
            if (hardpoint == null)
            {
                return;
            }
            if (hardpoint.Pylon != null)
            {
                hardpoint.Pylon.enabled = false;
            }
            if (hardpoint.Plug != null)
            {
                hardpoint.Plug.enabled = false;
            }
            Array variants = GetPylonOptions(hardpoint);
            foreach (object variant in variants)
            {
                Renderer renderer = PylonOptionRendererField?.GetValue(variant) as Renderer;
                if (renderer != null)
                {
                    renderer.enabled = false;
                }
            }
        }

        private static bool ContainsPreclusion(HardpointSet set, int index)
        {
            return set?.precludingHardpointSets != null && index >= 0 && index <= byte.MaxValue &&
                   set.precludingHardpointSets.Contains((byte)index);
        }

        private static void SetMutualPreclusion(
            HardpointSet selected,
            int selectedIndex,
            HardpointSet other,
            int otherIndex,
            bool enabled)
        {
            if (selected == null || other == null || selectedIndex < 0 || otherIndex < 0 ||
                selectedIndex > byte.MaxValue || otherIndex > byte.MaxValue)
            {
                return;
            }
            selected.precludingHardpointSets =
                selected.precludingHardpointSets ?? new List<byte>();
            other.precludingHardpointSets = other.precludingHardpointSets ?? new List<byte>();
            SetPreclusion(selected.precludingHardpointSets, (byte)otherIndex, enabled);
            SetPreclusion(other.precludingHardpointSets, (byte)selectedIndex, enabled);
        }

        private static void SetPreclusion(ICollection<byte> indexes, byte value, bool enabled)
        {
            if (enabled)
            {
                if (!indexes.Contains(value))
                {
                    indexes.Add(value);
                }
            }
            else
            {
                indexes.Remove(value);
            }
        }

        private void AppendPylonStructure(StringBuilder output, HardpointSet set, int setIndex)
        {
            output.AppendLine("  HardpointCount: " + (set.hardpoints?.Count ?? 0));
            output.AppendLine("  SymmetryWithPrev: " + set.SymmetryWithPrev);
            output.AppendLine("  SymmetryName: " +
                              (string.IsNullOrEmpty(set.SymmetryName) ? "<none>" : set.SymmetryName));
            output.Append("  LinkedPreviousPylon: ");
            if (set.SymmetryWithPrev && setIndex > 0)
            {
                output.AppendLine("[" + (setIndex - 1) + "] " +
                                  (weaponManager.hardpointSets[setIndex - 1]?.name ?? "<missing>"));
            }
            else
            {
                output.AppendLine("<none>");
            }
            output.Append("  PrecludingPylonSets: ");
            if (set.precludingHardpointSets == null || set.precludingHardpointSets.Count == 0)
            {
                output.AppendLine("<none>");
                return;
            }
            bool wrote = false;
            foreach (byte index in set.precludingHardpointSets)
            {
                if (wrote)
                {
                    output.Append(", ");
                }
                string name = index < weaponManager.hardpointSets.Length
                    ? weaponManager.hardpointSets[index]?.name ?? "<missing>"
                    : "<out-of-range>";
                output.Append("[" + index + "] " + name);
                wrote = true;
            }
            output.AppendLine();
        }

        private void AppendHardpointStructure(
            StringBuilder output,
            Hardpoint hardpoint,
            int hardpointIndex)
        {
            PylonStationBinding binding = GetOrCreatePylonStationBinding(hardpoint);
            Transform assembly = binding?.AssemblyRoot;
            output.AppendLine("    AssemblyRoot: " + GetTransformPath(assembly));
            if (assembly != null)
            {
                output.AppendLine("    AssemblyPosition: (" +
                                  FormatFloat(assembly.localPosition.x) + ", " +
                                  FormatFloat(assembly.localPosition.y) + ", " +
                                  FormatFloat(assembly.localPosition.z) + ")");
                Vector3 assemblyRotation = assembly.localEulerAngles;
                output.AppendLine("    AssemblyRotation: (" +
                                  FormatFloat(NormalizeAngle(assemblyRotation.x)) + ", " +
                                  FormatFloat(NormalizeAngle(assemblyRotation.y)) + ", " +
                                  FormatFloat(NormalizeAngle(assemblyRotation.z)) + ")");
                output.AppendLine("    AssemblyScale: (" +
                                  FormatFloat(assembly.localScale.x) + ", " +
                                  FormatFloat(assembly.localScale.y) + ", " +
                                  FormatFloat(assembly.localScale.z) + ")");
            }
            output.AppendLine("    ModelSlot: " + GetTransformPath(binding?.ModelSlot));
            output.AppendLine("    OriginalModelRenderer: " +
                              RendererLabel(binding?.OriginalBaseRenderer ??
                                            binding?.OriginalLegacyRenderer));
            output.AppendLine("    ActiveModelRenderer: " +
                              RendererLabel(GetBasePylonRenderer(hardpoint)));
            output.AppendLine("    WeaponAnchor: " + GetTransformPath(hardpoint?.transform));
            output.AppendLine("    AnchorParent: " +
                              GetTransformPath(hardpoint?.transform?.parent));
            output.AppendLine("    RootParent: " +
                              GetTransformPath(hardpoint?.transform?.parent));
            Vector3 rotation = hardpoint?.transform == null
                ? Vector3.zero
                : hardpoint.transform.localEulerAngles;
            Vector3 scale = hardpoint?.transform == null
                ? Vector3.one
                : hardpoint.transform.localScale;
            output.AppendLine("    Rotation: (" +
                              FormatFloat(NormalizeAngle(rotation.x)) + ", " +
                              FormatFloat(NormalizeAngle(rotation.y)) + ", " +
                              FormatFloat(NormalizeAngle(rotation.z)) + ")");
            output.AppendLine("    Scale: (" + FormatFloat(scale.x) + ", " +
                              FormatFloat(scale.y) + ", " + FormatFloat(scale.z) + ")");
            AppendEditor2HardpointMetadata(output, hardpoint);
            output.AppendLine("    TemplateSource: " + DescribeTemplateSource(hardpoint));
            output.AppendLine("    HardpointIndex: " + (hardpoint?.HardpointIndex ?? -1));
            output.AppendLine("    DoorOpenDuration: " +
                              FormatFloat(hardpoint?.doorOpenDuration ?? 0f));
            output.Append("    BayDoors: ");
            if (hardpoint?.bayDoors == null || hardpoint.bayDoors.Length == 0)
            {
                output.AppendLine("<none>");
            }
            else
            {
                bool wrote = false;
                foreach (BayDoor door in hardpoint.bayDoors)
                {
                    if (door == null)
                    {
                        continue;
                    }
                    if (wrote)
                    {
                        output.Append(", ");
                    }
                    output.Append(GetTransformPath(door.transform));
                    wrote = true;
                }
                output.AppendLine(wrote ? string.Empty : "<none>");
            }
            output.AppendLine("    LegacyPylonRenderer: " + RendererLabel(hardpoint?.Pylon));
            output.AppendLine("    PlugRenderer: " + RendererLabel(hardpoint?.Plug));
            output.AppendLine("    BaseModelRenderer: " + RendererLabel(GetBasePylonRenderer(hardpoint)));
        }

        private string DescribeTemplateSource(Hardpoint hardpoint)
        {
            if (hardpoint != null && hardpointTemplateOrigins.TryGetValue(hardpoint, out string origin))
            {
                return origin;
            }
            Renderer renderer = GetBasePylonRenderer(hardpoint);
            for (int index = 1; index < hardpointTemplates.Count; index++)
            {
                HardpointTemplate template = hardpointTemplates[index];
                if (template.CurrentAircraft && template.SourceRenderer == renderer)
                {
                    return "Existing | " + template.ExportDescription;
                }
            }
            return renderer == null
                ? "Existing hardpoint | No visual model"
                : "Existing hardpoint | " + RendererLabel(renderer);
        }

        private void SetFreeCameraEnabled(bool enabled)
        {
            if (freeCameraEnabled == enabled)
            {
                return;
            }
            if (enabled)
            {
                CameraStateManager cameraManager = SceneSingleton<CameraStateManager>.i;
                if (selector == null || !selector.isActiveAndEnabled || previewAircraft == null ||
                    cameraManager == null || cameraManager.currentState != cameraManager.selectionState)
                {
                    status = "Free camera requires an active aircraft selector preview.";
                    return;
                }
                freeCameraEnabled = true;
                freeCameraFov = cameraManager.mainCamera != null
                    ? cameraManager.mainCamera.fieldOfView
                    : 50f;
                FocusFreeCameraOnAircraft();
                status = "Selector free camera enabled.";
            }
            else
            {
                freeCameraEnabled = false;
                freeCameraMouseCaptured = false;
                RestoreSelectorCamera();
                status = "Selector free camera disabled; stock selector camera restored.";
            }
        }

        private void UpdateFreeCameraInput(CameraStateManager cameraManager)
        {
            bool rightMouse = Input.GetMouseButton(1);
            Vector3 currentMouse = Input.mousePosition;
            if (rightMouse)
            {
                if (freeCameraMouseCaptured)
                {
                    Vector3 delta = currentMouse - lastFreeCameraMousePosition;
                    freeCameraYaw += delta.x * 0.2f;
                    freeCameraPitch = Mathf.Clamp(freeCameraPitch - delta.y * 0.2f, -89f, 89f);
                }
                freeCameraMouseCaptured = true;

                float scroll = Input.mouseScrollDelta.y;
                if (Mathf.Abs(scroll) > 0.01f)
                {
                    freeCameraSpeed = Mathf.Clamp(
                        freeCameraSpeed * Mathf.Pow(1.25f, scroll),
                        0.05f,
                        1000f);
                }

                Quaternion rotation = Quaternion.Euler(freeCameraPitch, freeCameraYaw, 0f);
                Vector3 movement = Vector3.zero;
                movement += rotation * Vector3.forward * Axis(KeyCode.W, KeyCode.S);
                movement += rotation * Vector3.right * Axis(KeyCode.D, KeyCode.A);
                movement += Vector3.up * Axis(KeyCode.E, KeyCode.Q);
                float multiplier = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)
                    ? 5f
                    : 1f;
                if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
                {
                    multiplier *= 0.15f;
                }
                if (movement.sqrMagnitude > 1f)
                {
                    movement.Normalize();
                }
                freeCameraPosition += movement *
                                      (freeCameraSpeed * multiplier * Time.unscaledDeltaTime);

                if (Input.GetKeyDown(KeyCode.F))
                {
                    FocusFreeCameraOnHardpoint();
                }
            }
            else
            {
                freeCameraMouseCaptured = false;
            }
            lastFreeCameraMousePosition = currentMouse;
        }

        private static float Axis(KeyCode positive, KeyCode negative)
        {
            float value = 0f;
            if (Input.GetKey(positive))
            {
                value += 1f;
            }
            if (Input.GetKey(negative))
            {
                value -= 1f;
            }
            return value;
        }

        private void FocusFreeCameraOnAircraft()
        {
            if (previewAircraft == null)
            {
                return;
            }
            float length = previewAircraft.definition?.length ?? previewAircraft.maxRadius * 2f;
            float width = previewAircraft.definition?.width ?? previewAircraft.maxRadius * 2f;
            float height = previewAircraft.definition?.height ?? previewAircraft.maxRadius;
            float distance = Mathf.Max(5f, Mathf.Max(length, width) * 1.1f);
            Vector3 target = previewAircraft.transform.position + Vector3.up * (height * 0.35f);
            Vector3 viewDirection = previewAircraft.transform.forward;
            freeCameraPosition = target + viewDirection * distance + Vector3.up * (height * 0.2f);
            SetFreeCameraLookAt(target);
        }

        private void FocusFreeCameraOnHardpoint()
        {
            Hardpoint hardpoint = SelectedHardpoint();
            FocusFreeCameraOnTransform(hardpoint?.transform);
        }

        private void FocusFreeCameraOnTransform(Transform targetTransform)
        {
            if (targetTransform == null)
            {
                FocusFreeCameraOnAircraft();
                return;
            }
            Vector3 target = targetTransform.position;
            Vector3 aircraftForward = previewAircraft != null
                ? previewAircraft.transform.forward
                : Vector3.forward;
            Vector3 aircraftUp = previewAircraft != null ? previewAircraft.transform.up : Vector3.up;
            freeCameraPosition = target + aircraftForward * 3f + aircraftUp * 1.25f;
            SetFreeCameraLookAt(target);
        }

        private void SetFreeCameraLookAt(Vector3 target)
        {
            Vector3 direction = target - freeCameraPosition;
            if (direction.sqrMagnitude < 0.0001f)
            {
                direction = Vector3.forward;
            }
            Vector3 euler = Quaternion.LookRotation(direction.normalized, Vector3.up).eulerAngles;
            freeCameraYaw = NormalizeAngle(euler.y);
            freeCameraPitch = NormalizeAngle(euler.x);
        }

        private void RestoreSelectorCamera()
        {
            if (selector == null || !selector.isActiveAndEnabled)
            {
                return;
            }
            CameraStateManager cameraManager = SceneSingleton<CameraStateManager>.i;
            Airbase airbase = SelectorAirbaseField?.GetValue(selector) as Airbase;
            if (cameraManager == null || airbase == null)
            {
                return;
            }
            cameraManager.FocusAirbase(airbase, allowMoveToDropFocus: false);
            if (previewAircraft != null)
            {
                cameraManager.selectionState.SetPreviewAircraft(previewAircraft);
            }
        }

        private static float NormalizeAngle(float value)
        {
            value %= 360f;
            if (value > 180f)
            {
                value -= 360f;
            }
            if (value < -180f)
            {
                value += 360f;
            }
            return value;
        }

        private static bool RunStructureEditorSelfTest()
        {
            Vector3 sourcePosition = new Vector3(2f, 3f, 4f);
            Vector3 mirroredPosition = MirrorAircraftPosition(sourcePosition);
            Quaternion sourceRotation = Quaternion.Euler(10f, 20f, 30f);
            Quaternion mirroredRotation = MirrorAircraftRotation(sourceRotation);
            return SelectorAirbaseField != null &&
                   AccessTools.Method(typeof(CameraStateManager), "LateUpdate") != null &&
                   AccessTools.Method(
                       typeof(CameraStateManager),
                       nameof(CameraStateManager.FocusAirbase),
                       new[] { typeof(Airbase), typeof(bool), typeof(float), typeof(float) }) != null &&
                   AccessTools.Method(
                       typeof(CameraSelectionState),
                       nameof(CameraSelectionState.SetPreviewAircraft),
                       new[] { typeof(Aircraft) }) != null &&
                   mirroredPosition == new Vector3(-2f, 3f, 4f) &&
                   Quaternion.Angle(mirroredRotation, sourceRotation) < 0.0001f &&
                   MaximumHardpointsPerSet >= 16;
        }

        private bool RunStructureEditorLiveSmokeTest(out string failure)
        {
            failure = null;
            if (string.Equals(
                    previewAircraft?.definition?.jsonKey,
                    "P_Trisurface1",
                    StringComparison.OrdinalIgnoreCase) &&
                !ValidateTernionAssemblyResolution(out failure))
            {
                return false;
            }
            HardpointTemplate template = null;
            for (int index = 1; index < hardpointTemplates.Count; index++)
            {
                if (hardpointTemplates[index].SourceRenderer != null)
                {
                    template = hardpointTemplates[index];
                    break;
                }
            }
            if (template == null)
            {
                failure = "no pylon renderer template was discovered";
                return false;
            }

            GameObject originalParent = new GameObject("TGBR Structure Smoke Original Parent");
            GameObject replacementParent = new GameObject("TGBR Structure Smoke Replacement Parent");
            GameObject assemblyRoot = new GameObject("TGBR Structure Smoke Pylon Assembly");
            GameObject mountPoint = new GameObject("TGBR Structure Smoke Hardpoint");
            GameObject detachedPylon = new GameObject("TGBR Structure Smoke Detached Pylon");
            GameObject detachedPartRoot = new GameObject("TGBR Structure Smoke Detached UnitPart");
            GameObject detachedAssemblyProbe =
                new GameObject("TGBR Structure Smoke Detached Assembly");
            Hardpoint hardpoint = null;
            try
            {
                originalParent.transform.SetParent(previewAircraft.transform, false);
                originalParent.transform.localPosition = new Vector3(0.5f, 0.25f, -0.75f);
                originalParent.transform.localRotation = Quaternion.Euler(0f, 15f, 0f);
                originalParent.transform.localScale = Vector3.one * 1.1f;
                replacementParent.transform.SetParent(previewAircraft.transform, false);
                replacementParent.transform.localPosition = new Vector3(-0.8f, 0.4f, 0.65f);
                replacementParent.transform.localRotation = Quaternion.Euler(0f, -20f, 0f);
                replacementParent.transform.localScale = Vector3.one * 0.9f;
                detachedAssemblyProbe.transform.SetParent(detachedPartRoot.transform, false);
                bool detachedScopePassed = IsPylonAssemblyCandidateWithinScope(
                    detachedAssemblyProbe.transform,
                    previewAircraft.transform,
                    detachedPartRoot.transform);
                assemblyRoot.transform.SetParent(originalParent.transform, false);
                assemblyRoot.transform.localPosition = new Vector3(0.2f, -0.1f, 0.35f);
                MeshRenderer assemblyRenderer = assemblyRoot.AddComponent<MeshRenderer>();
                mountPoint.transform.SetParent(assemblyRoot.transform, false);
                mountPoint.transform.localPosition = new Vector3(1f, 2f, 3f);
                mountPoint.transform.localRotation = Quaternion.Euler(10f, 20f, 30f);
                mountPoint.transform.localScale = new Vector3(1.1f, 0.9f, 1.2f);
                Vector3 originalLocalPosition = mountPoint.transform.localPosition;
                Quaternion originalLocalRotation = mountPoint.transform.localRotation;
                Vector3 originalLocalScale = mountPoint.transform.localScale;
                Vector3 pylonAnchorOffset = new Vector3(0.2f, -0.1f, 0.35f);
                Quaternion pylonAnchorRotation = Quaternion.Euler(2f, 5f, 8f);
                detachedPylon.transform.SetPositionAndRotation(
                    mountPoint.transform.TransformPoint(pylonAnchorOffset),
                    mountPoint.transform.rotation * pylonAnchorRotation);
                MeshRenderer detachedPylonRenderer = detachedPylon.AddComponent<MeshRenderer>();
                hardpoint = new Hardpoint
                {
                    transform = mountPoint.transform,
                    part = null,
                    bayDoors = Array.Empty<BayDoor>(),
                    Pylon = assemblyRenderer,
                    Plug = detachedPylonRenderer,
                    BuiltInWeapons = Array.Empty<Weapon>(),
                    BuiltInTurrets = Array.Empty<Turret>(),
                    HardpointIndex = -1
                };
                SetEmptyPylonOptions(hardpoint);
                RememberOriginalTransform(hardpoint);
                PylonStationBinding smokeBinding =
                    GetOrCreatePylonStationBinding(hardpoint);
                Renderer renderer = CloneTemplateRenderer(
                    template,
                    hardpoint,
                    smokeBinding.ModelSlot);
                SetRendererEnabledIfOwned(assemblyRenderer, hardpoint, false);
                SetPylonRendererReferences(hardpoint, renderer, renderer);
                smokeBinding.GeneratedModel = renderer?.gameObject;
                GameObject mountedStore = new GameObject("TGBR Structure Smoke Mounted Store");
                mountedStore.transform.SetParent(mountPoint.transform, false);
                mountedStore.transform.localPosition = new Vector3(0.1f, -0.25f, 0.4f);
                Vector3 storeLocalPosition = mountedStore.transform.localPosition;

                Transform resolvedAssembly = ResolvePylonAssemblyRoot(hardpoint);
                Vector3 originalAssemblyPosition = assemblyRoot.transform.localPosition;
                Vector3 anchorBeforeOffset = mountPoint.transform.localPosition;
                MutateWeaponAnchorTransform(
                    hardpoint,
                    transform => transform.localPosition += new Vector3(0.15f, 0f, 0f));
                bool anchorLayerPassed =
                    resolvedAssembly == assemblyRoot.transform &&
                    assemblyRoot.transform.localPosition == originalAssemblyPosition &&
                    mountPoint.transform.localPosition != anchorBeforeOffset;
                MutateWeaponAnchorTransform(
                    hardpoint,
                    transform => transform.localPosition = anchorBeforeOffset);

                Vector3 detachedAssemblyOffset = assemblyRoot.transform.InverseTransformPoint(
                    detachedPylon.transform.position);
                MutatePylonAssemblyTransform(
                    hardpoint,
                    assemblyRoot.transform,
                    transform => transform.localPosition += new Vector3(0f, 0.2f, 0f));
                bool assemblyLayerPassed =
                    mountPoint.transform.localPosition == anchorBeforeOffset &&
                    Vector3.Distance(
                        detachedPylon.transform.position,
                        assemblyRoot.transform.TransformPoint(detachedAssemblyOffset)) < 0.0001f;
                ResetPylonAssemblyTransform(hardpoint);
                bool assemblyResetPassed =
                    assemblyRoot.transform.localPosition == originalAssemblyPosition;

                Vector3 editedPosition = new Vector3(4f, 5f, 6f);
                Quaternion editedRotation = Quaternion.Euler(20f, 40f, 60f);
                Vector3 editedScale = new Vector3(0.8f, 1.2f, 1.4f);
                SetHardpointLocalTransform(
                    hardpoint,
                    editedPosition,
                    editedRotation,
                    editedScale);

                bool transformEdited =
                    mountPoint.transform.localPosition == editedPosition &&
                    Vector3.Distance(mountPoint.transform.localScale, editedScale) < 0.0001f;
                Vector3 rootWorldPosition = mountPoint.transform.position;
                Quaternion rootWorldRotation = mountPoint.transform.rotation;
                Vector3 rootWorldScale = mountPoint.transform.lossyScale;
                Vector3 storeWorldPosition = mountedStore.transform.position;
                Vector3 detachedPylonWorldPosition = detachedPylon.transform.position;
                Quaternion detachedPylonWorldRotation = detachedPylon.transform.rotation;
                ReparentHardpointRootPreservingWorld(hardpoint, replacementParent.transform);
                bool reparentPassed =
                    mountPoint.transform.parent == replacementParent.transform &&
                    Vector3.Distance(mountPoint.transform.position, rootWorldPosition) < 0.0001f &&
                    Quaternion.Angle(mountPoint.transform.rotation, rootWorldRotation) < 0.001f &&
                    Vector3.Distance(mountPoint.transform.lossyScale, rootWorldScale) < 0.0001f &&
                    Vector3.Distance(mountedStore.transform.position, storeWorldPosition) < 0.0001f &&
                    Vector3.Distance(detachedPylon.transform.position, detachedPylonWorldPosition) <
                    0.0001f &&
                    Quaternion.Angle(detachedPylon.transform.rotation, detachedPylonWorldRotation) <
                    0.001f;

                HardpointSet first = new HardpointSet
                {
                    precludingHardpointSets = new List<byte>()
                };
                HardpointSet second = new HardpointSet
                {
                    precludingHardpointSets = new List<byte>()
                };
                SetMutualPreclusion(first, 0, second, 1, enabled: true);

                bool storeFollowed = mountedStore.transform.localPosition == storeLocalPosition;
                bool pylonStayedWithAssembly =
                    Vector3.Distance(
                        detachedPylon.transform.position,
                        assemblyRoot.transform.TransformPoint(detachedAssemblyOffset)) < 0.0001f;
                bool linksPassed = ContainsPreclusion(first, 1) && ContainsPreclusion(second, 0);
                bool rendererPassed = renderer != null &&
                                      renderer.transform.parent == smokeBinding.ModelSlot &&
                                      smokeBinding.ModelSlot.parent == assemblyRoot.transform &&
                                      renderer.transform.childCount == 0 &&
                                      renderer.GetComponent<MeshFilter>() != null &&
                                      renderer.GetComponent<Collider>() == null &&
                                      renderer.GetComponents<Component>().Length == 3 &&
                                      GetBasePylonRenderer(hardpoint) == renderer;
                ResetHardpointTransform(hardpoint);
                bool resetPassed =
                    mountPoint.transform.parent == assemblyRoot.transform &&
                    Vector3.Distance(mountPoint.transform.localPosition, originalLocalPosition) <
                    0.0001f &&
                    Quaternion.Angle(mountPoint.transform.localRotation, originalLocalRotation) <
                    0.001f &&
                    Vector3.Distance(mountPoint.transform.localScale, originalLocalScale) < 0.0001f;
                RestoreOriginalPylonRendererReferences(hardpoint, smokeBinding);
                bool originalModelRestored =
                    hardpoint.Pylon == assemblyRenderer &&
                    GetBasePylonRenderer(hardpoint) == assemblyRenderer &&
                    assemblyRenderer.enabled &&
                    smokeBinding.GeneratedModel == null &&
                    smokeBinding.AssemblyRoot == assemblyRoot.transform &&
                    mountPoint.transform.localPosition == originalLocalPosition;
                if (!anchorLayerPassed || !assemblyLayerPassed || !assemblyResetPassed ||
                    !transformEdited || !reparentPassed || !resetPassed || !storeFollowed ||
                    !pylonStayedWithAssembly || !linksPassed || !rendererPassed ||
                    !originalModelRestored || !detachedScopePassed)
                {
                    failure = "the temporary assembly/anchor layers, parent preservation/reset, " +
                              "store, stable model slot, passive renderer, original restoration, " +
                              "or links failed";
                    return false;
                }
                return true;
            }
            catch (Exception exception)
            {
                failure = exception.Message;
                return false;
            }
            finally
            {
                if (hardpoint != null)
                {
                    originalTransforms.Remove(hardpoint);
                    originalAssemblyTransforms.Remove(hardpoint);
                    pylonStationBindings.Remove(hardpoint);
                }
                UnityEngine.Object.Destroy(mountPoint);
                UnityEngine.Object.Destroy(detachedPylon);
                UnityEngine.Object.Destroy(assemblyRoot);
                UnityEngine.Object.Destroy(detachedAssemblyProbe);
                UnityEngine.Object.Destroy(detachedPartRoot);
                UnityEngine.Object.Destroy(originalParent);
                UnityEngine.Object.Destroy(replacementParent);
            }
        }

        private bool ValidateTernionAssemblyResolution(out string failure)
        {
            int expectedAssemblies = 0;
            int resolvedAssemblies = 0;
            foreach (HardpointSet set in weaponManager.hardpointSets)
            {
                if (set?.hardpoints == null)
                {
                    continue;
                }
                foreach (Hardpoint hardpoint in set.hardpoints)
                {
                    Transform anchor = hardpoint?.transform;
                    if (anchor == null)
                    {
                        continue;
                    }
                    bool hasAncestorPylon = false;
                    foreach (Renderer renderer in GetReferencedPylonRenderers(hardpoint))
                    {
                        if (renderer?.transform != null &&
                            anchor.IsChildOf(renderer.transform))
                        {
                            hasAncestorPylon = true;
                            break;
                        }
                    }
                    if (!hasAncestorPylon)
                    {
                        continue;
                    }
                    expectedAssemblies++;
                    PylonStationBinding binding =
                        GetOrCreatePylonStationBinding(hardpoint);
                    Transform assembly = binding?.AssemblyRoot;
                    if (assembly != null && assembly != anchor &&
                        anchor.IsChildOf(assembly) &&
                        binding.ModelSlot != null &&
                        binding.ModelSlot.parent == assembly &&
                        binding.GeneratedModel == null)
                    {
                        resolvedAssemblies++;
                    }
                }
            }
            if (expectedAssemblies < 10 || resolvedAssemblies != expectedAssemblies)
            {
                failure = "Ternion stable assembly/model-slot binding returned " +
                          resolvedAssemblies + "/" + expectedAssemblies +
                          " distinct assembly roots";
                return false;
            }
            failure = null;
            return true;
        }
    }

    [HarmonyPatch(typeof(CameraStateManager), "LateUpdate")]
    internal static class PylonEditorFreeCameraLateUpdatePatch
    {
        private static void Postfix(CameraStateManager __instance)
        {
            AircraftSelectorPylonEditor.ApplyActiveFreeCamera(__instance);
        }
    }
}

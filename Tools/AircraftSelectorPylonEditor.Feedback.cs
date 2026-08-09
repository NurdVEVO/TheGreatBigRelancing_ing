using System.Collections.Generic;
using UnityEngine;

namespace TheGreatBigRebalancing.Tools
{
    internal sealed partial class AircraftSelectorPylonEditor
    {
        private sealed class UnitPartHighlightSnapshot
        {
            internal Renderer Renderer;
            internal MaterialPropertyBlock OriginalProperties;
        }

        private const float NearestPartFeedbackDuration = 1.75f;
        private static readonly int BaseColorProperty = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorProperty = Shader.PropertyToID("_Color");
        private static readonly int EmissionColorProperty = Shader.PropertyToID("_EmissionColor");

        private readonly List<UnitPartHighlightSnapshot> nearestPartHighlightSnapshots =
            new List<UnitPartHighlightSnapshot>();
        private readonly List<Collider> nearestPartFeedbackColliders =
            new List<Collider>();
        private readonly MaterialPropertyBlock nearestPartWorkingProperties =
            new MaterialPropertyBlock();
        private GameObject nearestPartLineObject;
        private LineRenderer nearestPartLine;
        private GameObject nearestPartOutlineObject;
        private LineRenderer nearestPartOutline;
        private Material nearestPartLineMaterial;
        private Hardpoint nearestPartFeedbackHardpoint;
        private UnitPart nearestPartFeedbackPart;
        private float nearestPartFeedbackStarted;
        private float nearestPartFeedbackEnds;
        private bool nearestPartFeedbackSmokeValidated;

        private void ChooseNearestUnitPart(Hardpoint hardpoint)
        {
            if (hardpoint?.transform == null || parts.Count == 0)
            {
                status = "No weapon anchor or damageable UnitPart is available.";
                return;
            }

            UnitPart nearest = FindNearestUnitPart(hardpoint, out float nearestDistance);
            if (nearest == null)
            {
                status = "No damageable UnitPart could be resolved for this anchor.";
                return;
            }
            hardpoint.part = nearest;
            StartNearestPartFeedback(hardpoint, nearest);
            status = "Linked " + GetEditor2Metadata(hardpoint, false).DisplayName +
                     " to nearest damageable part " + PartLabel(nearest) +
                     " (" + FormatFloat(Mathf.Sqrt(nearestDistance)) + " m).";
        }

        private UnitPart FindNearestUnitPart(Hardpoint hardpoint, out float nearestDistance)
        {
            UnitPart nearest = null;
            nearestDistance = float.PositiveInfinity;
            if (hardpoint?.transform == null)
            {
                return null;
            }
            Vector3 anchorPosition = hardpoint.transform.position;
            foreach (UnitPart candidate in parts)
            {
                if (candidate?.transform == null)
                {
                    continue;
                }
                Vector3 point = ClosestPointOnUnitPart(candidate, anchorPosition);
                float distance = (point - anchorPosition).sqrMagnitude;
                if (distance < nearestDistance - 0.000001f ||
                    (Mathf.Abs(distance - nearestDistance) <= 0.000001f &&
                     string.CompareOrdinal(
                         GetTransformPath(candidate.transform),
                         GetTransformPath(nearest?.transform)) < 0))
                {
                    nearest = candidate;
                    nearestDistance = distance;
                }
            }
            return nearest;
        }

        private static Vector3 ClosestPointOnUnitPart(UnitPart part, Vector3 position)
        {
            Vector3 closest = part.transform.position;
            float closestDistance = (closest - position).sqrMagnitude;
            foreach (Renderer renderer in part.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null ||
                    renderer.GetComponentInParent<UnitPart>() != part)
                {
                    continue;
                }
                Vector3 point = renderer.bounds.ClosestPoint(position);
                float distance = (point - position).sqrMagnitude;
                if (distance < closestDistance)
                {
                    closest = point;
                    closestDistance = distance;
                }
            }
            foreach (Collider collider in part.GetComponentsInChildren<Collider>(true))
            {
                if (collider == null || collider.GetComponentInParent<UnitPart>() != part)
                {
                    continue;
                }
                Vector3 point = collider.bounds.ClosestPoint(position);
                float distance = (point - position).sqrMagnitude;
                if (distance < closestDistance)
                {
                    closest = point;
                    closestDistance = distance;
                }
            }
            return closest;
        }

        private void StartNearestPartFeedback(Hardpoint hardpoint, UnitPart part)
        {
            StopNearestPartFeedback();
            nearestPartFeedbackHardpoint = hardpoint;
            nearestPartFeedbackPart = part;
            nearestPartFeedbackStarted = Time.unscaledTime;
            nearestPartFeedbackEnds = nearestPartFeedbackStarted + NearestPartFeedbackDuration;

            foreach (Renderer renderer in part.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null ||
                    renderer.GetComponentInParent<UnitPart>() != part)
                {
                    continue;
                }
                MaterialPropertyBlock original = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(original);
                nearestPartHighlightSnapshots.Add(new UnitPartHighlightSnapshot
                {
                    Renderer = renderer,
                    OriginalProperties = original
                });
            }
            foreach (Collider collider in part.GetComponentsInChildren<Collider>(true))
            {
                if (collider != null && collider.GetComponentInParent<UnitPart>() == part)
                {
                    nearestPartFeedbackColliders.Add(collider);
                }
            }
            EnsureNearestPartLine(hardpoint.transform);
            UpdateNearestPartFeedback();
        }

        private void EnsureNearestPartLine(Transform anchor)
        {
            if (nearestPartLine == null)
            {
                nearestPartLineObject = new GameObject("TGBR Nearest UnitPart Feedback");
                nearestPartLineObject.hideFlags = HideFlags.HideAndDontSave;
                nearestPartLine = nearestPartLineObject.AddComponent<LineRenderer>();
                nearestPartLine.useWorldSpace = true;
                nearestPartLine.positionCount = 2;
                nearestPartLine.alignment = LineAlignment.View;
                nearestPartLine.textureMode = LineTextureMode.Tile;
                nearestPartLine.numCapVertices = 4;
                nearestPartLine.numCornerVertices = 2;
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ??
                                Shader.Find("Sprites/Default") ??
                                Shader.Find("Unlit/Color");
                if (shader != null)
                {
                    nearestPartLineMaterial = new Material(shader)
                    {
                        hideFlags = HideFlags.HideAndDontSave,
                        name = "TGBR Nearest UnitPart Feedback Material"
                    };
                    nearestPartLineMaterial.color = Color.white;
                    nearestPartLine.sharedMaterial = nearestPartLineMaterial;
                }
            }
            if (nearestPartOutline == null)
            {
                nearestPartOutlineObject = new GameObject("TGBR UnitPart Highlight Outline");
                nearestPartOutlineObject.hideFlags = HideFlags.HideAndDontSave;
                nearestPartOutline = nearestPartOutlineObject.AddComponent<LineRenderer>();
                nearestPartOutline.useWorldSpace = true;
                nearestPartOutline.positionCount = 16;
                nearestPartOutline.alignment = LineAlignment.View;
                nearestPartOutline.textureMode = LineTextureMode.Stretch;
                nearestPartOutline.numCapVertices = 2;
                nearestPartOutline.numCornerVertices = 2;
                nearestPartOutline.sharedMaterial = nearestPartLineMaterial;
            }
            if (nearestPartLineObject != null)
            {
                nearestPartLineObject.layer = anchor.gameObject.layer;
                nearestPartLineObject.SetActive(true);
            }
            if (nearestPartOutlineObject != null)
            {
                nearestPartOutlineObject.layer = anchor.gameObject.layer;
                nearestPartOutlineObject.SetActive(true);
            }
            if (nearestPartLine != null)
            {
                nearestPartLine.enabled = true;
            }
            if (nearestPartOutline != null)
            {
                nearestPartOutline.enabled = true;
            }
        }

        private void UpdateNearestPartFeedback()
        {
            if (nearestPartFeedbackHardpoint?.transform == null ||
                nearestPartFeedbackPart?.transform == null ||
                Time.unscaledTime >= nearestPartFeedbackEnds)
            {
                if (nearestPartFeedbackPart != null || nearestPartLine != null && nearestPartLine.enabled)
                {
                    StopNearestPartFeedback();
                }
                return;
            }

            float normalized = Mathf.Clamp01(
                (Time.unscaledTime - nearestPartFeedbackStarted) / NearestPartFeedbackDuration);
            float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.65f, 1f, normalized));
            float pulse = 0.65f + 0.35f * Mathf.Sin(normalized * Mathf.PI * 8f);
            Color lineColor = new Color(0.25f, 1f, 0.35f, fade);
            Bounds feedbackBounds = GetUnitPartFeedbackBounds(nearestPartFeedbackPart);
            if (nearestPartLine != null)
            {
                Vector3 anchorPosition = nearestPartFeedbackHardpoint.transform.position;
                Vector3 partPosition = feedbackBounds.ClosestPoint(anchorPosition);
                nearestPartLine.SetPosition(0, anchorPosition);
                nearestPartLine.SetPosition(1, partPosition);
                nearestPartLine.startWidth = Mathf.Lerp(0.025f, 0.07f, pulse);
                nearestPartLine.endWidth = Mathf.Lerp(0.07f, 0.025f, pulse);
                nearestPartLine.startColor = lineColor;
                nearestPartLine.endColor = new Color(1f, 0.85f, 0.15f, fade);
            }
            if (nearestPartOutline != null)
            {
                SetOutlinePositions(nearestPartOutline, feedbackBounds);
                float width = Mathf.Lerp(0.018f, 0.055f, pulse);
                nearestPartOutline.startWidth = width;
                nearestPartOutline.endWidth = width;
                nearestPartOutline.startColor = new Color(0.25f, 1f, 0.35f, fade);
                nearestPartOutline.endColor = new Color(1f, 0.85f, 0.15f, fade);
            }

            Color highlight = Color.Lerp(
                new Color(0.15f, 0.8f, 0.2f, 1f),
                new Color(1f, 0.85f, 0.15f, 1f),
                pulse);
            highlight *= Mathf.Lerp(0.75f, 1.35f, fade);
            foreach (UnitPartHighlightSnapshot snapshot in nearestPartHighlightSnapshots)
            {
                if (snapshot.Renderer == null)
                {
                    continue;
                }
                nearestPartWorkingProperties.Clear();
                snapshot.Renderer.GetPropertyBlock(nearestPartWorkingProperties);
                nearestPartWorkingProperties.SetColor(BaseColorProperty, highlight);
                nearestPartWorkingProperties.SetColor(ColorProperty, highlight);
                nearestPartWorkingProperties.SetColor(
                    EmissionColorProperty,
                    highlight * 1.5f);
                snapshot.Renderer.SetPropertyBlock(nearestPartWorkingProperties);
            }
        }

        private void StopNearestPartFeedback()
        {
            foreach (UnitPartHighlightSnapshot snapshot in nearestPartHighlightSnapshots)
            {
                if (snapshot.Renderer != null)
                {
                    snapshot.Renderer.SetPropertyBlock(snapshot.OriginalProperties);
                }
            }
            nearestPartHighlightSnapshots.Clear();
            nearestPartFeedbackColliders.Clear();
            nearestPartFeedbackHardpoint = null;
            nearestPartFeedbackPart = null;
            nearestPartFeedbackStarted = 0f;
            nearestPartFeedbackEnds = 0f;
            if (nearestPartLine != null)
            {
                nearestPartLine.enabled = false;
            }
            if (nearestPartLineObject != null)
            {
                nearestPartLineObject.SetActive(false);
            }
            if (nearestPartOutline != null)
            {
                nearestPartOutline.enabled = false;
            }
            if (nearestPartOutlineObject != null)
            {
                nearestPartOutlineObject.SetActive(false);
            }
        }

        private void DisposeNearestPartFeedback()
        {
            StopNearestPartFeedback();
            if (nearestPartLineObject != null)
            {
                UnityEngine.Object.Destroy(nearestPartLineObject);
            }
            if (nearestPartOutlineObject != null)
            {
                UnityEngine.Object.Destroy(nearestPartOutlineObject);
            }
            if (nearestPartLineMaterial != null)
            {
                UnityEngine.Object.Destroy(nearestPartLineMaterial);
            }
            nearestPartLineObject = null;
            nearestPartLine = null;
            nearestPartOutlineObject = null;
            nearestPartOutline = null;
            nearestPartLineMaterial = null;
        }

        private bool RunNearestPartFeedbackLiveSmokeTest(out string failure)
        {
            failure = null;
            Hardpoint testHardpoint = null;
            if (weaponManager?.hardpointSets != null)
            {
                foreach (HardpointSet set in weaponManager.hardpointSets)
                {
                    if (set?.hardpoints == null)
                    {
                        continue;
                    }
                    foreach (Hardpoint candidate in set.hardpoints)
                    {
                        UnitPart candidateNearest =
                            FindNearestUnitPart(candidate, out _);
                        if (candidate?.transform == null || candidateNearest == null)
                        {
                            continue;
                        }
                        testHardpoint = candidate;
                        break;
                    }
                    if (testHardpoint != null)
                    {
                        break;
                    }
                }
            }
            UnitPart nearest = FindNearestUnitPart(testHardpoint, out float distance);
            if (testHardpoint == null || nearest == null || float.IsInfinity(distance))
            {
                failure = "no hardpoint/UnitPart pair was available";
                return false;
            }
            try
            {
                StartNearestPartFeedback(testHardpoint, nearest);
                UpdateNearestPartFeedback();
                bool started = nearestPartFeedbackPart == nearest &&
                               nearestPartLine != null && nearestPartLine.enabled &&
                               nearestPartLine.positionCount == 2 &&
                               nearestPartOutline != null && nearestPartOutline.enabled &&
                               nearestPartOutline.positionCount == 16 &&
                               nearestPartFeedbackEnds > Time.unscaledTime;
                StopNearestPartFeedback();
                bool stopped = nearestPartFeedbackPart == null &&
                               nearestPartHighlightSnapshots.Count == 0 &&
                               nearestPartLine != null && !nearestPartLine.enabled &&
                               nearestPartOutline != null && !nearestPartOutline.enabled;
                if (!started || !stopped)
                {
                    failure = "line/highlight startup or deterministic cleanup failed";
                    return false;
                }
                return true;
            }
            catch (System.Exception exception)
            {
                failure = exception.Message;
                StopNearestPartFeedback();
                return false;
            }
        }

        private Bounds GetUnitPartFeedbackBounds(UnitPart part)
        {
            Bounds bounds = new Bounds(
                part?.transform == null ? Vector3.zero : part.transform.position,
                Vector3.zero);
            bool found = false;
            if (part == null)
            {
                return bounds;
            }
            foreach (UnitPartHighlightSnapshot snapshot in nearestPartHighlightSnapshots)
            {
                Renderer renderer = snapshot.Renderer;
                if (renderer == null)
                {
                    continue;
                }
                if (!found)
                {
                    bounds = renderer.bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }
            foreach (Collider collider in nearestPartFeedbackColliders)
            {
                if (collider == null)
                {
                    continue;
                }
                if (!found)
                {
                    bounds = collider.bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(collider.bounds);
                }
            }
            if (!found || bounds.size.sqrMagnitude < 0.0001f)
            {
                bounds = new Bounds(part.transform.position, Vector3.one * 0.7f);
            }
            return bounds;
        }

        private static void SetOutlinePositions(LineRenderer line, Bounds bounds)
        {
            Vector3 min = bounds.min;
            Vector3 max = bounds.max;
            Vector3 c0 = new Vector3(min.x, min.y, min.z);
            Vector3 c1 = new Vector3(max.x, min.y, min.z);
            Vector3 c2 = new Vector3(max.x, max.y, min.z);
            Vector3 c3 = new Vector3(min.x, max.y, min.z);
            Vector3 c4 = new Vector3(min.x, min.y, max.z);
            Vector3 c5 = new Vector3(max.x, min.y, max.z);
            Vector3 c6 = new Vector3(max.x, max.y, max.z);
            Vector3 c7 = new Vector3(min.x, max.y, max.z);
            line.SetPosition(0, c0);
            line.SetPosition(1, c1);
            line.SetPosition(2, c2);
            line.SetPosition(3, c3);
            line.SetPosition(4, c0);
            line.SetPosition(5, c4);
            line.SetPosition(6, c5);
            line.SetPosition(7, c1);
            line.SetPosition(8, c5);
            line.SetPosition(9, c6);
            line.SetPosition(10, c2);
            line.SetPosition(11, c6);
            line.SetPosition(12, c7);
            line.SetPosition(13, c3);
            line.SetPosition(14, c7);
            line.SetPosition(15, c4);
        }
    }
}

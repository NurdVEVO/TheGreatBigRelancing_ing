using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace TheGreatBigRebalancing.Balance
{
    internal static class CompassDryMassRebalance
    {
        private const string CompassDefinitionKey = "trainer";
        private const string CompassDisplayName = "T/A-30 Compass";
        private const float Pre034StructuralMass = 5320f;

        private static readonly Dictionary<string, float> Pre034PartMasses =
            new Dictionary<string, float>(StringComparer.Ordinal)
            {
                { "cockpit_F", 600f },
                { "engine_L", 300f },
                { "engine_R", 300f },
                { "intake_L", 200f },
                { "intake_R", 200f }
            };

        private static bool loggedApply;

        internal static void ApplyToEncyclopedia(Encyclopedia encyclopedia)
        {
            AircraftDefinition definition = encyclopedia?.aircraft?.Find(IsCompass);
            if (definition?.unitPrefab == null)
            {
                Plugin.ModLogger?.LogError(
                    "Compass dry-mass rebalance could not find the trainer aircraft definition or prefab.");
                return;
            }

            UnitPart[] parts = definition.unitPrefab.GetComponentsInChildren<UnitPart>(true);
            if (!TryResolveAdjustedParts(parts, out Dictionary<string, UnitPart> adjustedParts))
            {
                return;
            }

            Turbojet[] engines = definition.unitPrefab.GetComponentsInChildren<Turbojet>(true);
            float thrustBefore = SumThrust(engines);

            foreach (KeyValuePair<string, float> mass in Pre034PartMasses)
            {
                adjustedParts[mass.Key].mass = mass.Value;
            }

            // CacheMass is what Nuclear Option uses for simplified remote physics. Recompute it
            // from the restored structural parts so local and distant simulations agree.
            definition.CacheMass();

            float thrustAfter = SumThrust(engines);
            float structuralMass = SumMass(parts);
            if (!Mathf.Approximately(structuralMass, Pre034StructuralMass) ||
                !Mathf.Approximately(definition.mass, Pre034StructuralMass))
            {
                Plugin.ModLogger?.LogError(
                    "Compass dry-mass rebalance produced an unexpected structural mass: parts=" +
                    structuralMass + " kg, cached=" + definition.mass + " kg; expected " +
                    Pre034StructuralMass + " kg.");
                return;
            }

            if (!Mathf.Approximately(thrustBefore, thrustAfter))
            {
                Plugin.ModLogger?.LogError(
                    "Compass dry-mass rebalance unexpectedly changed total engine thrust from " +
                    thrustBefore + " N to " + thrustAfter + " N.");
                return;
            }

            if (!loggedApply)
            {
                loggedApply = true;
                Plugin.ModLogger?.LogInfo(
                    "Compass dry-mass rebalance active: restored the pre-0.34 structural mass of " +
                    Pre034StructuralMass + " kg while preserving update 0.34 engine thrust at " +
                    thrustAfter + " N total.");
            }
        }

        internal static void ApplyToUnitPart(UnitPart part)
        {
            if (part == null || !IsCompass(part.parentUnit?.definition as AircraftDefinition) ||
                !Pre034PartMasses.TryGetValue(part.gameObject.name, out float pre034Mass))
            {
                return;
            }

            // This runs before UnitPart.Awake captures baseMass, keeping damage, detachment,
            // fuel/store mass, and center-of-mass calculations on the restored baseline.
            part.mass = pre034Mass;
        }

        private static bool TryResolveAdjustedParts(
            UnitPart[] parts,
            out Dictionary<string, UnitPart> adjustedParts)
        {
            adjustedParts = new Dictionary<string, UnitPart>(StringComparer.Ordinal);
            foreach (UnitPart part in parts)
            {
                if (part == null || !Pre034PartMasses.ContainsKey(part.gameObject.name))
                {
                    continue;
                }

                if (adjustedParts.ContainsKey(part.gameObject.name))
                {
                    Plugin.ModLogger?.LogError(
                        "Compass dry-mass rebalance found more than one part named '" +
                        part.gameObject.name + "'; no mass values were changed.");
                    return false;
                }

                adjustedParts.Add(part.gameObject.name, part);
            }

            foreach (string requiredName in Pre034PartMasses.Keys)
            {
                if (!adjustedParts.ContainsKey(requiredName))
                {
                    Plugin.ModLogger?.LogError(
                        "Compass dry-mass rebalance could not find required part '" + requiredName +
                        "'; no mass values were changed.");
                    return false;
                }
            }

            return true;
        }

        private static float SumMass(UnitPart[] parts)
        {
            float total = 0f;
            foreach (UnitPart part in parts)
            {
                if (part != null)
                {
                    total += part.mass;
                }
            }
            return total;
        }

        private static float SumThrust(Turbojet[] engines)
        {
            float total = 0f;
            foreach (Turbojet engine in engines)
            {
                if (engine != null)
                {
                    total += engine.maxThrust;
                }
            }
            return total;
        }

        private static bool IsCompass(AircraftDefinition definition)
        {
            return definition != null &&
                   (string.Equals(
                        definition.jsonKey,
                        CompassDefinitionKey,
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        definition.unitName,
                        CompassDisplayName,
                        StringComparison.OrdinalIgnoreCase));
        }
    }

    [HarmonyPatch]
    internal static class CompassEncyclopediaAfterLoadPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Encyclopedia), "AfterLoad", Type.EmptyTypes);
        }

        private static void Postfix(Encyclopedia __instance)
        {
            CompassDryMassRebalance.ApplyToEncyclopedia(__instance);
        }
    }

    [HarmonyPatch]
    internal static class CompassUnitPartAwakePatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(UnitPart), "Awake", Type.EmptyTypes);
        }

        private static void Prefix(UnitPart __instance)
        {
            CompassDryMassRebalance.ApplyToUnitPart(__instance);
        }
    }
}

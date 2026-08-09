using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace TheGreatBigRebalancing.Visuals
{
    /// <summary>
    /// Gives each distinct aircraft exhaust point one pooled missile-style ribbon.
    /// Aircraft transforms are already synchronized by the game, so compatible clients
    /// independently reproduce the same altitude gate without additional network traffic.
    /// </summary>
    internal sealed class HighAltitudeEngineSmoke : IDisposable
    {
        internal const float FullStrengthAltitudeMeters = 7500f;
        internal const int FullStrengthAltitudeFeet = 24606;
        internal const float MinimumFormationAltitudeMeters = 6000f;
        internal const int MinimumFormationAltitudeFeet = 19685;

        private const float FormationHysteresisMeters = 150f;
        private const float MinimumAirspeedMetersPerSecond = 40f;
        private const float SmokeAppearanceOffsetMeters = 4.572f;
        private const float RibbonPointSpacingMeters = 12f;
        private const float MaximumContinuousStepMeters = 1500f;
        private const int MaximumPointsPerNozzlePerFrame = 16;
        private const int MaximumParticlesPerRibbon = 1024;
        private const float ParticleLifetimeSeconds = 15f;
        private const float RibbonWidthSourceSizeMeters = 20f;
        private const float AircraftDiscoveryIntervalSeconds = 0.5f;
        private const float DormantAircraftUpdateIntervalSeconds = 0.2f;

        private static readonly FieldInfo JetNozzleThrustTransformField =
            AccessTools.Field(typeof(JetNozzle), "thrustTransform");

        private static readonly FieldInfo MissileTrailSystemField =
            AccessTools.Field(typeof(TrailEmitter), "trailSystem");

        private sealed class RibbonSystem
        {
            internal GameObject GameObject;
            internal ParticleSystem Particles;
            internal bool InUse;
        }

        private sealed class NozzleState
        {
            internal Transform Transform;
            internal JetNozzle JetNozzle;
            internal IEngine Engine;
            internal RibbonSystem Ribbon;
            internal GlobalPosition LastPosition;
            internal float DistanceCarry;
            internal float ReleaseAtTime;
            internal uint RandomSeed;
            internal bool HasLastPosition;
            internal bool WasEmitting;
        }

        private sealed class AircraftState
        {
            internal Aircraft Aircraft;
            internal NozzleState[] Nozzles;
            internal bool AltitudeActive;
            internal bool UsesEngineFallback;
            internal bool HasOwnedRibbon;
            internal float NextDormantUpdateTime;
        }

        private readonly ManualLogSource logger;
        private readonly Dictionary<int, AircraftState> aircraftStates =
            new Dictionary<int, AircraftState>();
        private readonly List<int> staleAircraft = new List<int>();
        private readonly List<RibbonSystem> allRibbons = new List<RibbonSystem>();
        private readonly Stack<RibbonSystem> availableRibbons = new Stack<RibbonSystem>();

        private ParticleSystem missileTemplate;
        private Transform simulationOrigin;
        private float nextAircraftDiscoveryTime;
        private float nextTemplateSearchTime;
        private bool loggedMissingMissileSmoke;
        private bool disposed;

        internal HighAltitudeEngineSmoke(ManualLogSource logger)
        {
            this.logger = logger;

            if (!ValidateAltitudeGradient())
            {
                logger.LogError("High-altitude contrail visibility-gradient self-test failed.");
            }
            else
            {
                logger.LogInfo(
                    "High-altitude contrail visibility-gradient self-test passed: 0% at " +
                    MinimumFormationAltitudeMeters.ToString("0", CultureInfo.InvariantCulture) +
                    " m, 50% at " +
                    ((MinimumFormationAltitudeMeters + FullStrengthAltitudeMeters) * 0.5f)
                        .ToString("0", CultureInfo.InvariantCulture) +
                    " m, and 100% at " +
                    FullStrengthAltitudeMeters.ToString("0", CultureInfo.InvariantCulture) +
                    " m.");
            }

            if (JetNozzleThrustTransformField == null)
            {
                logger.LogWarning(
                    "High-altitude contrails could not find JetNozzle.thrustTransform; " +
                    "affected aircraft will use engine transforms instead.");
            }

            if (MissileTrailSystemField == null)
            {
                logger.LogError(
                    "High-altitude contrails are incompatible with this game build: " +
                    "TrailEmitter.trailSystem was not found.");
            }
        }

        internal void Update()
        {
            if (disposed || GameManager.IsHeadless)
            {
                return;
            }

            if (missileTemplate == null)
            {
                TryResolveMissileTemplate();
            }

            float unscaledTime = Time.unscaledTime;
            if (unscaledTime >= nextAircraftDiscoveryTime)
            {
                nextAircraftDiscoveryTime = unscaledTime + AircraftDiscoveryIntervalSeconds;
                DiscoverAircraft();
            }

            if (missileTemplate == null)
            {
                return;
            }

            RefreshSimulationOrigin();
            EmitAircraftContrails(Time.time);
        }

        private void TryResolveMissileTemplate()
        {
            if (Time.unscaledTime < nextTemplateSearchTime)
            {
                return;
            }

            nextTemplateSearchTime = Time.unscaledTime + 1f;
            missileTemplate = FindMissileSmokeTemplate();
            if (missileTemplate == null)
            {
                if (!loggedMissingMissileSmoke && aircraftStates.Count > 0)
                {
                    logger.LogWarning(
                        "High-altitude contrails are waiting for a stock missile-smoke template.");
                    loggedMissingMissileSmoke = true;
                }

                return;
            }

            // Warm one pooled renderer immediately. This validates the complete cloned
            // component/material path during the bounded startup smoke test, before any
            // flyable aircraft needs a ribbon.
            RibbonSystem warmRibbon = CreateRibbonSystem();
            availableRibbons.Push(warmRibbon);
            logger.LogInfo(
                "High-altitude contrail ribbon pool ready with a smooth visibility gradient " +
                "from 0% at " +
                MinimumFormationAltitudeMeters.ToString("0", CultureInfo.InvariantCulture) +
                " m / " + MinimumFormationAltitudeFeet.ToString("N0", CultureInfo.InvariantCulture) +
                " ft to 100% at " +
                FullStrengthAltitudeMeters.ToString("0", CultureInfo.InvariantCulture) + " m / " +
                FullStrengthAltitudeFeet.ToString("N0", CultureInfo.InvariantCulture) +
                " ft ASL. Each distinct nozzle " +
                "lazily acquires exactly one pooled missile Ribbon with particle rendering " +
                "disabled, " +
                "appearing " + SmokeAppearanceOffsetMeters.ToString("0.###") +
                " m behind the engine and adding no network messages. Template: " +
                missileTemplate.gameObject.name + ".");
        }

        private void DiscoverAircraft()
        {
            List<Aircraft> aircraft = UnitRegistry.allAircraft;
            for (int i = 0; i < aircraft.Count; i++)
            {
                Aircraft candidate = aircraft[i];
                if (candidate == null)
                {
                    continue;
                }

                int instanceId = candidate.GetInstanceID();
                if (aircraftStates.ContainsKey(instanceId))
                {
                    continue;
                }

                bool usesEngineFallback;
                NozzleState[] nozzles =
                    FindEngineEmissionPoints(candidate, out usesEngineFallback);
                if (nozzles.Length == 0)
                {
                    // Component Awake ordering can leave engineStates empty for one scan.
                    // Do not cache the miss; the next inexpensive discovery pass retries it.
                    continue;
                }

                AircraftState state = new AircraftState
                {
                    Aircraft = candidate,
                    Nozzles = nozzles,
                    UsesEngineFallback = usesEngineFallback
                };
                aircraftStates.Add(instanceId, state);

                logger.LogDebug(
                    "High-altitude contrails registered " + candidate.name + " with " +
                    nozzles.Length + " single-ribbon emission point(s) using " +
                    (usesEngineFallback ? "IEngine fallback transforms" :
                        "exact JetNozzle thrust transforms") + ".");
            }

            staleAircraft.Clear();
            foreach (KeyValuePair<int, AircraftState> pair in aircraftStates)
            {
                if (pair.Value.Aircraft == null || pair.Value.Aircraft.disabled)
                {
                    staleAircraft.Add(pair.Key);
                }
            }

            for (int i = 0; i < staleAircraft.Count; i++)
            {
                AircraftState state = aircraftStates[staleAircraft[i]];
                ReleaseAircraftRibbons(state);
                aircraftStates.Remove(staleAircraft[i]);
            }
        }

        private static NozzleState[] FindEngineEmissionPoints(
            Aircraft aircraft,
            out bool usesEngineFallback)
        {
            List<NozzleState> nozzles = new List<NozzleState>();
            JetNozzle[] jetNozzles = aircraft.GetComponentsInChildren<JetNozzle>(true);

            if (JetNozzleThrustTransformField != null)
            {
                for (int i = 0; i < jetNozzles.Length; i++)
                {
                    Transform thrustTransform =
                        JetNozzleThrustTransformField.GetValue(jetNozzles[i]) as Transform;
                    AddDistinctNozzle(
                        nozzles,
                        thrustTransform,
                        jetNozzles[i],
                        null);
                }
            }

            usesEngineFallback = nozzles.Count == 0;
            if (usesEngineFallback)
            {
                for (int i = 0; i < aircraft.engineStates.Count; i++)
                {
                    IEngine engine = aircraft.engineStates[i];
                    if (engine != null)
                    {
                        AddDistinctNozzle(
                            nozzles,
                            engine.transform,
                            null,
                            engine);
                    }
                }
            }

            NozzleState[] result = nozzles.ToArray();
            uint aircraftSeed = unchecked((uint)aircraft.persistentID.GetHashCode());
            for (int i = 0; i < result.Length; i++)
            {
                result[i].RandomSeed =
                    aircraftSeed ^ unchecked((uint)(i + 1) * 0x9E3779B9u);
            }

            return result;
        }

        private static void AddDistinctNozzle(
            List<NozzleState> nozzles,
            Transform candidate,
            JetNozzle jetNozzle,
            IEngine engine)
        {
            if (candidate == null)
            {
                return;
            }

            for (int i = 0; i < nozzles.Count; i++)
            {
                if (nozzles[i].Transform == candidate ||
                    (nozzles[i].Transform.position - candidate.position).sqrMagnitude < 0.0625f)
                {
                    return;
                }
            }

            nozzles.Add(new NozzleState
            {
                Transform = candidate,
                JetNozzle = jetNozzle,
                Engine = engine
            });
        }

        private RibbonSystem AcquireRibbonSystem()
        {
            RibbonSystem ribbon = availableRibbons.Count > 0
                ? availableRibbons.Pop()
                : CreateRibbonSystem();
            ribbon.InUse = true;
            ribbon.GameObject.SetActive(true);
            ConfigureSimulationOrigin(ribbon);
            ribbon.Particles.Clear(true);
            return ribbon;
        }

        private RibbonSystem CreateRibbonSystem()
        {
            GameObject ribbonObject = UnityEngine.Object.Instantiate(missileTemplate.gameObject);
            ribbonObject.name = "TGBR Pooled Contrail Ribbon " + allRibbons.Count;
            ribbonObject.transform.SetParent(null, false);
            ribbonObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            ribbonObject.transform.localScale = Vector3.one;
            UnityEngine.Object.DontDestroyOnLoad(ribbonObject);

            TrailEmitter[] inheritedEmitters = ribbonObject.GetComponents<TrailEmitter>();
            for (int i = 0; i < inheritedEmitters.Length; i++)
            {
                inheritedEmitters[i].enabled = false;
                UnityEngine.Object.Destroy(inheritedEmitters[i]);
            }

            ParticleSystem particles = ribbonObject.GetComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            particles.useAutoRandomSeed = false;
            particles.randomSeed = 0x54474252u + unchecked((uint)allRibbons.Count);

            ParticleSystem.MainModule main = particles.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startDelay = 0f;
            main.startLifetime = ParticleLifetimeSeconds;
            main.startSize = RibbonWidthSourceSizeMeters;
            main.startColor = new Color(1f, 1f, 1f, 0.78f);
            main.gravityModifier = 0f;
            main.maxParticles = MaximumParticlesPerRibbon;
            main.simulationSpace = ParticleSystemSimulationSpace.Custom;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.enabled = false;
            ParticleSystem.ShapeModule shape = particles.shape;
            shape.enabled = false;

            ParticleSystem.TrailModule trails = particles.trails;
            trails.enabled = true;
            trails.mode = ParticleSystemTrailMode.Ribbon;
            trails.ratio = 1f;
            trails.ribbonCount = 1;
            trails.worldSpace = false;
            trails.dieWithParticles = true;
            trails.inheritParticleColor = true;
            trails.attachRibbonsToTransform = false;

            ParticleSystemRenderer renderer =
                ribbonObject.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.None;
            renderer.allowOcclusionWhenDynamic = false;
            renderer.minParticleSize = 0f;
            renderer.maxParticleSize = 0.08f;

            RibbonSystem result = new RibbonSystem
            {
                GameObject = ribbonObject,
                Particles = particles
            };
            allRibbons.Add(result);
            ConfigureSimulationOrigin(result);
            ribbonObject.SetActive(false);

            if (!ValidateSingleRibbon(result))
            {
                logger.LogError(
                    "A pooled contrail renderer failed its one-ribbon configuration check.");
            }

            return result;
        }

        private static bool ValidateSingleRibbon(RibbonSystem ribbon)
        {
            ParticleSystem.TrailModule trails = ribbon.Particles.trails;
            ParticleSystemRenderer renderer =
                ribbon.GameObject.GetComponent<ParticleSystemRenderer>();
            return trails.enabled &&
                trails.mode == ParticleSystemTrailMode.Ribbon &&
                trails.ribbonCount == 1 &&
                renderer != null &&
                renderer.renderMode == ParticleSystemRenderMode.None;
        }

        private void ReleaseAircraftRibbons(AircraftState state)
        {
            for (int i = 0; i < state.Nozzles.Length; i++)
            {
                ReleaseNozzleRibbon(state.Nozzles[i]);
            }
            state.HasOwnedRibbon = false;
        }

        private void ReleaseNozzleRibbon(NozzleState nozzle)
        {
            ResetNozzleHistory(nozzle, markInactive: true);
            nozzle.ReleaseAtTime = 0f;
            RibbonSystem ribbon = nozzle.Ribbon;
            if (ribbon == null || !ribbon.InUse)
            {
                nozzle.Ribbon = null;
                return;
            }

            ribbon.Particles.Stop(
                true,
                ParticleSystemStopBehavior.StopEmittingAndClear);
            ribbon.GameObject.SetActive(false);
            ribbon.InUse = false;
            availableRibbons.Push(ribbon);
            nozzle.Ribbon = null;
        }

        private ParticleSystem FindMissileSmokeTemplate()
        {
            if (MissileTrailSystemField == null)
            {
                return null;
            }

            TrailEmitter[] loadedEmitters = Resources.FindObjectsOfTypeAll<TrailEmitter>();
            ParticleSystem best = null;
            float bestScore = float.MinValue;
            for (int i = 0; i < loadedEmitters.Length; i++)
            {
                ParticleSystem candidate =
                    MissileTrailSystemField.GetValue(loadedEmitters[i]) as ParticleSystem;
                if (candidate == null || !candidate.trails.enabled)
                {
                    continue;
                }

                ParticleSystemRenderer renderer =
                    candidate.GetComponent<ParticleSystemRenderer>();
                if (renderer == null || renderer.trailMaterial == null)
                {
                    continue;
                }

                ParticleSystem.MainModule main = candidate.main;
                float lifetime = main.startLifetime.constantMax;
                float size = main.startSize.constantMax;
                if (lifetime < 8f || lifetime > 25f || size < 8f || size > 40f)
                {
                    continue;
                }

                float score = 100f - Mathf.Abs(lifetime - 15f) * 4f -
                    Mathf.Abs(size - 20f);
                if (string.Equals(
                        candidate.gameObject.name,
                        "smokeTrail",
                        StringComparison.OrdinalIgnoreCase))
                {
                    score += 100f;
                }

                if (score > bestScore)
                {
                    best = candidate;
                    bestScore = score;
                }
            }

            return best;
        }

        private void RefreshSimulationOrigin()
        {
            if (Datum.origin == null || simulationOrigin == Datum.origin)
            {
                return;
            }

            simulationOrigin = Datum.origin;
            for (int i = 0; i < allRibbons.Count; i++)
            {
                RibbonSystem ribbon = allRibbons[i];
                ribbon.Particles.Clear(true);
                ConfigureSimulationOrigin(ribbon);
            }

            foreach (AircraftState state in aircraftStates.Values)
            {
                for (int i = 0; i < state.Nozzles.Length; i++)
                {
                    ReleaseNozzleRibbon(state.Nozzles[i]);
                }
            }
        }

        private void ConfigureSimulationOrigin(RibbonSystem ribbon)
        {
            if (ribbon == null || ribbon.Particles == null || Datum.origin == null)
            {
                return;
            }

            ParticleSystem.MainModule main = ribbon.Particles.main;
            main.customSimulationSpace = Datum.origin;
        }

        private void EmitAircraftContrails(float particleTime)
        {
            foreach (AircraftState state in aircraftStates.Values)
            {
                Aircraft aircraft = state.Aircraft;
                if (aircraft == null || aircraft.disabled)
                {
                    continue;
                }

                if (!state.HasOwnedRibbon && particleTime < state.NextDormantUpdateTime)
                {
                    continue;
                }
                if (!state.HasOwnedRibbon)
                {
                    state.NextDormantUpdateTime =
                        particleTime + DormantAircraftUpdateIntervalSeconds;
                }

                float altitude = aircraft.transform.GlobalPosition().y;
                state.AltitudeActive = state.AltitudeActive
                    ? altitude >= MinimumFormationAltitudeMeters - FormationHysteresisMeters
                    : altitude >= MinimumFormationAltitudeMeters;
                float visibility = VisibilityAtAltitude(altitude);

                bool active = state.AltitudeActive && visibility > 0f &&
                    aircraft.Ignition &&
                    aircraft.speed >= MinimumAirspeedMetersPerSecond;

                Vector3 inheritedVelocity = aircraft.rb == null
                    ? Vector3.zero
                    : aircraft.rb.velocity;
                for (int i = 0; i < state.Nozzles.Length; i++)
                {
                    NozzleState nozzle = state.Nozzles[i];
                    if (nozzle.Transform == null)
                    {
                        ReleaseNozzleRibbon(nozzle);
                        continue;
                    }

                    if (!active || !IsEmissionPointPowered(nozzle))
                    {
                        StopNozzleEmission(nozzle, particleTime);
                        continue;
                    }

                    if (nozzle.Ribbon == null)
                    {
                        nozzle.Ribbon = AcquireRibbonSystem();
                        state.HasOwnedRibbon = true;
                    }

                    if (!nozzle.WasEmitting)
                    {
                        // A Ribbon system connects all live particles. Clear any fading
                        // remnant before restarting so a new contrail cannot bridge across
                        // an engine-off or below-altitude gap.
                        nozzle.Ribbon.Particles.Clear(true);
                        nozzle.Ribbon.Particles.Play(false);
                        nozzle.WasEmitting = true;
                    }

                    nozzle.ReleaseAtTime = 0f;
                    EmitNozzle(nozzle, inheritedVelocity, visibility);
                }
                if (state.HasOwnedRibbon)
                {
                    state.HasOwnedRibbon = HasOwnedRibbon(state);
                }
            }
        }

        private static bool HasOwnedRibbon(AircraftState state)
        {
            for (int i = 0; i < state.Nozzles.Length; i++)
            {
                if (state.Nozzles[i].Ribbon != null)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsEmissionPointPowered(NozzleState nozzle)
        {
            if (nozzle.JetNozzle != null)
            {
                return nozzle.JetNozzle.GetTotalThrust() > 1f;
            }

            if (nozzle.Engine != null)
            {
                return nozzle.Engine.GetThrust() > 1f ||
                    nozzle.Engine.GetRPMRatio() > 0.15f;
            }

            return false;
        }

        private void EmitNozzle(
            NozzleState nozzle,
            Vector3 inheritedVelocity,
            float visibility)
        {
            Vector3 appearancePoint = nozzle.Transform.position -
                nozzle.Transform.forward * SmokeAppearanceOffsetMeters;
            GlobalPosition currentPosition = appearancePoint.ToGlobalPosition();
            if (!nozzle.HasLastPosition)
            {
                EmitRibbonPoint(nozzle, currentPosition, inheritedVelocity, visibility);
                nozzle.LastPosition = currentPosition;
                nozzle.HasLastPosition = true;
                return;
            }

            Vector3 step = currentPosition - nozzle.LastPosition;
            float distance = step.magnitude;
            if (distance <= 0.001f)
            {
                return;
            }

            if (distance > MaximumContinuousStepMeters)
            {
                nozzle.Ribbon.Particles.Clear(true);
                nozzle.LastPosition = currentPosition;
                nozzle.DistanceCarry = 0f;
                EmitRibbonPoint(nozzle, currentPosition, inheritedVelocity, visibility);
                return;
            }

            float distanceToNext = RibbonPointSpacingMeters - nozzle.DistanceCarry;
            int emitted = 0;
            while (distanceToNext <= distance && emitted < MaximumPointsPerNozzlePerFrame)
            {
                float t = distanceToNext / distance;
                GlobalPosition emitPosition = nozzle.LastPosition + step * t;
                EmitRibbonPoint(nozzle, emitPosition, inheritedVelocity, visibility);
                distanceToNext += RibbonPointSpacingMeters;
                emitted++;
            }

            nozzle.DistanceCarry =
                (nozzle.DistanceCarry + distance) % RibbonPointSpacingMeters;
            nozzle.LastPosition = currentPosition;
        }

        private static void EmitRibbonPoint(
            NozzleState nozzle,
            GlobalPosition position,
            Vector3 velocity,
            float visibility)
        {
            byte alpha = (byte)Mathf.Clamp(
                Mathf.RoundToInt(199f * visibility),
                0,
                199);
            ParticleSystem.EmitParams emit = new ParticleSystem.EmitParams
            {
                position = position.AsVector3(),
                velocity = velocity,
                startLifetime = ParticleLifetimeSeconds,
                startSize = RibbonWidthSourceSizeMeters,
                startColor = new Color32(255, 255, 255, alpha),
                randomSeed = nozzle.RandomSeed++
            };
            nozzle.Ribbon.Particles.Emit(emit, 1);
        }

        private static float VisibilityAtAltitude(float altitudeMeters)
        {
            float linear = Mathf.InverseLerp(
                MinimumFormationAltitudeMeters,
                FullStrengthAltitudeMeters,
                altitudeMeters);
            return Mathf.SmoothStep(0f, 1f, linear);
        }

        private static bool ValidateAltitudeGradient()
        {
            float midpoint =
                (MinimumFormationAltitudeMeters + FullStrengthAltitudeMeters) * 0.5f;
            return Mathf.Approximately(
                       VisibilityAtAltitude(MinimumFormationAltitudeMeters - 1f),
                       0f) &&
                   Mathf.Approximately(VisibilityAtAltitude(midpoint), 0.5f) &&
                   Mathf.Approximately(
                       VisibilityAtAltitude(FullStrengthAltitudeMeters),
                       1f);
        }

        private void StopNozzleEmission(NozzleState nozzle, float particleTime)
        {
            if (nozzle.Ribbon == null)
            {
                ResetNozzleHistory(nozzle, markInactive: true);
                nozzle.ReleaseAtTime = 0f;
                return;
            }

            if (nozzle.WasEmitting)
            {
                ResetNozzleHistory(nozzle, markInactive: true);
                nozzle.ReleaseAtTime = particleTime + ParticleLifetimeSeconds;
                return;
            }

            if (nozzle.ReleaseAtTime > 0f &&
                particleTime >= nozzle.ReleaseAtTime)
            {
                ReleaseNozzleRibbon(nozzle);
            }
        }

        private static void ResetNozzleHistory(NozzleState nozzle, bool markInactive)
        {
            nozzle.HasLastPosition = false;
            nozzle.DistanceCarry = 0f;
            if (markInactive)
            {
                nozzle.WasEmitting = false;
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            foreach (AircraftState state in aircraftStates.Values)
            {
                ReleaseAircraftRibbons(state);
            }

            aircraftStates.Clear();
            staleAircraft.Clear();
            availableRibbons.Clear();
            for (int i = 0; i < allRibbons.Count; i++)
            {
                if (allRibbons[i].GameObject != null)
                {
                    UnityEngine.Object.Destroy(allRibbons[i].GameObject);
                }
            }

            allRibbons.Clear();
            missileTemplate = null;
            simulationOrigin = null;
        }
    }
}

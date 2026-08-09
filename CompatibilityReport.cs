using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using UnityEngine;

namespace TheGreatBigRebalancing
{
    internal sealed class CompatibilityReport
    {
        private const string ExpectedGameVersionPrefix = "0.34";
        private const string ExpectedUnityVersion = "2022.3.62f2";
        private const BindingFlags InstanceMembers =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly string[] RequiredGameTypes =
        {
            "GameManager",
            "Aircraft",
            "AircraftDefinition",
            "Weapon"
        };

        private CompatibilityReport(
            string gameVersion,
            string unityVersion,
            string gameAssemblyName,
            IReadOnlyList<string> missingTypes,
            IReadOnlyList<string> missingMembers)
        {
            GameVersion = gameVersion;
            UnityVersion = unityVersion;
            GameAssemblyName = gameAssemblyName;
            MissingTypes = missingTypes;
            MissingMembers = missingMembers;
        }

        internal string GameVersion { get; }

        internal string UnityVersion { get; }

        internal string GameAssemblyName { get; }

        internal IReadOnlyList<string> MissingTypes { get; }

        internal IReadOnlyList<string> MissingMembers { get; }

        internal bool MatchesExpectedGameVersion =>
            GameVersion != null &&
            GameVersion.StartsWith(ExpectedGameVersionPrefix, StringComparison.OrdinalIgnoreCase);

        internal bool MatchesExpectedUnityVersion =>
            string.Equals(UnityVersion, ExpectedUnityVersion, StringComparison.OrdinalIgnoreCase);

        internal bool HasRequiredApi => MissingTypes.Count == 0 && MissingMembers.Count == 0;

        internal static CompatibilityReport Create()
        {
            // This direct reference is intentional: a successful build proves that the
            // plugin is compiling against Nuclear Option's Assembly-CSharp API.
            Assembly gameAssembly = typeof(GameManager).Assembly;
            List<string> missingTypes = new List<string>();
            List<string> missingMembers = new List<string>();

            foreach (string typeName in RequiredGameTypes)
            {
                if (gameAssembly.GetType(typeName, false) == null)
                {
                    missingTypes.Add(typeName);
                }
            }

            RequireMethod(missingMembers, typeof(Encyclopedia), "AfterLoad", Type.EmptyTypes);
            RequireField(missingMembers, typeof(AircraftSelectionMenu), "previewAircraft");
            RequireField(missingMembers, typeof(AircraftSelectionMenu), "loadoutSelector");
            RequireField(missingMembers, typeof(AircraftSelectionMenu), "aircraftSelection");
            RequireField(missingMembers, typeof(AircraftSelectionMenu), "selectionIndex");
            RequireField(missingMembers, typeof(AircraftSelectionMenu), "airbase");
            RequireMethod(missingMembers, typeof(AircraftSelectionMenu), "OnEnable", Type.EmptyTypes);
            RequireMethod(missingMembers, typeof(AircraftSelectionMenu), "OnDestroy", Type.EmptyTypes);
            RequireMethod(missingMembers, typeof(AircraftSelectionMenu), "SpawnPreview", Type.EmptyTypes);
            RequireMethod(
                missingMembers,
                typeof(CameraStateManager),
                nameof(CameraStateManager.FocusAirbase),
                new[] { typeof(Airbase), typeof(bool), typeof(float), typeof(float) });
            RequireMethod(missingMembers, typeof(CameraStateManager), "LateUpdate", Type.EmptyTypes);
            RequireMethod(
                missingMembers,
                typeof(CameraSelectionState),
                nameof(CameraSelectionState.SetPreviewAircraft),
                new[] { typeof(Aircraft) });
            RequireField(missingMembers, typeof(Hardpoint), "pylonOptions");
            RequireField(missingMembers, typeof(Hardpoint), "spawnedPrefab");
            RequireField(missingMembers, typeof(PowerSupply), "charge");
            RequireField(missingMembers, typeof(PowerSupply), "powerDrawn");
            RequireField(missingMembers, typeof(PowerSupply), "powerRequested");
            RequireField(missingMembers, typeof(PowerSupply), "chargePerRPM");
            RequireField(missingMembers, typeof(PowerSupply), "maxCharge");
            RequireMethod(missingMembers, typeof(PowerSupply), "FixedUpdate", Type.EmptyTypes);
            RequireField(missingMembers, typeof(RadarJammer), "powerUsage");
            RequireField(missingMembers, typeof(RadarJammer), "jammingIntensity");
            RequireField(missingMembers, typeof(RadarJammer), "jamIntensityCurrent");
            RequireField(missingMembers, typeof(JammingPod), "power");
            RequireField(missingMembers, typeof(JammingPod), "effectiveness");
            RequireField(missingMembers, typeof(JammingPod), "rangeFalloff");
            RequireField(missingMembers, typeof(JammingPod), "directionTransform");
            RequireMethod(missingMembers, typeof(JammingPod), "FixedUpdate", Type.EmptyTypes);
            RequireMethod(missingMembers, typeof(JammingPod), "LateUpdate", Type.EmptyTypes);
            RequireField(missingMembers, typeof(JetNozzle), "thrustTransform");
            RequireField(missingMembers, typeof(TrailEmitter), "trailSystem");
            RequireField(missingMembers, typeof(FlareEjector), "maxAmmo");
            RequireField(missingMembers, typeof(FlareEjector), "ejectionPoints");
            RequireField(missingMembers, typeof(FlareEjector), "ejectionGrouping");
            RequireField(missingMembers, typeof(FlareEjector), "ejectionVelocity");
            RequireField(missingMembers, typeof(FlareEjector), "ejectionIndex");
            RequireField(missingMembers, typeof(CountermeasureManager), "countermeasureStations");
            RequireField(missingMembers, typeof(WeaponSelector), "hardpointSet");
            RequireField(missingMembers, typeof(WeaponMount), "disabled");
            RequireField(missingMembers, typeof(UnitDefinition), "disabled");
            RequireMethod(
                missingMembers,
                typeof(Aircraft),
                Balance.AircraftRadarTargetWarning.RadarWarningMethodName,
                new[] { typeof(Unit) });
            RequireMethod(
                missingMembers,
                typeof(Aircraft),
                "UserCode_RpcToggleRadar_1325449311",
                new[] { typeof(bool) });
            Type networkManager = gameAssembly.GetType(
                "NuclearOption.Networking.NetworkManagerNuclearOption",
                false);
            if (networkManager == null)
            {
                missingTypes.Add("NuclearOption.Networking.NetworkManagerNuclearOption");
            }
            else
            {
                RequireMethod(missingMembers, networkManager, "RegisterPrefabs", Type.EmptyTypes);
            }

            return new CompatibilityReport(
                Application.version,
                Application.unityVersion,
                gameAssembly.GetName().Name,
                missingTypes,
                missingMembers);
        }

        private static void RequireField(List<string> missing, Type type, string fieldName)
        {
            if (type.GetField(fieldName, InstanceMembers) == null)
            {
                missing.Add(type.FullName + "." + fieldName);
            }
        }

        private static void RequireMethod(
            List<string> missing,
            Type type,
            string methodName,
            Type[] parameterTypes)
        {
            if (type.GetMethod(
                    methodName,
                    InstanceMembers,
                    null,
                    parameterTypes,
                    null) == null)
            {
                missing.Add(type.FullName + "." + methodName + "()");
            }
        }

        internal void Log(ManualLogSource logger)
        {
            logger.LogInfo(
                "Game API assembly: " + GameAssemblyName + ". Game version: " + GameVersion +
                ". Unity runtime: " + UnityVersion + ".");

            if (!MatchesExpectedGameVersion)
            {
                logger.LogWarning(
                    "This build targets Nuclear Option update 0.34, but the running game reports " +
                    GameVersion + ".");
            }

            if (!MatchesExpectedUnityVersion)
            {
                logger.LogWarning(
                    "This scaffold targets Nuclear Option 0.34 / Unity " + ExpectedUnityVersion +
                    ", but the running game reports Unity " + UnityVersion + ".");
            }

            if (!HasRequiredApi)
            {
                if (MissingTypes.Count > 0)
                {
                    logger.LogError(
                        "Required Nuclear Option API types are missing: " +
                        string.Join(", ", MissingTypes));
                }
                if (MissingMembers.Count > 0)
                {
                    logger.LogError(
                        "Required Nuclear Option 0.34 API members are missing: " +
                        string.Join(", ", MissingMembers));
                }
            }
        }
    }
}

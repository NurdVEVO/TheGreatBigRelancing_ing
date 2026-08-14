using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace TheGreatBigRebalancing
{
    [BepInProcess("NuclearOption.exe")]
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.nurd.basic.thegreatbigrebalancing";
        public const string PluginName = "The Great Big Rebalancing";
        public const string PluginVersion = "0.4.66";

        // Increment both values whenever a release changes network-visible data or behavior.
        internal const string MatchmakingProtocol = "tgbr-v58";
        internal const uint BuildHashSalt = 0x5447423Au;

        internal static ManualLogSource ModLogger { get; private set; }

        private Harmony harmony;
#if HPW_EDITOR
        private Tools.AircraftSelectorPylonEditor pylonEditor;
#endif
        private Visuals.HighAltitudeEngineSmoke highAltitudeEngineSmoke;

        private void Awake()
        {
            ModLogger = Logger;
            CompatibilityReport report = CompatibilityReport.Create();
            report.Log(Logger);
            if (!report.HasRequiredApi || !report.MatchesExpectedGameVersion)
            {
                Logger.LogError(
                    "The Great Big Rebalancing patches were not applied because this game build " +
                    "does not match the required Nuclear Option 0.34 API.");
                return;
            }

            harmony = new Harmony(PluginGuid);
            harmony.PatchAll(typeof(Plugin).Assembly);
            if (!GameManager.IsHeadless)
            {
#if HPW_EDITOR
                pylonEditor = new Tools.AircraftSelectorPylonEditor(Logger);
#endif
                highAltitudeEngineSmoke = new Visuals.HighAltitudeEngineSmoke(Logger);
            }
            Logger.LogInfo(
                PluginName + " " + PluginVersion + " loaded. Multiplayer compatibility: " +
                Networking.ServerSeparation.CompatibilityVersion() + ".");
        }

        private void OnDestroy()
        {
#if HPW_EDITOR
            pylonEditor?.Dispose();
            pylonEditor = null;
#endif
            highAltitudeEngineSmoke?.Dispose();
            highAltitudeEngineSmoke = null;
            harmony?.UnpatchSelf();
            harmony = null;
            ModLogger = null;
        }

        private void Update()
        {
            Balance.F99PylonExpansion.TryApplyPending();
            Balance.Fs41TargetingPodPylon.TryApplyPending();
            Balance.F16ViperII.TryApplyPending();
            Balance.TernionLoadoutRebalance.TryApplyPending();
            highAltitudeEngineSmoke?.Update();
#if HPW_EDITOR
            pylonEditor?.Update();
#endif
        }

#if HPW_EDITOR
        private void OnGUI()
        {
            pylonEditor?.OnGUI();
        }
#endif

    }
}

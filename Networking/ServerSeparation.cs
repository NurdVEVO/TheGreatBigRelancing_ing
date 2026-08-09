using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Mirage;
using Mirage.Authentication;
using NuclearOption.Networking;
using NuclearOption.Networking.Authentication;
using NuclearOption.Networking.Lobbies;
using UnityEngine;

namespace TheGreatBigRebalancing.Networking
{
    internal static class ServerSeparation
    {
        private static readonly MethodInfo ApplicationVersionGetter =
            AccessTools.PropertyGetter(typeof(Application), nameof(Application.version));

        private static readonly MethodInfo CompatibilityVersionMethod =
            AccessTools.Method(typeof(ServerSeparation), nameof(CompatibilityVersion));

        private static readonly MethodInfo EffectiveBuildHashMethod =
            AccessTools.Method(typeof(NetworkAuthenticatorNuclearOption), "GetBuildHash");

        internal static string CompatibilityVersion()
        {
            return Application.version + "-" + Plugin.MatchmakingProtocol;
        }

        internal static bool BuildHashMatches(uint remoteBuildHash)
        {
            uint localBuildHash = (uint)EffectiveBuildHashMethod.Invoke(null, null);
            return localBuildHash == remoteBuildHash;
        }

        internal static IEnumerable<CodeInstruction> ReplaceApplicationVersion(
            IEnumerable<CodeInstruction> instructions)
        {
            int replacements = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(ApplicationVersionGetter))
                {
                    replacements++;
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = CompatibilityVersionMethod;
                }

                yield return instruction;
            }

            if (replacements == 0)
            {
                Plugin.ModLogger?.LogError("Could not apply a multiplayer compatibility-version patch.");
            }
        }
    }

    [HarmonyPatch(typeof(NetworkManagerNuclearOption), nameof(NetworkManagerNuclearOption.Awake))]
    internal static class NetworkManagerAwakePatch
    {
        private static void Postfix(NetworkManagerNuclearOption __instance)
        {
            __instance.SetModdedServer(true);
        }
    }

    [HarmonyPatch(typeof(HostedLobbyInstance), nameof(HostedLobbyInstance.SetData))]
    internal static class HostedLobbySetDataPatch
    {
        private static void Prefix(string key, ref string value)
        {
            SetCompatibilityLobbyValue(key, ref value);
        }

        private static void SetCompatibilityLobbyValue(string key, ref string value)
        {
            if (string.Equals(key, "version", StringComparison.Ordinal))
            {
                value = ServerSeparation.CompatibilityVersion();
            }
            else if (string.Equals(key, "modded_server", StringComparison.Ordinal))
            {
                value = "1";
            }
        }
    }

    [HarmonyPatch(typeof(DedicatedServerKeyValues), nameof(DedicatedServerKeyValues.SetKeyValue))]
    internal static class DedicatedServerKeyValuesPatch
    {
        private static void Prefix(string key, ref string value)
        {
            if (string.Equals(key, "version", StringComparison.Ordinal))
            {
                value = ServerSeparation.CompatibilityVersion();
            }
            else if (string.Equals(key, "modded_server", StringComparison.Ordinal))
            {
                value = "1";
            }
        }
    }

    [HarmonyPatch]
    internal static class PlayerHostedLobbySearchPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.DeclaredMethod(
                typeof(SteamLobby),
                "RequestPlayerHostedLobbies",
                new Type[] { typeof(LobbySearchFilter) });
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            return ServerSeparation.ReplaceApplicationVersion(instructions);
        }
    }

    [HarmonyPatch]
    internal static class DedicatedServerSearchPatch
    {
        private static MethodBase TargetMethod()
        {
            Type serverListRequest = typeof(SteamLobby).GetNestedType(
                "ServerListRequest",
                BindingFlags.NonPublic);
            return AccessTools.DeclaredMethod(
                serverListRequest,
                "BuildServerRequestFilters",
                new Type[] { typeof(LobbySearchFilter) });
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            return ServerSeparation.ReplaceApplicationVersion(instructions);
        }
    }

    [HarmonyPatch(typeof(SteamLobby), nameof(SteamLobby.TryJoinLobby))]
    internal static class LobbyJoinCompatibilityPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            return ServerSeparation.ReplaceApplicationVersion(instructions);
        }
    }

    [HarmonyPatch(typeof(NetworkAuthenticatorNuclearOption), "GetBuildHash")]
    internal static class NetworkBuildHashPatch
    {
        private static void Postfix(ref uint __result)
        {
            __result ^= Plugin.BuildHashSalt;
        }
    }

    [HarmonyPatch(typeof(NetworkAuthenticatorNuclearOption), "ValidateJoinAs")]
    internal static class NetworkProtocolEnforcementPatch
    {
        private static bool Prefix(
            NetworkAuthenticatorNuclearOption __instance,
            INetworkPlayer player,
            NetworkAuthenticatorNuclearOption.AuthMessage message,
            ref AuthenticationResult? __result)
        {
            if (ServerSeparation.BuildHashMatches(message.BuildHash))
            {
                return true;
            }

            Plugin.ModLogger?.LogWarning(
                "Rejected a client with an incompatible vanilla or modded network protocol: " + player + ".");
            __result = AuthenticationResult.CreateFail(
                "The Great Big Rebalancing version does not match the server.",
                __instance);
            return false;
        }
    }
}

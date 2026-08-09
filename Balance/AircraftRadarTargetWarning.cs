using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace TheGreatBigRebalancing.Balance
{
    internal static class AircraftRadarTargetWarning
    {
        internal const string RadarWarningMethodName =
            "UserCode_RpcGetRadarWarning_-1586111906";

        private static readonly MethodInfo CheckIsTargetMethod =
            AccessTools.Method(typeof(Unit), nameof(Unit.CheckIsTarget), new[] { typeof(Unit) });

        internal static IEnumerable<CodeInstruction> RestorePre034TargetState(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> code = new List<CodeInstruction>(instructions);
            int guardIndex = -1;
            int checkIndex = -1;
            int storeIndex = -1;

            for (int i = 1; i < code.Count; i++)
            {
                if (code[i].opcode != OpCodes.Isinst || code[i].operand as Type != typeof(Aircraft))
                {
                    continue;
                }

                for (int j = i + 1; j < code.Count && j <= i + 12; j++)
                {
                    if (code[j].Calls(CheckIsTargetMethod))
                    {
                        guardIndex = i - 1;
                        checkIndex = j;
                        break;
                    }
                }

                if (checkIndex >= 0)
                {
                    break;
                }
            }

            if (guardIndex < 0 || checkIndex < guardIndex + 3)
            {
                Plugin.ModLogger?.LogError(
                    "Could not restore aircraft radar target warnings: the update 0.34 " +
                    "aircraft-emitter guard was not found.");
                return code;
            }

            for (int i = checkIndex + 1; i < code.Count && i <= checkIndex + 8; i++)
            {
                if (IsStoreLocal(code[i].opcode))
                {
                    storeIndex = i;
                    break;
                }
            }

            if (storeIndex < 0)
            {
                Plugin.ModLogger?.LogError(
                    "Could not restore aircraft radar target warnings: the target-state local " +
                    "store was not found.");
                return code;
            }

            // 0.34 calculates:
            //   emitter is not Aircraft && detected && emitter.CheckIsTarget(receiver)
            // Preserve the argument loads and CheckIsTarget call while neutralizing both new
            // conditions and their false-result branches. This restores the 0.33 calculation:
            //   emitter.CheckIsTarget(receiver)
            for (int i = guardIndex; i < checkIndex - 2; i++)
            {
                MakeNop(code[i]);
            }

            for (int i = checkIndex + 1; i < storeIndex; i++)
            {
                MakeNop(code[i]);
            }

            Plugin.ModLogger?.LogInfo(
                "Pre-0.34 aircraft radar target warnings restored: active aircraft target " +
                "selection now sets OnRadarWarning.isTarget.");
            return code;
        }

        private static bool IsStoreLocal(OpCode opcode)
        {
            return opcode == OpCodes.Stloc ||
                   opcode == OpCodes.Stloc_S ||
                   opcode == OpCodes.Stloc_0 ||
                   opcode == OpCodes.Stloc_1 ||
                   opcode == OpCodes.Stloc_2 ||
                   opcode == OpCodes.Stloc_3;
        }

        private static void MakeNop(CodeInstruction instruction)
        {
            instruction.opcode = OpCodes.Nop;
            instruction.operand = null;
        }
    }

    [HarmonyPatch]
    internal static class AircraftRadarTargetWarningPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.DeclaredMethod(
                typeof(Aircraft),
                AircraftRadarTargetWarning.RadarWarningMethodName,
                new[] { typeof(Unit) });
        }

        private static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            return AircraftRadarTargetWarning.RestorePre034TargetState(instructions);
        }
    }
}

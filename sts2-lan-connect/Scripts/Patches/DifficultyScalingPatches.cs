using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Logging;

namespace Sts2LanConnect.Scripts;

internal static class DifficultyScalingPatches
{
    internal static Type ResolveScalingModelType(Assembly gameAssembly) =>
        gameAssembly.GetType("MegaCrit.Sts2.Core.Models.MultiplayerScalingModel")
        ?? gameAssembly.GetType("MegaCrit.Sts2.Core.Models.Singleton.MultiplayerScalingModel")
        ?? throw new TypeLoadException("Cannot resolve the game's multiplayer scaling model.");

    public static void Apply(Harmony harmony)
    {
        Type scalingModelType = ResolveScalingModelType(typeof(Creature).Assembly);
        MethodInfo? scaleHp = AccessTools.Method(typeof(Creature), nameof(Creature.ScaleMonsterHpForMultiplayer));
        if (scaleHp != null)
        {
            harmony.Patch(scaleHp, prefix: new HarmonyMethod(typeof(DifficultyScalingPatches), nameof(ScaleMonsterHpPrefix)));
        }

        MethodInfo? modifyBlock = AccessTools.Method(scalingModelType, "ModifyBlockMultiplicative");
        if (modifyBlock != null)
        {
            harmony.Patch(modifyBlock, transpiler: new HarmonyMethod(typeof(DifficultyScalingPatches), nameof(PatchPlayersCountTranspiler)));
        }

        MethodInfo? modifyPower = AccessTools.Method(scalingModelType, "ModifyPowerAmountGiven");
        if (modifyPower != null)
        {
            harmony.Patch(modifyPower, transpiler: new HarmonyMethod(typeof(DifficultyScalingPatches), nameof(PatchPlayersCountTranspiler)));
        }

        Log.Info("sts2_lan_connect gameplay: difficulty scaling patches applied.");
    }

    internal static int GetEffectivePlayerCount(int rawCount)
    {
        return LanConnectConfig.DifficultyScalingEnabled ? rawCount : Math.Min(rawCount, 4);
    }

    // ReSharper disable UnusedMember.Local

    private static void ScaleMonsterHpPrefix(ref int playerCount)
    {
        playerCount = GetEffectivePlayerCount(playerCount);
    }

    internal static IEnumerable<CodeInstruction> PatchPlayersCountTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        MethodInfo helper = AccessTools.Method(typeof(DifficultyScalingPatches), nameof(GetEffectivePlayerCount));
        bool foundPlayersLoad = false;
        int patched = 0;

        foreach (CodeInstruction instruction in instructions)
        {
            yield return instruction;

            if (instruction.opcode == OpCodes.Nop)
            {
                continue;
            }

            // 0.113 moves _runState into RunModel and accesses it through a getter.
            // Match Players.Count itself, not the old model's private field load.
            bool isCall = instruction.opcode == OpCodes.Callvirt || instruction.opcode == OpCodes.Call;
            if (foundPlayersLoad && isCall && instruction.operand is MethodInfo count
                && count.Name == "get_Count" && count.ReturnType == typeof(int))
            {
                yield return new CodeInstruction(OpCodes.Call, helper);
                patched++;
            }

            foundPlayersLoad = isCall && instruction.operand is MethodInfo players
                && players.Name == "get_Players";
        }

        if (patched == 0)
        {
            throw new InvalidOperationException("Multiplayer scaling no longer reads Players.Count; difficulty patch was not applied.");
        }
    }

    // ReSharper restore UnusedMember.Local
}

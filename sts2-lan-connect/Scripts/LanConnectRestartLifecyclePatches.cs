using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Game;

namespace Sts2LanConnect.Scripts;

internal static class LanConnectRestartLifecyclePatches
{
    private const string IndicatorTypeName =
        "MegaCrit.Sts2.Core.Nodes.Multiplayer.NMultiplayerNetworkProblemIndicator";

    internal static void Apply(Harmony harmony)
    {
        Type indicator = typeof(INetGameService).Assembly.GetType(IndicatorTypeName)
            ?? throw new TypeLoadException(IndicatorTypeName);
        MethodInfo target = ResolveUpdateLoopTarget(indicator);
        harmony.Patch(target, transpiler: new HarmonyMethod(
            typeof(LanConnectRestartLifecyclePatches), nameof(GuardConnectedRead)));
        Log.Info("sts2_lan_connect restart lifecycle: network indicator null-service guard applied.");
    }

    internal static MethodInfo ResolveUpdateLoopTarget(Type indicator)
    {
        MethodInfo loop = AccessTools.DeclaredMethod(indicator, "UpdateLoop")
            ?? throw new MissingMethodException(indicator.FullName, "UpdateLoop");
        Type stateMachine = loop.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType
            ?? throw new InvalidOperationException("Network indicator UpdateLoop has no async state machine.");
        return AccessTools.DeclaredMethod(stateMachine, "MoveNext")
            ?? throw new MissingMethodException(stateMachine.FullName, "MoveNext");
    }

    internal static IEnumerable<CodeInstruction> GuardConnectedRead(IEnumerable<CodeInstruction> instructions)
    {
        MethodInfo getter = AccessTools.PropertyGetter(typeof(INetGameService), nameof(INetGameService.IsConnected));
        MethodInfo guard = AccessTools.Method(typeof(LanConnectRestartLifecyclePatches), nameof(IsConnectedOrFalse));
        List<CodeInstruction> body = instructions.Select(instruction => new CodeInstruction(instruction)).ToList();
        CodeInstruction[] reads = body.Where(instruction => instruction.Calls(getter)).ToArray();
        if (reads.Length != 1)
        {
            throw new InvalidOperationException(
                $"Expected one network indicator connection check, found {reads.Length}; no patch applied.");
        }

        // Patch MoveNext, not the async kickoff: this check also runs after Task.Delay.
        // Keep labels/exception blocks and all original non-null behavior intact.
        reads[0].opcode = OpCodes.Call;
        reads[0].operand = guard;
        return body;
    }

    // RunManager.NetService is deliberately detached for replacement lobby binding.
    // An old indicator can resume later, even after its scene has been removed.
    internal static bool IsConnectedOrFalse(INetGameService? service) => service?.IsConnected ?? false;
}

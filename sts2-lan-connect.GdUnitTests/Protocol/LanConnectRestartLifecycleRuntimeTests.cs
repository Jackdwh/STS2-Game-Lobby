using System.Reflection;
using System.Reflection.Emit;
using GdUnit4;
using HarmonyLib;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Runs;
using Sts2LanConnect.Scripts;
using static GdUnit4.Assertions;

namespace Sts2LanConnect.GdUnitTests.Protocol;

[TestSuite]
[RequireGodotRuntime]
public sealed class LanConnectRestartLifecycleRuntimeTests
{
    [TestCase]
    public void Actual_game_loop_faults_without_service_and_exits_after_guard_is_installed()
    {
        FieldInfo serviceField = AccessTools.Field(typeof(RunManager), "<NetService>k__BackingField");
        object? previous = serviceField.GetValue(RunManager.Instance);
        var indicator = new NMultiplayerNetworkProblemIndicator();
        MethodInfo loop = AccessTools.DeclaredMethod(typeof(NMultiplayerNetworkProblemIndicator), "UpdateLoop");
        Harmony harmony = new("sts2_lan_connect.tests.restart_lifecycle");
        try
        {
            serviceField.SetValue(RunManager.Instance, null);
            Task before = (Task)loop.Invoke(indicator, null)!;
            AssertThat(before.IsFaulted).IsTrue();
            AssertThat(before.Exception?.InnerException is NullReferenceException).IsTrue();

            LanConnectRestartLifecyclePatches.Apply(harmony);
            Task after = (Task)loop.Invoke(indicator, null)!;
            AssertThat(after.IsCompletedSuccessfully).IsTrue();
            // The guard must not reattach the stale service or rewrite the run/session.
            AssertThat(serviceField.GetValue(RunManager.Instance)).IsNull();
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            serviceField.SetValue(RunManager.Instance, previous);
            indicator.Free();
        }
    }

    [TestCase]
    public void Guard_preserves_non_null_connection_states()
    {
        AssertThat(LanConnectRestartLifecyclePatches.IsConnectedOrFalse(null)).IsFalse();
        AssertThat(LanConnectRestartLifecyclePatches.IsConnectedOrFalse(new NetSingleplayerGameService())).IsTrue();
        AssertThat(LanConnectRestartLifecyclePatches.IsConnectedOrFalse(new NetClientGameService(default))).IsFalse();
    }

    [TestCase]
    public void Guard_changes_only_the_real_async_connection_check()
    {
        MethodInfo target = LanConnectRestartLifecyclePatches.ResolveUpdateLoopTarget(typeof(NMultiplayerNetworkProblemIndicator));
        var original = PatchProcessor.GetOriginalInstructions(target).ToArray();
        var patched = LanConnectRestartLifecyclePatches.GuardConnectedRead(original).ToArray();
        MethodInfo getter = AccessTools.PropertyGetter(typeof(INetGameService), "IsConnected");
        MethodInfo guard = AccessTools.Method(typeof(LanConnectRestartLifecyclePatches), "IsConnectedOrFalse");
        AssertThat(patched.Length).IsEqual(original.Length);
        AssertThat(original.Count(i => i.Calls(getter))).IsEqual(1);
        AssertThat(patched.Count(i => i.Calls(getter))).IsEqual(0);
        AssertThat(patched.Count(i => i.Calls(guard))).IsEqual(1);
        for (int i = 0; i < original.Length; i++)
        {
            AssertThat(patched[i].labels.SequenceEqual(original[i].labels)).IsTrue();
            AssertThat(patched[i].blocks.SequenceEqual(original[i].blocks)).IsTrue();
            if (!original[i].Calls(getter))
            {
                AssertThat(patched[i].opcode == original[i].opcode).IsTrue();
                AssertThat(Equals(patched[i].operand, original[i].operand)).IsTrue();
            }
        }
    }

    [TestCase]
    public void Unexpected_loop_shape_is_rejected_instead_of_silently_claiming_success()
    {
        Exception? caught = null;
        try { _ = LanConnectRestartLifecyclePatches.GuardConnectedRead([new CodeInstruction(OpCodes.Ret)]).ToArray(); }
        catch (InvalidOperationException exception) { caught = exception; }
        AssertThat(caught).IsNotNull();
    }
}

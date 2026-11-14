using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Patches;

public sealed class LanConnectBeta113CompatibilityTests
{
    [Fact]
    public void Clean_build_keeps_restart_guard_but_contains_no_temporary_trace_types()
    {
        Assembly assembly = typeof(DifficultyScalingPatches).Assembly;
        Assert.NotNull(assembly.GetType("Sts2LanConnect.Scripts.LanConnectRestartLifecyclePatches"));
        Assert.Null(assembly.GetType("Sts2LanConnect.Scripts.LanConnectRestartDiagnostics"));
        Assert.Null(assembly.GetType("Sts2LanConnect.Scripts.LanConnectRestartTraceWriter"));
    }

    [Theory]
    [InlineData("MegaCrit.Sts2.Core.Models.MultiplayerScalingModel")]
    [InlineData("MegaCrit.Sts2.Core.Models.Singleton.MultiplayerScalingModel")]
    public void Scaling_model_resolves_both_namespaces(string name)
    {
        AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName(Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.Run);
        Type type = assembly.DefineDynamicModule("game").DefineType(name).CreateType()!;
        Assert.Equal(type, DifficultyScalingPatches.ResolveScalingModelType(assembly));
    }

    [Fact]
    public void Scaling_model_missing_is_not_silently_accepted() =>
        Assert.Throws<TypeLoadException>(() => DifficultyScalingPatches.ResolveScalingModelType(typeof(string).Assembly));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Scaling_matches_players_count_after_field_or_property(bool useProperty)
    {
        var load = useProperty
            ? new CodeInstruction(OpCodes.Call, typeof(Model).GetProperty(nameof(Model.RunState))!.GetMethod!)
            : new CodeInstruction(OpCodes.Ldfld, typeof(Model).GetField(nameof(Model.State))!);
        MethodInfo players = typeof(State).GetProperty(nameof(State.Players))!.GetMethod!;
        MethodInfo count = typeof(List<int>).GetProperty(nameof(List<int>.Count))!.GetMethod!;
        CodeInstruction[] body = [load, new(OpCodes.Callvirt, players), new(OpCodes.Callvirt, count), new(OpCodes.Ret)];
        CodeInstruction[] patched = DifficultyScalingPatches.PatchPlayersCountTranspiler(body).ToArray();
        Assert.Equal(5, patched.Length);
        Assert.Equal(nameof(DifficultyScalingPatches.GetEffectivePlayerCount), ((MethodInfo)patched[3].operand).Name);
    }

    [Fact]
    public void Scaling_rejects_unrelated_count() =>
        Assert.Throws<InvalidOperationException>(() => DifficultyScalingPatches.PatchPlayersCountTranspiler(
            [new CodeInstruction(OpCodes.Callvirt, typeof(List<int>).GetProperty("Count")!.GetMethod!)]).ToArray());

    [Fact]
    public void Join_ascensions_preserve_distinct_standard_and_blitz_progress()
    {
        var result = LanConnectLobbyHandshakeCompatibility.PopulateJoinAscensions(new NewJoin(), new Progress());
        Assert.Equal(12, result.maxStandardAscension);
        Assert.Equal(7, result.maxBlitzAscension);
        var legacy = LanConnectLobbyHandshakeCompatibility.PopulateJoinAscensions(new OldJoin(), new Progress());
        Assert.Equal(12, legacy.maxAscensionUnlocked);
    }

    [Fact]
    public void Incomplete_ascension_schema_fails_closed() =>
        Assert.Throws<MissingFieldException>(() =>
            LanConnectLobbyHandshakeCompatibility.PopulateJoinAscensions(new PartialJoin(), new Progress()));

#pragma warning disable CS0649
    private sealed class Model { public State State = new(); public State RunState => State; }
    private sealed class State { public List<int> Players { get; } = []; }
    private sealed class Progress { public int MaxMultiplayerAscension => 12; public int MaxBlitzAscension => 7; }
    private struct OldJoin { public int maxAscensionUnlocked; }
    private struct NewJoin { public int maxStandardAscension; public int maxBlitzAscension; }
    private struct PartialJoin { public int maxStandardAscension; }
#pragma warning restore CS0649
}

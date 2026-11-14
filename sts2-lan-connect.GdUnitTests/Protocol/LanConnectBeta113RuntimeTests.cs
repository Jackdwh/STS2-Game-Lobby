using System.Reflection;
using GdUnit4;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using Sts2LanConnect.Scripts;
using static GdUnit4.Assertions;

namespace Sts2LanConnect.GdUnitTests.Protocol;

[TestSuite]
[RequireGodotRuntime]
public sealed class LanConnectBeta113RuntimeTests
{
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void Host_mode_buttons_resolve_in_legacy_nested_and_unique_name_scenes(int layout)
    {
        var root = new Control();
        try
        {
            Node parent = root;
            if (layout != 0)
            {
                parent = new Control { Name = "ButtonContainer" };
                root.AddChild(parent);
            }
            foreach (string name in new[] { "StandardButton", "DailyButton", "CustomRunButton" })
            {
                var button = new MegaCrit.Sts2.Core.Nodes.Screens.MainMenu.NSubmenuButton { Name = name };
                parent.AddChild(button);
                if (layout == 2)
                {
                    button.Owner = root;
                    button.UniqueNameInOwner = true;
                }
                AccessTools.Field(typeof(MegaCrit.Sts2.Core.Nodes.GodotExtensions.NClickableControl), "_isEnabled")
                    .SetValue(button, name != "DailyButton");
                AssertThat(HostSubmenuPatches.FindNativeModeButton(root, name)).IsEqual(button);
            }
            var availability = HostSubmenuPatches.GetModeAvailability(root);
            AssertThat(availability.Standard).IsTrue();
            AssertThat(availability.Daily).IsFalse();
            AssertThat(availability.Custom).IsTrue();
        }
        finally { root.Free(); }
    }

    [TestCase]
    public void Missing_host_buttons_do_not_log_node_errors_or_unlock_modes()
    {
        var root = new Control();
        try
        {
            AssertThat(HostSubmenuPatches.FindNativeModeButton(root, "StandardButton")).IsNull();
            var availability = HostSubmenuPatches.GetModeAvailability(root);
            AssertThat(availability.Standard).IsFalse();
            AssertThat(availability.Daily).IsFalse();
            AssertThat(availability.Custom).IsFalse();
        }
        finally { root.Free(); }
    }

    [TestCase]
    public void Runtime_patch_targets_and_scaling_IL_resolve()
    {
        Assembly game = typeof(PacketWriter).Assembly;
        AssertThat(LanConnectTailRuntimeSupport.Probe(game).Available).IsTrue();
        AssertThat(LanConnectTailMessagePatches.ResolvePatchPlan(game)).IsNotNull();
        AssertThat(LanConnectLobbyCapacityPatches.ResolveLobbyConstructor(typeof(StartRunLobby))).IsNotNull();
        Type scaling = DifficultyScalingPatches.ResolveScalingModelType(game);
        MethodInfo block = AccessTools.Method(scaling, "ModifyBlockMultiplicative");
        var original = PatchProcessor.GetOriginalInstructions(block).ToArray();
        var patched = DifficultyScalingPatches.PatchPlayersCountTranspiler(original).ToArray();
        AssertThat(patched.Length > original.Length).IsTrue();
        AssertThat(typeof(Creature).GetMethod("ScaleMonsterHpForMultiplayer")!.GetParameters()
            .Any(p => p.Name == "playerCount" && p.ParameterType == typeof(int))).IsTrue();
    }

    [TestCase]
    public void Gameplay_and_continue_run_patch_groups_install_on_the_actual_game()
    {
        Harmony harmony = new("sts2_lan_connect.tests.beta113.gameplay");
        try
        {
            DifficultyScalingPatches.Apply(harmony);
            TreasurePatches.Apply(harmony);
            RestSitePatches.Apply(harmony);
            MerchantPatches.Apply(harmony);
            LanConnectLobbyCapacityPatches.Apply(harmony);
            LanConnectLobbyCapacityPatches.ApplyHostProtocolGuards(harmony);
            LanConnectOfficialContinueRunPatches.Apply(harmony);
            LanConnectSaveManagerPatches.Apply(harmony);

            MethodBase[] required =
            [
                AccessTools.Method(DifficultyScalingPatches.ResolveScalingModelType(typeof(Creature).Assembly), "ModifyBlockMultiplicative"),
                AccessTools.Method(typeof(Creature), "ScaleMonsterHpForMultiplayer"),
                AccessTools.Method(typeof(NTreasureRoomRelicCollection), "InitializeRelics"),
                LanConnectLobbyCapacityPatches.ResolveLobbyConstructor(typeof(StartRunLobby))!,
                AccessTools.Method(typeof(StartRunLobby), "HandleClientLobbyJoinRequestMessage"),
                AccessTools.Method(typeof(MegaCrit.Sts2.Core.Multiplayer.NetHostGameService), "StartENetHost"),
                AccessTools.Method(typeof(MegaCrit.Sts2.Core.Nodes.Screens.MainMenu.NMultiplayerSubmenu), "StartHost")
            ];
            foreach (MethodBase method in required)
            {
                AssertThat(Harmony.GetPatchInfo(method)?.Owners.Contains(harmony.Id) == true).IsTrue();
            }
        }
        finally { harmony.UnpatchAll(harmony.Id); }
    }

    [TestCase]
    public void Join_response_body_matches_native_serialization_including_mode_and_custom()
    {
        object boxed = new ClientLobbyJoinResponseMessage
        {
            playersInLobby = [], dailyTime = null, seed = "beta-113", ascension = 13, modifiers = []
        };
        typeof(ClientLobbyJoinResponseMessage).GetField("gameMode")?.SetValue(boxed, GameMode.Daily);
        typeof(ClientLobbyJoinResponseMessage).GetField("isCustom")?.SetValue(boxed, true);
        var message = (ClientLobbyJoinResponseMessage)boxed;
        PacketWriter native = new();
        message.Serialize(native);
        PacketWriter mirrored = new();
        LanConnectSerializationPatches.SerializeJoinResponseBody(mirrored, message, 3);
        AssertThat(mirrored.BitPosition).IsEqual(native.BitPosition);
        AssertThat(mirrored.Buffer.AsSpan(0, mirrored.BytePosition).ToArray())
            .IsEqual(native.Buffer.AsSpan(0, native.BytePosition).ToArray());

        PacketWriter compat = new();
        LanConnectSerializationPatches.SerializeJoinResponseBody(compat, message, 5);
        PacketReader reader = new();
        reader.Reset(compat.Buffer.AsSpan(0, compat.BytePosition).ToArray());
        AssertThat(reader.ReadInt(5)).IsEqual(0);
        AssertThat(reader.ReadBool()).IsFalse();
        AssertThat(reader.ReadBool()).IsTrue();
        AssertThat(reader.ReadString()).IsEqual("beta-113");
        if (LanConnectSerializationPatches.ValidateJoinModeFields(typeof(ClientLobbyJoinResponseMessage)))
        {
            AssertThat(reader.ReadEnum<GameMode>()).IsEqual(GameMode.Daily);
            AssertThat(reader.ReadBool()).IsTrue();
        }
        AssertThat(reader.ReadInt(5)).IsEqual(13);
        AssertThat(reader.ReadList<MegaCrit.Sts2.Core.Saves.Runs.SerializableModifier>().Count).IsEqual(0);
        AssertThat(reader.BitPosition).IsEqual(compat.BitPosition);
    }

    [TestCase]
    public void Join_mode_comes_from_response_and_custom_flag_is_preserved()
    {
        var initial = new LegacyInitial { gameMode = GameMode.Standard };
        var response = new NewResponse { gameMode = GameMode.Daily, isCustom = true };
        GameMode mode = LanConnectGameModeCompatibility.ResolveJoinedMode(initial, response, false);
        AssertThat(mode).IsEqual(GameMode.Daily);
        AssertThat(LanConnectGameModeCompatibility.ResolveJoinedCustom(response, false, mode)).IsTrue();
        AssertThat(LanConnectGameModeCompatibility.ResolveJoinedMode(initial, new object(), false))
            .IsEqual(GameMode.Standard);
        var saved = new SavedResponse { serializableRun = new SavedMode { GameMode = GameMode.Standard, IsCustomGame = true } };
        AssertThat(LanConnectGameModeCompatibility.ResolveJoinedCustom(saved, true, GameMode.Standard)).IsTrue();
        AssertThat(LanConnectGameModeCompatibility.IsCustomSelection(LanConnectGameModeCompatibility.CustomSelection)).IsTrue();
        AssertThat(LanConnectGameModeCompatibility.IsCustomSelection(GameMode.Standard)).IsFalse();
    }

    [TestCase]
    public void Explicit_custom_save_without_modifiers_remains_custom()
    {
        // Set through reflection so the suite still builds against pre-0.113 assemblies.
        var run = new SerializableRun();
        PropertyInfo? custom = typeof(SerializableRun).GetProperty("IsCustomGame");
        if (custom != null)
        {
            custom.SetValue(run, true);
            typeof(SerializableRun).GetProperty("GameMode")!.SetValue(run, GameMode.Standard);
            AssertThat(LanConnectMultiplayerSaveRoomBinding.GetLobbyGameMode(run)).IsEqual("custom");
            custom.SetValue(run, false);
            AssertThat(LanConnectMultiplayerSaveRoomBinding.GetLobbyGameMode(run)).IsEqual("standard");
        }
    }

    [TestCase]
    public void Treasure_layout_table_extends_to_sixteen_without_copying_signals()
    {
        var root = new Control();
        try
        {
            var first = new Control();
            first.AddChild(new NTreasureRoomRelicHolder());
            root.AddChild(first);
            List<Control> layouts = [first];
            TreasurePatches.EnsureHolderLayouts(layouts, 16);
            AssertThat(layouts.Count).IsEqual(16);
            for (int i = 0; i < layouts.Count; i++)
            {
                AssertThat(layouts[i].GetChildren().OfType<NTreasureRoomRelicHolder>().Count()).IsEqual(i + 1);
            }
            TreasurePatches.EnsureHolderLayouts(layouts, 8);
            AssertThat(layouts.Count).IsEqual(16);
        }
        finally { root.Free(); }
    }

    private sealed class LegacyInitial { public GameMode gameMode; }
    private sealed class NewResponse { public GameMode gameMode; public bool isCustom; }
    private sealed class SavedResponse { public SavedMode serializableRun = new(); }
    private sealed class SavedMode { public GameMode GameMode { get; set; } public bool IsCustomGame { get; set; } }
}

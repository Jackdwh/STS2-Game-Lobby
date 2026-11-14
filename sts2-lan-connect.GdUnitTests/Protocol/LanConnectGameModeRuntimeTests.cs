using System.Reflection;
using GdUnit4;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using Sts2LanConnect.Scripts;
using static GdUnit4.Assertions;

namespace Sts2LanConnect.GdUnitTests.Protocol;

[TestSuite]
[RequireGodotRuntime]
public sealed class LanConnectGameModeRuntimeTests
{
    [TestCase("Blitz", 3)]
    [TestCase("Dread", 4)]
    public void Additional_host_modes_resolve_native_values_and_host_client_screens(string name, int selectionId)
    {
        GameMode mode = LanConnectGameModeCompatibility.ToGameMode((LanConnectLanHostMode)selectionId);
        AssertThat(mode.ToString()).IsEqual(name);
        Type type = LanConnectGameModeCompatibility.ResolveAdditionalStartScreen(mode);
        AssertThat(type.Name).IsEqual($"N{name}Screen");
        AssertThat(type.GetMethod("InitializeMultiplayerAsHost", [typeof(INetGameService), typeof(int)])).IsNotNull();
        AssertThat(type.GetMethod("InitializeMultiplayerAsClient")).IsNotNull();
    }

    [TestCase]
    public void Native_buttons_control_additional_mode_availability_without_unlocking_progress()
    {
        var root = new Control();
        try
        {
            AssertThat(HostSubmenuPatches.GetModeAvailability(root).Blitz).IsFalse();
            AssertThat(HostSubmenuPatches.GetModeAvailability(root).Dread).IsFalse();
            var blitz = new NSubmenuButton { Name = "BlitzButton" };
            var dread = new NSubmenuButton { Name = "DreadButton" };
            root.AddChild(blitz);
            root.AddChild(dread);
            FieldInfo enabled = AccessTools.Field(typeof(NClickableControl), "_isEnabled");
            enabled.SetValue(blitz, true);
            enabled.SetValue(dread, false);
            var availability = HostSubmenuPatches.GetModeAvailability(root);
            AssertThat(availability.Blitz).IsTrue();
            AssertThat(availability.Dread).IsFalse();
            enabled.SetValue(dread, true);
            AssertThat(HostSubmenuPatches.GetModeAvailability(root).Dread).IsTrue();
        }
        finally { root.Free(); }
    }

    [TestCase("Standard", true, "custom")]
    [TestCase("Standard", false, "standard")]
    [TestCase("Blitz", false, "blitz")]
    [TestCase("Dread", false, "dread")]
    [TestCase("Blitz", true, "custom")]
    [TestCase("Daily", false, "daily")]
    public void Saved_mode_does_not_depend_on_having_modifiers(string mode, bool custom, string expected)
    {
        var run = new SerializableRun { GameMode = Enum.Parse<GameMode>(mode), IsCustomGame = custom, Modifiers = [] };
        AssertThat(LanConnectMultiplayerSaveRoomBinding.GetLobbyGameMode(run)).IsEqual(expected);
    }

    [TestCase("blitz", "极速")]
    [TestCase("dread", "恐惧")]
    public void Room_card_labels_do_not_fall_back_to_standard(string mode, string expected)
    {
        MethodInfo method = typeof(LanConnectLobbyOverlay).GetMethod("GetRoomGameModePill", BindingFlags.NonPublic | BindingFlags.Static)!;
        var pill = ((string Text, Color Border, Color Background))method.Invoke(null, [mode])!;
        AssertThat(pill.Text).IsEqual(expected);
    }

    [TestCase]
    public void Progress_adapter_targets_the_actual_result_contract()
    {
        MethodInfo method = typeof(SaveManager).GetMethod("UpdateProgressWithRunData", [typeof(SerializableRun), typeof(bool)])!;
        AssertThat(method.ReturnType.GetField("success")!.FieldType).IsEqual(typeof(bool));
    }
}

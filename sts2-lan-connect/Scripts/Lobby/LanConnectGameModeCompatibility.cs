using System.Reflection;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;

namespace Sts2LanConnect.Scripts;

internal static class LanConnectGameModeCompatibility
{
    // UI-only selection: 0.113 removed GameMode.Custom and uses Standard + isCustom.
    // Never serialize this sentinel; the custom screen creates the actual game lobby.
    internal static GameMode CustomSelection => Enum.TryParse("Custom", out GameMode custom)
        ? custom
        : (GameMode)(-1);

    internal static bool IsCustomSelection(GameMode mode) => mode == CustomSelection;

    internal static object? ReadMember(object value, string name)
    {
        Type type = value.GetType();
        return type.GetField(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(value)
            ?? type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(value);
    }

    internal static GameMode ResolveJoinedMode(object initialMessage, object response, bool savedRun)
    {
        object source = savedRun
            ? ReadMember(response, "serializableRun") ?? throw new MissingFieldException("Join response has no serializableRun.")
            : response;
        object? mode = ReadMember(source, savedRun ? "GameMode" : "gameMode")
            ?? ReadMember(initialMessage, "gameMode");
        return mode is GameMode result
            ? result
            : throw new MissingFieldException("Join response has no game mode.");
    }

    internal static bool ResolveJoinedCustom(object response, bool savedRun, GameMode mode)
    {
        object source = savedRun
            ? ReadMember(response, "serializableRun") ?? throw new MissingFieldException("Join response has no serializableRun.")
            : response;
        return ReadMember(source, savedRun ? "IsCustomGame" : "isCustom") is bool custom
            ? custom
            : IsCustomSelection(mode);
    }

    internal static JoinResult SetCustom(JoinResult result, bool custom)
    {
        object boxed = result;
        typeof(JoinResult).GetField("isCustom")?.SetValue(boxed, custom);
        return (JoinResult)boxed;
    }

    internal static bool IsCustom(JoinResult result) =>
        ReadMember(result, "isCustom") is bool custom ? custom : IsCustomSelection(result.gameMode);
}

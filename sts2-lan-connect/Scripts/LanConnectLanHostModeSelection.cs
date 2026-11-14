using System.Collections.Generic;

namespace Sts2LanConnect.Scripts;

internal enum LanConnectLanHostMode
{
    Standard,
    Daily,
    Custom,
    Blitz,
    Dread
}

internal readonly record struct LanConnectLanHostModeAvailability(
    bool Standard,
    bool Daily,
    bool Custom,
    bool Blitz = false,
    bool Dread = false);

internal readonly record struct LanConnectLanHostModeOption(
    int Id,
    string Label,
    LanConnectLanHostMode Mode);

internal static class LanConnectLanHostModeSelection
{
    public static IReadOnlyList<LanConnectLanHostModeOption> Options { get; } =
    [
        new(0, "标准模式", LanConnectLanHostMode.Standard),
        new(1, "多人每日挑战", LanConnectLanHostMode.Daily),
        new(2, "自定义模式", LanConnectLanHostMode.Custom),
        new(3, "极速模式", LanConnectLanHostMode.Blitz),
        new(4, "恐惧模式", LanConnectLanHostMode.Dread)
    ];

    public static bool TryResolve(
        long id,
        LanConnectLanHostModeAvailability availability,
        out LanConnectLanHostMode mode)
    {
        foreach (LanConnectLanHostModeOption option in Options)
        {
            if (option.Id != id || !IsAvailable(option.Mode, availability))
            {
                continue;
            }

            mode = option.Mode;
            return true;
        }

        mode = default;
        return false;
    }

    internal static bool IsAvailable(
        LanConnectLanHostMode mode,
        LanConnectLanHostModeAvailability availability) => mode switch
    {
        LanConnectLanHostMode.Standard => availability.Standard,
        LanConnectLanHostMode.Daily => availability.Daily,
        LanConnectLanHostMode.Custom => availability.Custom,
        LanConnectLanHostMode.Blitz => availability.Blitz,
        LanConnectLanHostMode.Dread => availability.Dread,
        _ => false
    };
}

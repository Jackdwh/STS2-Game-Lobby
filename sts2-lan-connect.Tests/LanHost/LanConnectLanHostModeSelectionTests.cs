using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.LanHost;

public sealed class LanConnectLanHostModeSelectionTests
{
    [Fact]
    public void Options_keep_existing_ids_and_add_native_unlocked_modes()
    {
        Assert.Equal(
            new[]
            {
                new LanConnectLanHostModeOption(0, "标准模式", LanConnectLanHostMode.Standard),
                new LanConnectLanHostModeOption(1, "多人每日挑战", LanConnectLanHostMode.Daily),
                new LanConnectLanHostModeOption(2, "自定义模式", LanConnectLanHostMode.Custom),
                new LanConnectLanHostModeOption(3, "极速模式", LanConnectLanHostMode.Blitz),
                new LanConnectLanHostModeOption(4, "恐惧模式", LanConnectLanHostMode.Dread)
            },
            LanConnectLanHostModeSelection.Options);
    }

    [Theory]
    [InlineData(0, (int)LanConnectLanHostMode.Standard)]
    [InlineData(1, (int)LanConnectLanHostMode.Daily)]
    [InlineData(2, (int)LanConnectLanHostMode.Custom)]
    [InlineData(3, (int)LanConnectLanHostMode.Blitz)]
    [InlineData(4, (int)LanConnectLanHostMode.Dread)]
    public void Available_option_resolves_to_expected_mode(long id, int expected)
    {
        bool resolved = LanConnectLanHostModeSelection.TryResolve(
            id, new(true, true, true, true, true), out LanConnectLanHostMode mode);
        Assert.True(resolved);
        Assert.Equal((LanConnectLanHostMode)expected, mode);
    }

    [Fact]
    public void Unknown_option_is_rejected()
    {
        Assert.False(LanConnectLanHostModeSelection.TryResolve(99, new(true, true, true, true, true), out _));
    }

    [Theory]
    [InlineData(0, false, true, true)]
    [InlineData(1, true, false, true)]
    [InlineData(2, true, true, false)]
    public void Locked_option_is_rejected(long id, bool standard, bool daily, bool custom)
    {
        Assert.False(LanConnectLanHostModeSelection.TryResolve(id, new(standard, daily, custom), out _));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public void Additional_modes_are_locked_when_native_availability_is_unknown(long id)
    {
        Assert.False(LanConnectLanHostModeSelection.TryResolve(id, new(true, true, true), out _));
    }
}

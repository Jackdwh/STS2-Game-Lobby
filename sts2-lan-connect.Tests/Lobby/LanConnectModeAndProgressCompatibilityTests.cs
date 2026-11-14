using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Lobby;

public sealed class LanConnectModeAndProgressCompatibilityTests
{
    [Theory]
    [InlineData("Standard", true, false, false, "custom")]
    [InlineData("Standard", false, true, false, "standard")]
    [InlineData("Blitz", false, true, false, "blitz")]
    [InlineData("Dread", false, false, false, "dread")]
    [InlineData("Blitz", true, false, false, "custom")]
    [InlineData("Daily", false, false, true, "daily")]
    [InlineData("Daily", true, false, true, "daily")]
    [InlineData("Custom", null, false, false, "custom")]
    [InlineData(null, null, false, false, "standard")]
    [InlineData(null, null, true, false, "custom")]
    [InlineData(null, null, true, true, "daily")]
    public void Saved_mode_uses_explicit_semantics_with_legacy_fallback(
        string? mode, bool? custom, bool modifiers, bool daily, string expected) =>
        Assert.Equal(expected, LanConnectGameModeCompatibility.ResolveSavedModeName(mode, custom, modifiers, daily));

    [Theory]
    [InlineData("None")]
    [InlineData("FutureMode")]
    public void Unknown_native_mode_is_not_silently_loaded_as_a_different_mode(string mode) =>
        Assert.Throws<NotSupportedException>(() => LanConnectGameModeCompatibility.ResolveSavedModeName(mode, false, false, false));

    [Fact]
    public void Legacy_void_progress_update_is_invoked_once_without_inventing_a_success_flag()
    {
        var manager = new LegacyManager();
        var run = new FakeRun();
        Assert.Null(LanConnectProgressUpdateCompatibility.Update(manager, run, false));
        Assert.Equal(1, manager.Calls);
        Assert.Same(run, manager.LastRun);
        Assert.False(manager.Victory);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void New_progress_result_is_reported_without_rewriting_it(bool applied)
    {
        var manager = new CurrentManager(applied);
        Assert.Equal(applied, LanConnectProgressUpdateCompatibility.Update(manager, new FakeRun(), false));
    }

    [Fact]
    public void Game_exception_is_not_hidden_inside_a_reflection_exception()
    {
        var exception = new InvalidOperationException("progress update failed");
        var thrown = Assert.Throws<InvalidOperationException>(() =>
            LanConnectProgressUpdateCompatibility.Update(new FailingManager(exception), new FakeRun(), false));
        Assert.Same(exception, thrown);
    }

    [Fact]
    public void Unknown_progress_result_is_not_reported_as_success() =>
        Assert.Throws<InvalidOperationException>(() =>
            LanConnectProgressUpdateCompatibility.Update(new UnknownManager(), new FakeRun(), false));

    private sealed class FakeRun { }
    private sealed class LegacyManager
    {
        public int Calls { get; private set; }
        public FakeRun? LastRun { get; private set; }
        public bool Victory { get; private set; }
        public void UpdateProgressWithRunData(FakeRun run, bool victory)
        {
            Calls++;
            LastRun = run;
            Victory = victory;
        }
    }
    private sealed class CurrentManager(bool applied)
    {
        public Result UpdateProgressWithRunData(FakeRun run, bool victory) => new() { success = applied };
    }
    private struct Result { public bool success; }
    private sealed class FailingManager(Exception exception)
    {
        public void UpdateProgressWithRunData(FakeRun run, bool victory) => throw exception;
    }
    private sealed class UnknownManager
    {
        public object UpdateProgressWithRunData(FakeRun run, bool victory) => new();
    }
}

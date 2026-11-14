using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Protocol;

public sealed class LanConnectRosterDepartureTests
{
    [Fact]
    public void Departure_preserves_slots_and_revision_and_is_idempotent()
    {
        var state = CreateState();
        Assert.True(state.ApplyNativeDeparture(1, 22));
        Assert.Equal(5u, state.Current!.RosterRevision);
        Assert.Equal(new[] { 1UL, 33UL }, state.Current.Players.Select(p => p.PlayerId));
        Assert.Equal(new byte[] { 0, 7 }, state.Current.Players.Select(p => p.RealSlotId));
        Assert.False(state.ApplyNativeDeparture(1, 22));
        Assert.False(state.ApplyNativeDeparture(1, 99));
        state.Accept(1, state.Current, LanConnectRosterSnapshotUse.CurrentState);
    }

    [Fact]
    public void Departure_does_not_allow_a_stale_snapshot_to_restore_removed_members()
    {
        var state = CreateState();
        var original = state.Current!;
        state.ApplyNativeDeparture(1, 22);
        Assert.Throws<InvalidDataException>(() => state.Accept(1, original, LanConnectRosterSnapshotUse.CurrentState));
        Assert.Throws<InvalidDataException>(() => state.Accept(1, original, LanConnectRosterSnapshotUse.StateTransition));
        Assert.Equal(new[] { 1UL, 33UL }, state.Current!.Players.Select(p => p.PlayerId));
    }

    [Theory]
    [InlineData(22UL, 33UL)]
    [InlineData(1UL, 1UL)]
    public void Invalid_departure_authority_does_not_change_roster(ulong sender, ulong departed)
    {
        var state = CreateState();
        var original = state.Current;
        Assert.Throws<InvalidDataException>(() => state.ApplyNativeDeparture(sender, departed));
        Assert.Same(original, state.Current);
    }

    [Fact]
    public void Early_departure_does_not_create_an_authority_snapshot()
    {
        var state = new LanConnectRosterAuthorityState(1);
        Assert.False(state.ApplyNativeDeparture(1, 22));
        Assert.Null(state.Current);
    }

    [Fact]
    public void Departure_then_replacement_uses_the_existing_mutation_revision_rule()
    {
        var host = CreateState();
        var client = CreateState();
        host.ApplyNativeDeparture(1, 22);
        client.ApplyNativeDeparture(1, 22);
        var next = host.CommitHostSnapshot([.. host.Current!.Players, new(44, 2, 8, [0])]);
        Assert.Equal(6u, next.RosterRevision);
        client.Accept(1, next, LanConnectRosterSnapshotUse.MembershipMutation, [1UL, 33UL, 44UL], 44);
        Assert.Equal(new[] { 1UL, 44UL, 33UL }, client.Current!.Players.Select(p => p.PlayerId));
    }

    [Fact]
    public void Same_saved_identity_can_rejoin_after_its_previous_occupant_leaves()
    {
        var host = CreateState();
        var client = CreateState();
        var before = host.Current!;
        host.ApplyNativeDeparture(1, 22);
        client.ApplyNativeDeparture(1, 22);
        var next = host.CommitHostSnapshot(before.Players);
        Assert.Equal(6u, next.RosterRevision);
        client.Accept(1, next, LanConnectRosterSnapshotUse.MembershipMutation, [1UL, 22UL, 33UL], 22);
        Assert.Equal(before.Players.Select(p => p.PlayerId), client.Current!.Players.Select(p => p.PlayerId));
    }

    [Fact]
    public void Host_only_intermediate_state_does_not_change_the_wire_player_count_limit()
    {
        var host = CreateState();
        var client = CreateState();
        foreach (ulong departed in new[] { 22UL, 33UL })
        {
            host.ApplyNativeDeparture(1, departed);
            client.ApplyNativeDeparture(1, departed);
        }
        Assert.Single(host.Current!.Players);
        Assert.Throws<InvalidDataException>(() => LanConnectRosterCodec.Encode(host.Current));
        var next = host.CommitHostSnapshot([.. host.Current.Players, new(44, 2, 8, [0])]);
        client.Accept(1, next, LanConnectRosterSnapshotUse.MembershipMutation, [1UL, 44UL], 44);
        Assert.Equal(6u, client.Current!.RosterRevision);
    }

    private static LanConnectRosterAuthorityState CreateState()
    {
        var state = new LanConnectRosterAuthorityState(1);
        state.Accept(1, new(1, 5, [new(1, 0, 8, [0]), new(22, 2, 8, [0]), new(33, 7, 8, [0])]),
            LanConnectRosterSnapshotUse.Bootstrap);
        return state;
    }
}

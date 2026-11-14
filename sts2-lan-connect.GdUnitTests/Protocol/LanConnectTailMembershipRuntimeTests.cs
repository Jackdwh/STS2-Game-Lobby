using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Runs;
using GdUnit4;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using Sts2LanConnect.Scripts;
using static GdUnit4.Assertions;

namespace Sts2LanConnect.GdUnitTests.Protocol;

public sealed partial class LanConnectTailMessageRuntimeTests
{
    [TestCase]
    public void Load_join_accepts_the_same_saved_roles_in_a_different_arrival_order()
    {
        InitializeSts2Serialization();
        using RuntimePair pair = new();
        var run = RunWithPlayers(5);
        ClientLoadJoinResponseMessage response = new()
        {
            serializableRun = run,
            playersAlreadyConnected = new[] { 0, 3, 1 }
                .Select(slot => new LoadRunLobbyPlayer { id = run.Players[slot].NetId, isReady = true }).ToList()
        };
        LanConnectPreparedTailMessage prepared = pair.Runtime.PrepareOutgoing(
            pair.HostBus, LanConnectSidecarMessageKind.LoadJoinResponse, pair.HostId, response, pair.Selection);
        INetMessage boxed = (INetMessage)prepared.Message;
        pair.Runtime.ValidateIncoming(pair.ClientBus, LanConnectSidecarMessageKind.LoadJoinResponse,
            pair.HostId, boxed, prepared.Container, pair.Selection);
        var restored = (ClientLoadJoinResponseMessage)boxed;
        AssertThat(restored.playersAlreadyConnected.Select(p => p.id).ToArray()).IsEqual(new[] { 100UL, 101UL, 103UL });
        AssertThat(run.Players.Select(p => p.NetId).ToArray()).IsEqual(new[] { 100UL, 101UL, 102UL, 103UL, 104UL });
    }

    [TestCase]
    public void Native_departure_allows_remaining_players_to_begin_without_resetting_authority()
    {
        InitializeSts2Serialization();
        using RuntimePair pair = new();
        using IDisposable session = pair.FreezeHostSession();
        SeedMembership(pair, MembershipPlayers(0, 1, 2));
        DeliverDeparture(pair, playerId: 23);
        LanConnectPreparedTailMessage begin = PrepareMembershipBegin(pair, MembershipPlayers(0, 1));
        INetMessage boxed = (INetMessage)begin.Message;
        pair.Runtime.ValidateIncoming(pair.ClientBus, LanConnectSidecarMessageKind.LobbyBeginRun,
            pair.HostId, boxed, begin.Container, pair.Selection);
        AssertThat(((LobbyBeginRunMessage)boxed).playersInLobby!.Select(p => p.id).ToArray())
            .IsEqual(new[] { 1UL, 22UL });
    }

    [TestCase]
    public void Departure_preserves_sparse_slots_when_projection_indices_change()
    {
        InitializeSts2Serialization();
        using RuntimePair pair = new();
        using IDisposable session = pair.FreezeHostSession();
        SeedMembership(pair, MembershipPlayers(0, 1, 2, 4, 7));
        DeliverDeparture(pair, playerId: 23);
        var begin = PrepareMembershipBegin(pair, MembershipPlayers(0, 1, 4, 7));
        INetMessage boxed = (INetMessage)begin.Message;
        pair.Runtime.ValidateIncoming(pair.ClientBus, LanConnectSidecarMessageKind.LobbyBeginRun,
            pair.HostId, boxed, begin.Container, pair.Selection);
        AssertThat(((LobbyBeginRunMessage)boxed).playersInLobby!.Select(p => p.slotId).ToArray())
            .IsEqual(new[] { 0, 1, 4, 7 });
    }

    [TestCase("extra")]
    [TestCase("missing")]
    [TestCase("duplicate")]
    [TestCase("unknown")]
    [TestCase("slot")]
    [TestCase("saved_duplicate")]
    public void Load_join_rejects_invalid_membership_without_poisoning_bootstrap(string corruption)
    {
        InitializeSts2Serialization();
        using RuntimePair pair = new();
        var run = RunWithPlayers(5);
        ClientLoadJoinResponseMessage response = new()
        {
            serializableRun = run,
            playersAlreadyConnected = new[] { 0, 3, 1 }
                .Select(slot => new LoadRunLobbyPlayer { id = run.Players[slot].NetId }).ToList()
        };
        var prepared = pair.Runtime.PrepareOutgoing(pair.HostBus, LanConnectSidecarMessageKind.LoadJoinResponse,
            pair.HostId, response, pair.Selection);
        switch (corruption)
        {
            case "extra": response.playersAlreadyConnected.Add(new() { id = 104 }); break;
            case "missing": response.playersAlreadyConnected.RemoveAt(1); break;
            case "duplicate": response.playersAlreadyConnected[1] = new() { id = 101 }; break;
            case "unknown": response.playersAlreadyConnected[1] = new() { id = 999 }; break;
            case "slot": (run.Players[1], run.Players[3]) = (run.Players[3], run.Players[1]); break;
            case "saved_duplicate": run.Players.Add(run.Players[0]); break;
        }
        AssertThrown(() => pair.Runtime.ValidateIncoming(pair.ClientBus, LanConnectSidecarMessageKind.LoadJoinResponse,
            pair.HostId, response, prepared.Container, pair.Selection));

        var cleanRun = RunWithPlayers(5);
        var valid = pair.Runtime.PrepareOutgoing(pair.HostBus, LanConnectSidecarMessageKind.LoadJoinResponse,
            pair.HostId, new ClientLoadJoinResponseMessage
            {
                serializableRun = cleanRun,
                playersAlreadyConnected = new[] { 0, 4, 2 }
                    .Select(slot => new LoadRunLobbyPlayer { id = cleanRun.Players[slot].NetId }).ToList()
            }, pair.Selection);
        // A different first valid snapshot must still bootstrap: the rejected one cannot commit authority.
        pair.Runtime.ValidateIncoming(pair.ClientBus, LanConnectSidecarMessageKind.LoadJoinResponse,
            pair.HostId, (INetMessage)valid.Message, valid.Container, pair.Selection);
    }

    [TestCase("wrong_sender")]
    [TestCase("wrong_channel")]
    [TestCase("missing_context")]
    [TestCase("remove_host")]
    public void Unauthenticated_or_invalid_departure_is_rejected(string corruption)
    {
        InitializeSts2Serialization();
        using RuntimePair pair = new();
        using IDisposable session = pair.FreezeClientSession();
        SeedMembership(pair, MembershipPlayers(0, 1, 2));
        INetMessage departure = new PlayerLeftMessage { playerId = corruption == "remove_host" ? pair.HostId : 23UL };
        bool accepted;
        if (corruption == "missing_context")
        {
            accepted = pair.Runtime.TryEnterNativeDispatch(pair.ClientBus, departure, pair.HostId);
        }
        else
        {
            using var receive = LanConnectTailMessagePatches.PushTransportReceiveContextForTesting(
                corruption == "wrong_sender" ? 999UL : pair.HostId,
                channel: corruption == "wrong_channel" ? 1 : 0);
            accepted = pair.Runtime.TryEnterNativeDispatch(pair.ClientBus, departure, pair.HostId);
        }
        AssertThat(accepted).IsFalse();
        AssertThat(pair.ClientTransport.DisconnectReasons.Contains(NetError.ModMismatch)).IsTrue();
    }

    [TestCase]
    public void Client_cannot_send_a_departure_to_mutate_the_host_roster()
    {
        InitializeSts2Serialization();
        using RuntimePair pair = new();
        using IDisposable session = pair.FreezeHostSession();
        SeedMembership(pair, MembershipPlayers(0, 1, 2));
        using var receive = LanConnectTailMessagePatches.PushTransportReceiveContextForTesting(pair.ClientId, channel: 0);
        AssertThat(pair.Runtime.TryEnterNativeDispatch(pair.HostBus, new PlayerLeftMessage { playerId = 23 }, pair.HostId)).IsFalse();
        AssertThat(pair.HostTransport.DisconnectedPeers.Contains(pair.ClientId)).IsTrue();
    }

    [TestCase]
    public void Buffered_departure_uses_recorded_transport_authority_only_when_released()
    {
        InitializeSts2Serialization();
        using RuntimePair pair = new();
        using IDisposable session = pair.FreezeClientSession();
        SeedMembership(pair, MembershipPlayers(0, 1, 2));
        INetMessage departure = new PlayerLeftMessage { playerId = 23 };
        SetBusBuffering(pair.ClientBus, true);
        using (LanConnectTailMessagePatches.PushTransportReceiveContextForTesting(pair.HostId, channel: 0))
        {
            AssertThat(pair.Runtime.TryEnterNativeDispatch(pair.ClientBus, departure, senderId: 999)).IsTrue();
        }
        // Still buffered: the original full roster remains valid.
        var unchanged = PrepareMembershipBegin(pair, MembershipPlayers(0, 1, 2));
        pair.Runtime.ValidateIncoming(pair.ClientBus, LanConnectSidecarMessageKind.LobbyBeginRun,
            pair.HostId, (INetMessage)unchanged.Message, unchanged.Container, pair.Selection);
        SetBusBuffering(pair.ClientBus, false);
        AssertThat(pair.Runtime.TryEnterNativeDispatch(pair.ClientBus, departure, senderId: 999)).IsTrue();
        var after = PrepareMembershipBegin(pair, MembershipPlayers(0, 1));
        pair.Runtime.ValidateIncoming(pair.ClientBus, LanConnectSidecarMessageKind.LobbyBeginRun,
            pair.HostId, (INetMessage)after.Message, after.Container, pair.Selection);
        // A normal non-buffered dispatch must not leave reusable authority in the sidetable.
        AssertThat(pair.Runtime.TryEnterNativeDispatch(pair.ClientBus, departure, pair.HostId)).IsFalse();
    }

    [TestCase]
    public void Departure_cannot_interrupt_a_matrix_extension_pair()
    {
        InitializeSts2Serialization();
        using RuntimePair pair = new();
        using IDisposable session = pair.FreezeClientSession();
        SeedMembership(pair, MembershipPlayers(0, 1, 2));
        var begin = PrepareMembershipBegin(pair, MembershipPlayers(0, 1, 2));
        using var receive = LanConnectTailMessagePatches.PushTransportReceiveContextForTesting(pair.HostId, channel: 0);
        AssertThat(pair.Runtime.TryEnterNativeDispatch(pair.ClientBus, (INetMessage)begin.Message, pair.HostId)).IsFalse();
        AssertThat(pair.Runtime.TryEnterNativeDispatch(pair.ClientBus, new PlayerLeftMessage { playerId = 23 }, pair.HostId)).IsFalse();
        AssertThat(pair.ClientTransport.DisconnectReasons.Contains(NetError.ModMismatch)).IsTrue();
    }

    [TestCase]
    public void Departure_broadcast_and_duplicate_notifications_are_idempotent_and_preserve_trailers()
    {
        InitializeSts2Serialization();
        using RuntimePair pair = new();
        using IDisposable session = pair.FreezeHostSession();
        SeedMembership(pair, MembershipPlayers(0, 1, 2));
        PlayerLeftMessage departure = new() { playerId = 23 };
        byte[] buffer = pair.HostBus.SerializeMessage(pair.HostId, departure, out int length).AsSpan(0, length).ToArray();
        byte[] withTrailer = [.. buffer, 0xde, 0xad, 0xbe, 0xef];
        byte[] before = withTrailer.ToArray();
        AssertThat(pair.Runtime.BeginNativeTransport(pair.HostTransport, true, pair.ClientId, withTrailer, withTrailer.Length)).IsNull();
        AssertThat(pair.Runtime.BeginNativeTransport(pair.HostTransport, true, 24, withTrailer, withTrailer.Length)).IsNull();
        AssertThat(withTrailer).IsEqual(before);
        using (LanConnectTailMessagePatches.PushTransportReceiveContextForTesting(pair.HostId, channel: 0))
        {
            AssertThat(pair.Runtime.TryEnterNativeDispatch(pair.ClientBus, departure, pair.HostId)).IsTrue();
            AssertThat(pair.Runtime.TryEnterNativeDispatch(pair.ClientBus, departure, pair.HostId)).IsTrue();
        }
        var begin = PrepareMembershipBegin(pair, MembershipPlayers(0, 1));
        AssertThat(DecodeRoster(LanConnectSidecarMessageKind.LobbyBeginRun, begin.Container, pair).RosterRevision).IsEqual(1u);
        pair.Runtime.ValidateIncoming(pair.ClientBus, LanConnectSidecarMessageKind.LobbyBeginRun,
            pair.HostId, (INetMessage)begin.Message, begin.Container, pair.Selection);
        AssertThat(pair.HostTransport.SentToClients.Count).IsEqual(0);
    }

    [TestCase]
    public void Same_saved_role_can_join_again_after_its_occupant_leaves()
    {
        InitializeSts2Serialization();
        using RuntimePair pair = new();
        using IDisposable session = pair.FreezeHostSession();
        SeedMembership(pair, MembershipPlayers(0, 1, 2));
        byte[] previousNonce = Enumerable.Repeat((byte)0x31, 16).ToArray();
        byte[] replacementNonce = Enumerable.Repeat((byte)0x52, 16).ToArray();
        pair.PrepareHostNativeFlow(23, previousNonce);
        DeliverDeparture(pair, playerId: 23);
        // The existing HostedRoomSession disconnect callback clears the old ticket flow.
        pair.Runtime.ClearNativePeer(pair.Host, 23);
        pair.PrepareHostNativeFlow(23, replacementNonce);
        // The replacement inherits the same saved ID/slot; controller identity is not a roster key.
        var join = pair.Runtime.PrepareOutgoing(pair.HostBus, LanConnectSidecarMessageKind.LobbyJoinResponse,
            pair.HostId, new ClientLobbyJoinResponseMessage { playersInLobby = MembershipPlayers(0, 1, 2), modifiers = [] }, pair.Selection);
        AssertThat(DecodeRoster(LanConnectSidecarMessageKind.LobbyJoinResponse, join.Container, pair).RosterRevision).IsEqual(2u);
        var joined = pair.Runtime.PrepareOutgoing(pair.HostBus, LanConnectSidecarMessageKind.PlayerJoined,
            pair.HostId, new PlayerJoinedMessage { lobbyPlayer = MembershipPlayers(2).Single() }, pair.Selection);
        pair.Runtime.ValidateIncoming(pair.ClientBus, LanConnectSidecarMessageKind.PlayerJoined,
            pair.HostId, (INetMessage)joined.Message, joined.Container, pair.Selection);
        PendingVanilla pending = pair.SerializeHostMatrixMessage(LanConnectSidecarMessageKind.PlayerJoined,
            new PlayerJoinedMessage { lobbyPlayer = MembershipPlayers(2).Single() });
        pair.DeliverPendingToPeer(pending, 23);
        AssertThat(FrameNonceOf(pair.HostTransport.SentToClients.Last())).IsEqual(replacementNonce);
        var begin = PrepareMembershipBegin(pair, MembershipPlayers(0, 1, 2));
        pair.Runtime.ValidateIncoming(pair.ClientBus, LanConnectSidecarMessageKind.LobbyBeginRun,
            pair.HostId, (INetMessage)begin.Message, begin.Container, pair.Selection);
    }

    [TestCase]
    public void Replaying_a_validated_matrix_does_not_bypass_nested_departure_authentication()
    {
        InitializeSts2Serialization();
        using RuntimePair pair = new();
        using IDisposable session = pair.FreezeClientSession();
        bool called = false;
        bool accepted = true;
        bool otherBusAccepted = true;
        INetMessage? validatedMessage = null;
        pair.ClientBus.RegisterMessageHandler<ClientLobbyJoinResponseMessage>((_, _) =>
        {
            called = true;
            otherBusAccepted = pair.Runtime.TryEnterNativeDispatch(pair.HostBus, validatedMessage!, pair.HostId);
            using var attacker = LanConnectTailMessagePatches.PushTransportReceiveContextForTesting(999, channel: 0);
            accepted = pair.Runtime.TryEnterNativeDispatch(pair.ClientBus, new PlayerLeftMessage { playerId = 23 }, pair.HostId);
        });
        var initial = pair.Runtime.PrepareOutgoing(pair.HostBus, LanConnectSidecarMessageKind.LobbyJoinResponse,
            pair.HostId, new ClientLobbyJoinResponseMessage { playersInLobby = MembershipPlayers(0, 1, 2), modifiers = [] }, pair.Selection);
        validatedMessage = (INetMessage)initial.Message;
        using (LanConnectTailMessagePatches.PushTransportReceiveContextForTesting(pair.HostId, channel: 0))
        {
            AssertThat(pair.Runtime.TryEnterNativeDispatch(pair.ClientBus, validatedMessage, pair.HostId)).IsFalse();
            var extension = BuildExtension(LanConnectSidecarMessageKind.LobbyJoinResponse,
                pair.ProtocolFlowNonce, 1, initial.Container);
            AssertThat(pair.Runtime.TryEnterNativeDispatch(pair.ClientBus, extension, pair.HostId)).IsFalse();
        }
        AssertThat(called).IsTrue();
        AssertThat(accepted).IsFalse();
        AssertThat(otherBusAccepted).IsFalse();
    }

    [TestCase]
    public void In_run_disconnect_does_not_remove_saved_roles_from_rejoin_authority()
    {
        InitializeSts2Serialization();
        using RuntimePair pair = new();
        using IDisposable session = pair.FreezeHostSession();
        SeedMembership(pair, MembershipPlayers(0, 1, 2));
        var stateField = AccessTools.Field(typeof(RunManager), "<State>k__BackingField");
        var serviceField = AccessTools.Field(typeof(RunManager), "<NetService>k__BackingField");
        object? previousState = stateField.GetValue(RunManager.Instance);
        object? previousService = serviceField.GetValue(RunManager.Instance);
        try
        {
            stateField.SetValue(RunManager.Instance, RuntimeHelpers.GetUninitializedObject(typeof(RunState)));
            serviceField.SetValue(RunManager.Instance, pair.Host);
            PlayerLeftMessage departure = new() { playerId = 23 };
            byte[] buffer = pair.HostBus.SerializeMessage(pair.HostId, departure, out int length).AsSpan(0, length).ToArray();
            AssertThat(pair.Runtime.BeginNativeTransport(pair.HostTransport, true, pair.ClientId, buffer, length)).IsNull();
            serviceField.SetValue(RunManager.Instance, pair.Client);
            using (LanConnectTailMessagePatches.PushTransportReceiveContextForTesting(pair.HostId, channel: 0))
            {
                AssertThat(pair.Runtime.TryEnterNativeDispatch(pair.ClientBus, departure, pair.HostId)).IsTrue();
            }
            var unchanged = PrepareMembershipBegin(pair, MembershipPlayers(0, 1, 2));
            AssertThat(DecodeRoster(LanConnectSidecarMessageKind.LobbyBeginRun, unchanged.Container, pair).RosterRevision).IsEqual(1u);
            pair.Runtime.ValidateIncoming(pair.ClientBus, LanConnectSidecarMessageKind.LobbyBeginRun,
                pair.HostId, (INetMessage)unchanged.Message, unchanged.Container, pair.Selection);
        }
        finally
        {
            stateField.SetValue(RunManager.Instance, previousState);
            serviceField.SetValue(RunManager.Instance, previousService);
        }
    }

    [TestCase]
    public void Compat_dispatch_does_not_apply_tail_departure_rules()
    {
        InitializeSts2Serialization();
        using RuntimePair pair = new();
        SeedMembership(pair, MembershipPlayers(0, 1, 2));
        using (LanConnectSessionProtocolState.Shared.FreezeHost(
            LanConnectProtocolSelection.CreateLocalCompat(8, "0.113.0", null), "compat-departure"))
        {
            // No Tail transport context is required and no Tail roster is changed.
            AssertThat(pair.Runtime.TryEnterNativeDispatch(pair.ClientBus, new PlayerLeftMessage { playerId = 23 }, pair.HostId)).IsTrue();
        }
        var begin = PrepareMembershipBegin(pair, MembershipPlayers(0, 1, 2));
        pair.Runtime.ValidateIncoming(pair.ClientBus, LanConnectSidecarMessageKind.LobbyBeginRun,
            pair.HostId, (INetMessage)begin.Message, begin.Container, pair.Selection);
    }

    private static List<StartRunLobbyPlayer> MembershipPlayers(params int[] slots) =>
        StartRunPlayers(slots).Select(player =>
        {
            player.id = player.slotId == 0 ? 1UL : 21UL + (ulong)player.slotId;
            return player;
        }).ToList();

    private static void SeedMembership(RuntimePair pair, List<StartRunLobbyPlayer> players)
    {
        LanConnectPreparedTailMessage initial = pair.Runtime.PrepareOutgoing(
            pair.HostBus, LanConnectSidecarMessageKind.LobbyJoinResponse, pair.HostId,
            new ClientLobbyJoinResponseMessage { playersInLobby = players, modifiers = [] }, pair.Selection);
        pair.Runtime.ValidateIncoming(pair.ClientBus, LanConnectSidecarMessageKind.LobbyJoinResponse,
            pair.HostId, (INetMessage)initial.Message, initial.Container, pair.Selection);
    }

    private static LanConnectPreparedTailMessage PrepareMembershipBegin(RuntimePair pair, List<StartRunLobbyPlayer> players) =>
        pair.Runtime.PrepareOutgoing(pair.HostBus, LanConnectSidecarMessageKind.LobbyBeginRun, pair.HostId,
            new LobbyBeginRunMessage { playersInLobby = players, seed = "membership", modifiers = [], act1 = "Act1" },
            pair.Selection);

    private static void DeliverDeparture(RuntimePair pair, ulong playerId)
    {
        PlayerLeftMessage departure = new() { playerId = playerId };
        byte[] buffer = pair.HostBus.SerializeMessage(pair.HostId, departure, out int length).AsSpan(0, length).ToArray();
        AssertThat(pair.Runtime.BeginNativeTransport(pair.HostTransport, true, pair.ClientId, buffer, length)).IsNull();
        using (LanConnectTailMessagePatches.PushTransportReceiveContextForTesting(pair.HostId, channel: 0))
        {
            AssertThat(pair.Runtime.TryEnterNativeDispatch(pair.ClientBus, departure, pair.HostId)).IsTrue();
        }
    }
}

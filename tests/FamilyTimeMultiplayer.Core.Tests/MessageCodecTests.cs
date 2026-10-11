using System.Linq;
using System.IO;
using FamilyTimeMultiplayer.Core.Protocol;
using Xunit;

namespace FamilyTimeMultiplayer.Core.Tests;

public class MessageCodecTests
{
    private static T RoundTrip<T>(T message) where T : INetMessage =>
        Assert.IsType<T>(MessageCodec.Decode(MessageCodec.Encode(message)));

    private static readonly NetPose SamplePose =
        new NetPose(new NetVector3(1f, 2f, 3f), new NetQuaternion(0f, 0.7071f, 0f, 0.7071f), new NetVector3(0.5f, 0.5f, 0.5f));

    [Fact]
    public void HelloRoundTripsAllFields()
    {
        HelloMessage decoded = RoundTrip(new HelloMessage(ProtocolConstants.Version, "build-guid", "Gosia"));
        Assert.Equal(ProtocolConstants.Version, decoded.ProtocolVersion);
        Assert.Equal("build-guid", decoded.GameBuildId);
        Assert.Equal("Gosia", decoded.PlayerName);
    }

    [Fact]
    public void SnapshotRoundTripsAgentsInOrder()
    {
        var agents = new[] { new AgentState(7, SamplePose), new AgentState(65000, SamplePose) };
        AgentSnapshotMessage decoded = RoundTrip(new AgentSnapshotMessage(12.5, agents));
        Assert.Equal(12.5, decoded.HostTimeSeconds);
        Assert.Equal(new ushort[] { 7, 65000 }, new[] { decoded.Agents[0].StableIndex, decoded.Agents[1].StableIndex });
        Assert.Equal(SamplePose, decoded.Agents[1].Pose);
    }

    [Fact]
    public void SpawnAndDespawnRoundTrip()
    {
        AgentSpawnMessage spawn = RoundTrip(new AgentSpawnMessage(new[] { new AgentSpawn(3, 9, SamplePose) }));
        Assert.Equal(9, spawn.Spawns[0].TemplateIndex);
        AgentDespawnMessage despawn = RoundTrip(new AgentDespawnMessage(new ushort[] { 1, 2, 3 }));
        Assert.Equal(new ushort[] { 1, 2, 3 }, despawn.StableIndices);
    }

    [Fact]
    public void WorldChunkRoundTripsBytes()
    {
        WorldChunkMessage decoded = RoundTrip(new WorldChunkMessage(1024, new byte[] { 1, 2, 3 }));
        Assert.Equal(1024, decoded.Offset);
        Assert.Equal(new byte[] { 1, 2, 3 }, decoded.Data);
    }

    [Fact]
    public void DayStateRoundTrips()
    {
        DayStateMessage decoded = RoundTrip(new DayStateMessage(4, 120f, 6f, 18f, true));
        Assert.Equal((4, 120f, 6f, 18f, true), (decoded.Day, decoded.Timer, decoded.StartTimeOfDay, decoded.EndTimeOfDay, decoded.IsMoving));
    }

    [Fact]
    public void RosterRoundTrips()
    {
        PlayerRosterMessage decoded = RoundTrip(new PlayerRosterMessage(new[] { new RosterEntry(0, "Host", 12), new RosterEntry(3, "Ola", 40) }));
        Assert.Equal(("Ola", 40), (decoded.Players[1].Name, (int)decoded.Players[1].AvatarStableIndex));
    }

    [Fact]
    public void WelcomeCarriesFamilyMode() =>
        Assert.Equal(FamilyMode.Separate, RoundTrip(new WelcomeMessage(1, 2, 100, FamilyMode.Separate)).FamilyMode);

    [Fact]
    public void WelcomeRejectsUnknownFamilyMode()
    {
        byte[] encoded = MessageCodec.Encode(new WelcomeMessage(1, 2, 100, FamilyMode.Shared));
        encoded[15] = 77;
        Assert.Throws<InvalidDataException>(() => MessageCodec.Decode(encoded));
    }

    [Fact]
    public void WelcomeRejectsEmptyWorld()
    {
        byte[] encoded = MessageCodec.Encode(new WelcomeMessage(1, 2, 0, FamilyMode.Shared));
        Assert.Throws<InvalidDataException>(() => MessageCodec.Decode(encoded));
    }

    [Fact]
    public void RigidMessagesRoundTrip()
    {
        RigidSnapshotMessage snapshot = RoundTrip(new RigidSnapshotMessage(3.5, new[] { new RigidState(70000, NetVector3.One, NetQuaternion.Identity, carried: true) }));
        Assert.True(snapshot.Rigids[0].Carried);
        RigidSpawnMessage spawn = RoundTrip(new RigidSpawnMessage(new[] { new RigidSpawn(5, 2, NetVector3.One, NetQuaternion.Identity) }));
        Assert.Equal(2, spawn.Spawns[0].PrefabIndex);
        Assert.Equal(new[] { 5, 6 }, RoundTrip(new RigidDespawnMessage(new[] { 5, 6 })).NetIds);
    }

    [Fact]
    public void PropAndCommandMessagesRoundTrip()
    {
        PropChangeMessage props = RoundTrip(new PropChangeMessage(new[] { new PropChange(10, 20, added: false, "log") }));
        Assert.Equal(("log", 20u), (props.Changes[0].PropName, props.Changes[0].CellZ));
        CommandIntentMessage intent = RoundTrip(CommandIntentMessage.InteractProp(4, 5, "rock"));
        Assert.Equal((CommandTargetKind.Prop, 4, "rock"), (intent.TargetKind, intent.PropCellX, intent.PropName));
        CommandFeedbackMessage feedback = RoundTrip(new CommandFeedbackMessage(2, 6, CommandOutcome.Refused, "needs 3"));
        Assert.Equal((2, CommandOutcome.Refused), (feedback.AvailableWorkers, feedback.Outcome));
    }

    [Fact]
    public void CommandIntentRejectsUnknownKind()
    {
        byte[] encoded = MessageCodec.Encode(CommandIntentMessage.Follow(pressed: true));
        encoded[1] = 99;
        Assert.Throws<InvalidDataException>(() => MessageCodec.Decode(encoded));
    }

    [Fact]
    public void DecodeRejectsUnknownType() =>
        Assert.Throws<InvalidDataException>(() => MessageCodec.Decode(new byte[] { 250 }));

    [Fact]
    public void DecodeRejectsTrailingBytes()
    {
        byte[] encoded = MessageCodec.Encode(new ClientReadyMessage());
        Assert.Throws<InvalidDataException>(() => MessageCodec.Decode(encoded.Concat(new byte[] { 0xFF }).ToArray()));
    }

    [Fact]
    public void DecodeRejectsNegativeAgentCount()
    {
        byte[] payload = { (byte)MessageType.AgentDespawn, 0xFF, 0xFF, 0xFF, 0xFF };
        Assert.Throws<InvalidDataException>(() => MessageCodec.Decode(payload));
    }

    [Fact]
    public void Crc32MatchesKnownVector() =>
        Assert.Equal(0xCBF43926u, Crc32.Compute(System.Text.Encoding.ASCII.GetBytes("123456789")));
}

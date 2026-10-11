using System;
using System.Linq;
using FamilyTimeMultiplayer.Core.Commands;
using FamilyTimeMultiplayer.Core.Families;
using FamilyTimeMultiplayer.Core.Protocol;
using Xunit;

namespace FamilyTimeMultiplayer.Core.Tests;

public class FamilyTests
{
    private static T RoundTrip<T>(T message) where T : INetMessage =>
        (T)MessageCodec.Decode(MessageCodec.Encode(message));

    [Fact]
    public void LedgerMovesMemberBetweenOwners()
    {
        var ledger = new FamilyLedger();
        ledger.Assign(5, "Host");
        ledger.Assign(6, "Host");
        ledger.Assign(5, "Gosia");
        Assert.Equal(1, ledger.CountOf("Host"));
        Assert.True(ledger.Belongs(5, "Gosia"));
        Assert.False(ledger.Belongs(5, "Host"));
    }

    [Fact]
    public void LedgerReleaseForgetsMember()
    {
        var ledger = new FamilyLedger();
        ledger.Assign(7, "Host");
        Assert.True(ledger.Release(7));
        Assert.False(ledger.Release(7));
        Assert.False(ledger.TryGetOwner(7, out _));
        Assert.Empty(ledger.MembersOf("Host"));
    }

    [Fact]
    public void LedgerRejectsEmptyOwner()
    {
        Assert.Throws<ArgumentException>(() => new FamilyLedger().Assign(1, ""));
    }

    [Fact]
    public void HappyFamilyGrowsByItsOwnSize()
    {
        FamilyNightOutcome outcome = NightRules.Evaluate(new FamilyNightState(3, 0, 0), 0.3f);
        Assert.True(outcome.Happy);
        Assert.Equal(3, outcome.GrownBy);
    }

    [Fact]
    public void GrowthUsesMultiplierAboveOne()
    {
        Assert.Equal(5, NightRules.Evaluate(new FamilyNightState(3, 0, 0), 1.5f).GrownBy);
    }

    [Theory]
    [InlineData(1, 0, true, false)]
    [InlineData(0, 2, false, true)]
    [InlineData(1, 1, true, true)]
    public void UnhappyFamilyDoesNotGrow(int notSleeping, int hungry, bool missing, bool isHungry)
    {
        FamilyNightOutcome outcome = NightRules.Evaluate(new FamilyNightState(4, notSleeping, hungry), 0.3f);
        Assert.Equal(missing, outcome.ChildrenMissing);
        Assert.Equal(isHungry, outcome.ChildrenHungry);
        Assert.Equal(0, outcome.GrownBy);
    }

    [Fact]
    public void EmptyFamilyDoesNotGrow()
    {
        Assert.Equal(0, NightRules.Evaluate(new FamilyNightState(0, 0, 0), 0.3f).GrownBy);
    }

    [Fact]
    public void BedWaitsForEveryPlayerInSession()
    {
        var beds = new BedReadiness();
        beds.MarkReady(0);
        Assert.Equal(new[] { 2 }, beds.Waiting(new[] { 0, 2 }));
        beds.MarkReady(2);
        Assert.True(beds.EveryoneReady(new[] { 0, 2 }));
        beds.Cancel(0);
        Assert.False(beds.EveryoneReady(new[] { 0, 2 }));
    }

    [Fact]
    public void BedIgnoresReadyPlayersWhoLeft()
    {
        var beds = new BedReadiness();
        beds.MarkReady(0);
        beds.MarkReady(3);
        Assert.True(beds.EveryoneReady(new[] { 0 }));
    }

    [Fact]
    public void FamilyMessagesRoundTrip()
    {
        FamilyStatusMessage status = RoundTrip(new FamilyStatusMessage(7, 2, 1));
        Assert.Equal((7, 2, 1), (status.Children, status.Hungry, status.NotSleeping));
        Assert.True(RoundTrip(new BedRequestMessage(true)).Ready);
        BedStatusMessage bed = RoundTrip(new BedStatusMessage(true, new[] { "Host", "Ola" }));
        Assert.True(bed.YouAreReady);
        Assert.Equal(new[] { "Host", "Ola" }, bed.WaitingFor);
        NightOutcomeMessage night = RoundTrip(new NightOutcomeMessage(false, true, 0));
        Assert.Equal((false, true, 0), (night.ChildrenMissing, night.ChildrenHungry, night.GrownBy));
    }

    [Fact]
    public void MamaRosterRoundTrips()
    {
        MamaRosterMessage roster = RoundTrip(new MamaRosterMessage(new[] { new MamaEntry("Ola", 231), new MamaEntry("Kuba", 7) }));
        Assert.Equal(new[] { ("Ola", (ushort)231), ("Kuba", (ushort)7) }, roster.Mamas.Select(m => (m.OwnerName, m.AgentStableIndex)));
    }

    [Fact]
    public void FamilyWorldRecordRoundTrips()
    {
        var record = new FamilyWorldRecord(new[]
        {
            new FamilyRecord("Host", true, new ushort[] { 3, 4, 5 }, null),
            new FamilyRecord("Ola", false, new ushort[] { 231, 232 }, 234),
        });
        using var stream = new System.IO.MemoryStream();
        record.Write(stream);
        stream.Position = 0;
        FamilyWorldRecord read = FamilyWorldRecord.Read(stream);
        Assert.True(read.IsSeparateWorld);
        Assert.Equal(new[] { "Host", "Ola" }, read.Families.Select(f => f.Owner));
        Assert.True(read.Families[0].IsHostFamily);
        Assert.Null(read.Families[0].MamaAgent);
        Assert.Equal((ushort)234, read.Families[1].MamaAgent);
        Assert.Equal(new ushort[] { 231, 232 }, read.Families[1].Members);
    }

    [Fact]
    public void EmptyRecordIsNotSeparateWorld()
    {
        using var stream = new System.IO.MemoryStream();
        FamilyWorldRecord.Empty.Write(stream);
        stream.Position = 0;
        Assert.False(FamilyWorldRecord.Read(stream).IsSeparateWorld);
    }

    [Fact]
    public void FamilyWorldRecordRejectsGarbage()
    {
        using var stream = new System.IO.MemoryStream(new byte[] { 1, 2, 3, 4, 5 });
        Assert.Throws<System.IO.InvalidDataException>(() => FamilyWorldRecord.Read(stream));
    }

    [Fact]
    public void FamilyWorldRecordRejectsTwoHostFamilies()
    {
        Assert.Throws<ArgumentException>(() => new FamilyWorldRecord(new[]
        {
            new FamilyRecord("A", true, new ushort[0], null),
            new FamilyRecord("B", true, new ushort[0], null),
        }));
    }

    [Fact]
    public void HoldMessagesRoundTrip()
    {
        var hold = new HoldMessage(HoldTarget.Mama(234), HoldPhase.End, true, new NetVector3(1, 2, 3), new NetQuaternion(0, 0, 0, 1), new NetVector3(4, 5, 6));
        HoldMessage read = RoundTrip(hold);
        Assert.Equal(HoldTarget.Mama(234), read.Target);
        Assert.Equal(HoldPhase.End, read.Phase);
        Assert.True(read.CarriedInHand);
        Assert.Equal(6f, read.Velocity.Z);
        HoldResultMessage result = RoundTrip(new HoldResultMessage(HoldTarget.Rigid(17), false, "Ola is holding it"));
        Assert.Equal((HoldTarget.Rigid(17), false, "Ola is holding it"), (result.Target, result.Granted, result.Reason));
        Assert.Equal((ushort)9, RoundTrip(new MamaToggleMessage(9)).Agent);
        MamaStateMessage state = RoundTrip(new MamaStateMessage(new[] { new MamaStateEntry(0, true), new MamaStateEntry(234, false) }));
        Assert.Equal(new[] { (0, true), (234, false) }, state.Mamas.Select(m => ((int)m.Agent, m.AllowFollow)));
    }

    [Fact]
    public void HoldRejectsUnknownKind() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new HoldTarget((HoldTargetKind)9, 1));

    [Fact]
    public void WorldEditsRoundTrip()
    {
        var key = new GridKey(12, -40, 63, 2048, 3);
        var edits = new[]
        {
            WorldEdit.BuildPartCreate(key, true),
            WorldEdit.BuildPartRemove(key),
            WorldEdit.BuildPlanCreate(key, 0.25f),
            WorldEdit.BuildPlanRemove(key),
            WorldEdit.BuildPlanConstruct(key, new[] { 4, 9 }, new NetVector3(1, 2, 3)),
            WorldEdit.PropRemove(-7, 19),
            WorldEdit.ItemSpawn(5, new NetVector3(1, 2, 3), new NetQuaternion(0, 0.7071f, 0, 0.7071f)),
            WorldEdit.ItemDelete(77),
            WorldEdit.TerrainHeight(10, -11, 200),
            WorldEdit.TerrainEffect(10, -11, 1f, 0.5f),
        };
        WorldEditMessage read = RoundTrip(new WorldEditMessage(edits));
        Assert.Equal(edits.Select(e => e.Kind), read.Edits.Select(e => e.Kind));
        Assert.True(read.Edits[0].Transient);
        Assert.Equal(key, read.Edits[1].Grid);
        Assert.Equal(0.25f, read.Edits[2].Value);
        Assert.Equal(new[] { 4, 9 }, read.Edits[4].Materials);
        Assert.Equal((-7, 19), (read.Edits[5].CellX, read.Edits[5].CellZ));
        Assert.Equal(5, read.Edits[6].Id);
        Assert.Equal(77, read.Edits[7].Id);
        Assert.Equal((byte)200, read.Edits[8].Height);
        Assert.Equal(0.5f, read.Edits[9].SecondValue);
    }

    [Fact]
    public void NewCommandIntentsRoundTrip()
    {
        CommandIntentMessage attack = RoundTrip(CommandIntentMessage.InteractAgent(300, new NetVector3(1, 2, 3)));
        Assert.Equal((CommandIntentKind.Interact, CommandTargetKind.Agent, 300, 2f), (attack.Kind, attack.TargetKind, attack.RigidNetId, attack.Point.Y));
        Assert.Equal(CommandTargetKind.BuildPlan, RoundTrip(CommandIntentMessage.InteractBuildPlan(new NetVector3(4, 5, 6))).TargetKind);
        Assert.Equal(CommandTargetKind.Station, RoundTrip(CommandIntentMessage.InteractStation(new NetVector3(4, 5, 6))).TargetKind);
        CommandIntentMessage lockKid = RoundTrip(CommandIntentMessage.ToggleCommandLock(42));
        Assert.Equal((CommandIntentKind.CommandLock, 42), (lockKid.Kind, lockKid.RigidNetId));
    }

    [Fact]
    public void PickupSweepStartAndStopRoundTripTargetIdentity()
    {
        CommandIntentMessage start = RoundTrip(CommandIntentMessage.PickupSweep(271, started: true));
        CommandIntentMessage stop = RoundTrip(CommandIntentMessage.PickupSweep(271, started: false));

        Assert.Equal((CommandIntentKind.PickupSweepStarted, CommandTargetKind.Rigid, 271), (start.Kind, start.TargetKind, start.RigidNetId));
        Assert.Equal((CommandIntentKind.PickupSweepStopped, CommandTargetKind.None, -1), (stop.Kind, stop.TargetKind, stop.RigidNetId));
    }

    [Fact]
    public void PickupSweepAddsEachNewTargetOnlyOnceUntilStopped()
    {
        var sweep = new PickupSelectionSweep();

        Assert.True(sweep.Begin(20, alreadySelected: false));
        Assert.False(sweep.TryVisit(20, alreadySelected: true));
        Assert.True(sweep.TryVisit(21, alreadySelected: false));
        Assert.False(sweep.TryVisit(21, alreadySelected: true));
        sweep.Stop();
        Assert.False(sweep.TryVisit(22, alreadySelected: false));
    }

    [Fact]
    public void PickupSweepRemovesEachPreviouslySelectedTargetOnlyOnce()
    {
        var sweep = new PickupSelectionSweep();

        Assert.True(sweep.Begin(30, alreadySelected: true));
        Assert.True(sweep.TryVisit(31, alreadySelected: true));
        Assert.False(sweep.TryVisit(32, alreadySelected: false));
        Assert.False(sweep.TryVisit(31, alreadySelected: true));
    }

    [Fact]
    public void AgentVisualMessagesRoundTrip()
    {
        AgentInfoMessage info = RoundTrip(new AgentInfoMessage(new[] { new AgentInfoEntry(12, 0xDEADBEEF) }));
        Assert.Equal((12, 0xDEADBEEFu), ((int)info.Agents[0].HostStableIndex, info.Agents[0].Info));
        var play = new AnimationEvent(AnimationEventKind.PlayWithinDistance, new ushort[] { 3, 4 }, 7, 2, -1, true, -1, false, new NetVector3(1, 2, 3), 4.5f);
        AnimationEvent read = RoundTrip(new AnimationEventsMessage(new[] { play })).Events[0];
        Assert.Equal((AnimationEventKind.PlayWithinDistance, 7, 2, -1, true, 4.5f), (read.Kind, read.Primary, read.Secondary, read.MotionSetFilter, read.OnlyWhenIdling, read.Radius));
        Assert.Equal(new ushort[] { 3, 4 }, read.HostAgents);
    }

    [Fact]
    public void PuppetInfoDropsNavigationBits()
    {
        Assert.Equal(0xABCDE0FFu, AgentInfoBits.ForPuppet(0xABCDE123));
        Assert.Equal(AgentInfoBits.Visual(0x00080001), AgentInfoBits.Visual(0x00080FFE));
    }

    [Fact]
    public void WeatherWaterNameEditsRoundTrip()
    {
        WorldEditMessage read = RoundTrip(new WorldEditMessage(new[]
        {
            WorldEdit.WaterSpawn(5, -6, 0.25f),
            WorldEdit.AgentName(77, "Zosia"),
            WorldEdit.Weather(true, false, true),
            WorldEdit.Lightning(new NetVector3(1, 2, 3)),
        }));
        Assert.Equal((5, -6, 0.25f), (read.Edits[0].CellX, read.Edits[0].CellZ, read.Edits[0].Value));
        Assert.Equal((77, "Zosia"), (read.Edits[1].Id, read.Edits[1].Text));
        Assert.Equal((true, false, true), (read.Edits[2].Raining, read.Edits[2].LightningOn, read.Edits[2].FloodsOn));
        Assert.Equal(3f, read.Edits[3].Position.Z);
    }
}

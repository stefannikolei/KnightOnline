using System.Numerics;
using OpenKO.Core.Protocol;
using OpenKO.Network;
using OpenKO.Servers.AIServer.Ai;
using Xunit;

namespace OpenKO.Servers.Tests;

/// <summary>
/// Tests for the pure/deterministic pieces of the CNpc state-machine port
/// (Ai/Npc.State.cs). Expected values are computed by hand from the C++
/// (Server/AIServer/Npc.cpp).
/// </summary>
public class NpcStateTests
{
    private const int NpcBand = 10000; // NPC_BAND

    // ------------------------------------------------------------------
    // GetDir — 8-direction math (tile-based) + AddX/AddZ side effects.
    // ------------------------------------------------------------------

    [Theory]
    // from (10,10): tile (2,2), in-tile offset (2,2)
    [InlineData(10f, 10f, 20f, 10f, Npc.DirRight, 3f, 2f)]
    [InlineData(10f, 10f, 2f, 10f, Npc.DirLeft, 1f, 2f)]
    [InlineData(10f, 10f, 10f, 20f, Npc.DirDown, 2f, 3f)]
    [InlineData(10f, 10f, 10f, 2f, Npc.DirUp, 2f, 1f)]
    [InlineData(10f, 10f, 20f, 20f, Npc.DirDownRight, 3f, 3f)]
    [InlineData(10f, 10f, 2f, 20f, Npc.DirDownLeft, 1f, 3f)]
    [InlineData(10f, 10f, 20f, 2f, Npc.DirUpRight, 3f, 1f)]
    [InlineData(10f, 10f, 2f, 2f, Npc.DirUpLeft, 1f, 1f)]
    public void GetDir_ComputesEightDirections(
        float x1, float z1, float x2, float z2, int expectedDir, float expectedAddX, float expectedAddZ)
    {
        var npc = new Npc();

        int dir = npc.GetDir(x1, z1, x2, z2);

        Assert.Equal(expectedDir, dir);
        Assert.Equal(expectedAddX, npc.AddX);
        Assert.Equal(expectedAddZ, npc.AddZ);
    }

    [Fact]
    public void GetDir_SameTileIsLeft()
    {
        // deltax == deltay == 0 falls into the deltay==0 branch: x22 > x11 is false → DIR_LEFT.
        var npc = new Npc();
        Assert.Equal(Npc.DirLeft, npc.GetDir(10f, 10f, 10f, 10f));
    }

    // ------------------------------------------------------------------
    // Yaw2D — quadrant handling in radians (__PI = 3.141592654f).
    // ------------------------------------------------------------------

    private const float Pi = 3.141592654f;

    [Theory]
    [InlineData(0f, 1f, 0f)]                        // north: asin(0)
    [InlineData(1f, 0f, Pi / 2f)]                   // east: asin(1)
    [InlineData(0f, -1f, Pi)]                       // south: rad(90) + acos(0)
    [InlineData(-1f, 0f, 3f * Pi / 2f)]             // west: rad(270) + acos(1)
    [InlineData(0.70710678f, 0.70710678f, Pi / 4f)] // north-east: asin(√2/2)
    public void Yaw2D_QuadrantMath(float dirX, float dirZ, float expected)
    {
        Npc.Yaw2D(dirX, dirZ, out float yaw);

        Assert.Equal(expected, yaw, 0.0001f);
    }

    // ------------------------------------------------------------------
    // Vector helpers.
    // ------------------------------------------------------------------

    [Fact]
    public void GetVectorPosition_MovesTowardsDestination()
    {
        Vector3 result = Npc.GetVectorPosition(new Vector3(0, 0, 0), new Vector3(10, 0, 0), 4f);

        Assert.Equal(4f, result.X, 0.0001f);
        Assert.Equal(0f, result.Y);
        Assert.Equal(0f, result.Z);
    }

    [Fact]
    public void CalcAdaptivePosition_BacksOffFromDestination()
    {
        // fAttackDistance from vPosDest back towards vPosOrig.
        Vector3 result = Npc.CalcAdaptivePosition(new Vector3(0, 0, 0), new Vector3(10, 0, 0), 2f);

        Assert.Equal(8f, result.X, 0.0001f);
        Assert.Equal(0f, result.Z);
    }

    [Theory]
    [InlineData(0f, 100f, 102f)]   // 0°: (sin 0, cos 0) = (0, 1) → +Z
    [InlineData(90f, 102f, 100f)]  // 90°: (1, 0) → +X
    [InlineData(180f, 100f, 98f)]  // 180°: (0, -1) → -Z
    [InlineData(270f, 98f, 100f)]  // 270°: (-1, 0) → -X
    public void ComputeDestPos_RotatesUnitZAroundY(float degree, float expectedX, float expectedZ)
    {
        Vector3 result = Npc.ComputeDestPos(new Vector3(100, 0, 100), 0f, degree, 2f);

        Assert.Equal(expectedX, result.X, 0.001f);
        Assert.Equal(expectedZ, result.Z, 0.001f);
    }

    [Fact]
    public void GetDistance_Euclidean()
    {
        Assert.Equal(5f, Npc.GetDistance(new Vector3(0, 0, 0), new Vector3(3, 0, 4)));
    }

    [Fact]
    public void GetDirection_Normalizes()
    {
        Vector3 dir = Npc.GetDirection(new Vector3(0, 0, 0), new Vector3(0, 0, 8));

        Assert.Equal(0f, dir.X);
        Assert.Equal(1f, dir.Z, 0.0001f);
    }

    // ------------------------------------------------------------------
    // IsInRange / IsInPathRange / GetMyField.
    // ------------------------------------------------------------------

    [Fact]
    public void IsInRange_UsesCompareSemantics()
    {
        var npc = new Npc
        {
            LimitMinX = 100,
            LimitMaxX = 200,
            LimitMinZ = 100,
            LimitMaxZ = 200,
        };

        Assert.True(npc.IsInRange(150, 150));
        Assert.True(npc.IsInRange(100, 100));   // COMPARE is min-inclusive
        Assert.False(npc.IsInRange(200, 150));  // ... and max-exclusive
        Assert.False(npc.IsInRange(99, 150));
        Assert.False(npc.IsInRange(150, 250));
    }

    [Fact]
    public void IsInRange_HandlesSwappedLimits()
    {
        var npc = new Npc
        {
            LimitMinX = 200,
            LimitMaxX = 100,
            LimitMinZ = 200,
            LimitMaxZ = 100,
        };

        Assert.True(npc.IsInRange(150, 150));
        Assert.False(npc.IsInRange(250, 150));
    }

    [Fact]
    public void IsInPathRange_TruncatedDistanceAgainst41()
    {
        var npc = new Npc { PathCount = 0, CurY = 0 };
        npc.PathList[0].X = 100;
        npc.PathList[0].Z = 100;

        npc.CurX = 141; // distance 41 → (int)41 <= 41
        npc.CurZ = 100;
        Assert.True(npc.IsInPathRange());

        npc.CurX = 142; // distance 42 → false
        Assert.False(npc.IsInPathRange());

        npc.PathCount = -1;
        Assert.False(npc.IsInPathRange());
    }

    [Fact]
    public void GetMyField_QuadrantsOfTheRegion()
    {
        // Region (2,2) spans [96,144); half size 24 → quadrant split at 120.
        var npc = new Npc { RegionX = 2, RegionZ = 2 };

        npc.CurX = 100;
        npc.CurZ = 100;
        Assert.Equal(1, npc.GetMyField());

        npc.CurX = 125;
        Assert.Equal(2, npc.GetMyField());

        npc.CurX = 100;
        npc.CurZ = 125;
        Assert.Equal(3, npc.GetMyField());

        npc.CurX = 125;
        Assert.Equal(4, npc.GetMyField());

        npc.CurX = 200; // outside the region
        Assert.Equal(0, npc.GetMyField());
    }

    // ------------------------------------------------------------------
    // StepMove interpolation with a hand-built path.
    // ------------------------------------------------------------------

    private static Npc MakeMovingNpc()
    {
        var npc = new Npc();
        npc.ClearPathFindData();
        npc.State = NpcState.Moving;
        npc.SecForMeter = 4f;
        return npc;
    }

    [Fact]
    public void StepMove_FirstStepInterpolatesTowardsWaypoint()
    {
        Npc npc = MakeMovingNpc();
        npc.CurX = 0;
        npc.CurZ = 0;
        npc.EndPointX = 8;
        npc.EndPointY = 0;
        npc.AniFrameIndex = 2;
        npc.Points[0].XPos = 8;
        npc.Points[0].ZPos = 0;
        npc.Points[1].XPos = 8;
        npc.Points[1].ZPos = 0;

        Assert.True(npc.StepMove(1));

        Assert.Equal(4f, npc.PrevX, 0.0001f); // one SecForMeter towards (8,0)
        Assert.Equal(0f, npc.PrevZ, 0.0001f);
        Assert.Equal(1, npc.StepCount);
        Assert.Equal(4f, npc.SecForRealMoveMeter, 0.0001f);
        Assert.False(npc.IsMovingEnd());
    }

    [Fact]
    public void StepMove_FinalStepSnapsToEndPoint()
    {
        Npc npc = MakeMovingNpc();
        npc.CurX = 6;
        npc.CurZ = 0;
        npc.EndPointX = 8;
        npc.EndPointY = 0;
        npc.AniFrameIndex = 1;
        npc.Points[0].XPos = 8;   // 2m away → less than SecForMeter
        npc.Points[0].ZPos = 0;
        npc.Points[1].XPos = 8.5f; // final frame within SecForMeter → snap
        npc.Points[1].ZPos = 0;

        Assert.True(npc.StepMove(1));

        Assert.Equal(8f, npc.PrevX);
        Assert.Equal(0f, npc.PrevZ);
        Assert.True(npc.IsMovingEnd());
        Assert.Equal(0, npc.AniFrameCount); // IsMovingEnd resets the frame counter
    }

    [Fact]
    public void StepMove_NegativeWaypointFallsBackToEndPoint()
    {
        Npc npc = MakeMovingNpc();
        npc.CurX = 0;
        npc.CurZ = 0;
        npc.EndPointX = 12;
        npc.EndPointY = 34;
        npc.AniFrameIndex = 1;
        // Points cleared to (-1,-1) by ClearPathFindData → safety branch.

        Assert.False(npc.StepMove(1));
        Assert.Equal(12f, npc.PrevX);
        Assert.Equal(34f, npc.PrevZ);
    }

    [Fact]
    public void StepMove_RequiresMovingState()
    {
        Npc npc = MakeMovingNpc();
        npc.State = NpcState.Standing;

        Assert.False(npc.StepMove(1));
    }

    [Fact]
    public void StepNoPathMove_WalksWaypointList()
    {
        Npc npc = MakeMovingNpc();
        npc.CurX = 0;
        npc.CurZ = 0;
        npc.EndPointX = 8;
        npc.EndPointY = 0;
        npc.AniFrameIndex = 2;
        npc.Points[0].XPos = 4;
        npc.Points[0].ZPos = 0;
        npc.Points[1].XPos = 8;
        npc.Points[1].ZPos = 0;

        Assert.True(npc.StepNoPathMove(1));
        Assert.Equal(4f, npc.PrevX);
        Assert.Equal(1, npc.StepCount);
        Assert.Equal(4f, npc.SecForRealMoveMeter, 0.0001f);

        // Beyond the frame index the move reports failure like the C++.
        npc.StepCount = 2;
        Assert.False(npc.StepNoPathMove(1));
    }

    // ------------------------------------------------------------------
    // RunPathFind (CNpc::PathFind) — pure branches.
    // ------------------------------------------------------------------

    [Fact]
    public void RunPathFind_SameTileIsDirectMove()
    {
        var npc = new Npc();
        npc.EndPointX = 5.5f;
        npc.EndPointY = 6.5f;

        int result = npc.RunPathFind((2, 3), (2, 3), 4f);

        Assert.Equal(1, result);
        Assert.True(npc.PathFlag);
        Assert.Equal(1, npc.AniFrameIndex);
        Assert.Equal(5.5f, npc.Points[0].XPos);
        Assert.Equal(6.5f, npc.Points[0].ZPos);
    }

    [Fact]
    public void RunPathFind_NegativeCoordsFail()
    {
        var npc = new Npc();

        Assert.Equal(-1, npc.RunPathFind((-1, 0), (2, 3), 4f));
        Assert.Equal(-1, npc.RunPathFind((0, 0), (-2, 3), 4f));
    }

    // ------------------------------------------------------------------
    // Tick dispatch honoring Delay/DelayTime.
    // ------------------------------------------------------------------

    [Fact]
    public void Tick_WaitsUntilDelayElapsed()
    {
        double now = 100.0;
        var world = new AiWorld { Clock = () => now };
        var npc = new Npc
        {
            Nid = 1,
            World = world,
            State = NpcState.Sleeping,
            FirstLive = false,
            Delay = 1000,
            DelayTime = 100.0,
            HpChangeTime = 100.0,
        };

        npc.Tick(100.5); // 500ms elapsed < 1000ms delay → no dispatch
        Assert.Equal(NpcState.Sleeping, npc.State);
        Assert.Equal(1000, npc.Delay);

        now = 101.1;
        npc.Tick(101.1); // 1100ms > 1000ms → NpcSleeping; default night mode is day
        Assert.Equal(NpcState.Standing, npc.State);
        Assert.Equal(0, npc.Delay);
        Assert.Equal(101.1, npc.DelayTime);
    }

    [Fact]
    public void Tick_ZeroDelayDispatchesImmediately()
    {
        var world = new AiWorld { Clock = () => 50.0 };
        var npc = new Npc
        {
            Nid = 1,
            World = world,
            State = NpcState.Dead,
            FirstLive = false,
            Delay = 0, // Delay == 0 always dispatches
            DelayTime = 50.0,
            HpChangeTime = 50.0,
        };

        npc.Tick(50.0);

        Assert.Equal(NpcState.Live, npc.State); // NPC_DEAD → NPC_LIVE in the dispatcher
    }

    [Fact]
    public void Tick_FirstLiveBypassesDelay()
    {
        var world = new AiWorld { Clock = () => 10.0 };
        var npc = new Npc
        {
            Nid = 1,
            World = world,
            State = NpcState.Sleeping,
            Delay = 100000,
            DelayTime = 10.0,
            HpChangeTime = 10.0,
        };
        Assert.True(npc.FirstLive); // default

        npc.Tick(10.0);

        Assert.Equal(NpcState.Standing, npc.State);
    }

    [Fact]
    public void Tick_NegativeNidDoesNothing()
    {
        var world = new AiWorld { Clock = () => 10.0 };
        var npc = new Npc
        {
            Nid = -1,
            World = world,
            State = NpcState.Dead,
            FirstLive = false,
            Delay = 0,
        };

        npc.Tick(10.0);

        Assert.Equal(NpcState.Dead, npc.State);
    }

    // ------------------------------------------------------------------
    // FillNpcInfo — byte-exact layout per the C++ field order.
    // ------------------------------------------------------------------

    private static void AddShort(List<byte> bytes, short value) => bytes.AddRange(BitConverter.GetBytes(value));

    private static void AddInt(List<byte> bytes, int value) => bytes.AddRange(BitConverter.GetBytes(value));

    private static void AddFloat(List<byte> bytes, float value) => bytes.AddRange(BitConverter.GetBytes(value));

    [Fact]
    public void FillNpcInfo_MatchesCppByteLayout()
    {
        var npc = new Npc
        {
            Nid = 5,
            Sid = 601,
            Pid = 634,
            Size = 100,
            Weapon1 = 111,
            Weapon2 = 222,
            CurZone = 1,
            ZoneIndex = 0,
            Name = "Orc",
            Group = 3,
            Level = 12,
            CurX = 100.5f,
            CurZ = 200.25f,
            CurY = 7f,
            Direction = 2,
            HP = 300,
            MaxHP = 400,
            NpcType = 0,
            SellingGroup = 255,
            GateOpen = 1,
            HitRate = 90,
            ObjectType = 0,
            TrapNumber = 9,
        };

        var buf = new byte[256];
        var w = new PacketWriter(buf);
        npc.FillNpcInfo(ref w, 1);

        var expected = new List<byte>
        {
            AiOpcode.AG_NPC_INFO,
            1, // register in region (not a SpecialType-5 hidden monster)
        };
        AddShort(expected, (short)(5 + NpcBand)); // nid + NPC_BAND
        AddShort(expected, 601);                  // sid
        AddShort(expected, 634);                  // pid
        AddShort(expected, 100);                  // size
        AddInt(expected, 111);                    // weapon 1
        AddInt(expected, 222);                    // weapon 2
        AddShort(expected, 1);                    // cur zone
        AddShort(expected, 0);                    // zone index
        expected.Add(3);                          // name length (SetVarString)
        expected.AddRange("Orc"u8.ToArray());
        expected.Add(3);                          // group
        expected.Add(12);                         // level (byte cast)
        AddFloat(expected, 100.5f);               // x
        AddFloat(expected, 200.25f);              // z
        AddFloat(expected, 7f);                   // y
        expected.Add(2);                          // direction
        expected.Add(1);                          // alive flag (hp > 0)
        expected.Add(0);                          // npc type
        AddInt(expected, 255);                    // selling group
        AddInt(expected, 400);                    // max hp (dword)
        AddInt(expected, 300);                    // hp (dword)
        expected.Add(1);                          // gate open
        AddShort(expected, 90);                   // hit rate
        expected.Add(0);                          // object type
        expected.Add(9);                          // trap number

        Assert.Equal(expected.ToArray(), w.Written.ToArray());
    }

    [Fact]
    public void FillNpcInfo_HiddenSpecialMonsterIsNotRegistered()
    {
        var npc = new Npc { SpecialType = 5, ChangeType = 0, Name = string.Empty };

        var buf = new byte[256];
        var w = new PacketWriter(buf);
        npc.FillNpcInfo(ref w, 1);

        Assert.Equal(AiOpcode.AG_NPC_INFO, buf[0]);
        Assert.Equal(0, buf[1]); // "don't register in the region"
    }

    [Fact]
    public void SendNpcInfoAll_IsFillNpcInfoWithoutOpcode()
    {
        var npc = new Npc { Nid = 7, Name = "Bandit", HP = 10, MaxHP = 10 };

        var infoBuf = new byte[256];
        var infoW = new PacketWriter(infoBuf);
        npc.FillNpcInfo(ref infoW, 1);

        var allBuf = new byte[256];
        var allW = new PacketWriter(allBuf);
        npc.SendNpcInfoAll(ref allW, 1);

        // Same layout minus the opcode byte and the alive flag (byte after Direction).
        byte[] info = infoW.Written.ToArray();
        byte[] all = allW.Written.ToArray();
        Assert.Equal(info.Length - 2, all.Length);
        Assert.Equal(info[1..2], all[0..1]); // region flag lines up after dropping the opcode
    }

    // ------------------------------------------------------------------
    // HpChange — 10s regen + AG_USER_SET_HP payload.
    // ------------------------------------------------------------------

    [Fact]
    public void HpChange_RegeneratesAndBroadcasts()
    {
        var world = new AiWorld { Clock = () => 10.0 };
        byte[]? sent = null;
        var npc = new Npc
        {
            Nid = 2,
            World = world,
            State = NpcState.Standing,
            MaxHP = 100,
            HP = 50,
            SendToZone = p =>
            {
                sent = p;
                return ValueTask.CompletedTask;
            },
        };

        npc.HpChange();

        Assert.Equal(55, npc.HP); // + MaxHP / 20
        Assert.Equal(10.0, npc.HpChangeTime);
        Assert.NotNull(sent);
        Assert.Equal(11, sent!.Length);
        Assert.Equal(AiOpcode.AG_USER_SET_HP, sent[0]);
        Assert.Equal((short)(2 + NpcBand), BitConverter.ToInt16(sent, 1));
        Assert.Equal(55u, BitConverter.ToUInt32(sent, 3));
        Assert.Equal(100u, BitConverter.ToUInt32(sent, 7));
    }

    [Fact]
    public void HpChange_NoRegenAtFullOrZeroHp()
    {
        var world = new AiWorld { Clock = () => 10.0 };
        bool sentAny = false;
        var npc = new Npc
        {
            Nid = 2,
            World = world,
            State = NpcState.Standing,
            MaxHP = 100,
            HP = 100,
            SendToZone = _ =>
            {
                sentAny = true;
                return ValueTask.CompletedTask;
            },
        };

        npc.HpChange();
        Assert.Equal(100, npc.HP);

        npc.HP = 0;
        npc.HpChange();
        Assert.Equal(0, npc.HP);

        Assert.False(sentAny);
    }
}

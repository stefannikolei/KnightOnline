using System.Numerics;
using System.Text;
using OpenKO.Core.Protocol;
using OpenKO.GameData.Maps;
using OpenKO.GameData.Math;
using OpenKO.Network;

namespace OpenKO.Servers.AIServer.Ai;

/// <summary>_NpcPosition (Server/AIServer/Define.h): one waypoint of the active path.</summary>
public struct NpcPosition
{
    public byte Type;      // byType
    public byte Speed;     // bySpeed
    public int PointX;     // pPoint.x (tile)
    public int PointY;     // pPoint.y (tile)
    public float XPos;     // fXPos (world)
    public float ZPos;     // fZPos (world)
}

/// <summary>
/// Port of the CNpc state machine, movement and pathing methods (Server/AIServer/Npc.cpp)
/// plus the per-NPC body of CNpcThread::thread_loop (NpcThread.cpp) as <see cref="Tick"/>.
/// Combat methods that belong to stage 3.7 part 2 are declared as stubs at the bottom
/// returning the C++ "no action" values so the movement call sites stay faithful.
/// </summary>
public partial class Npc
{
    // ---- Define.h / Npc.cpp constants ----
    private const int TileSize = 4;              // TILE_SIZE
    private const int UserBand = 0;              // USER_BAND
    private const int NpcBand = 10000;           // NPC_BAND
    private const int InvalidBand = 20000;       // INVALID_BAND
    private const int NpcMaxMoveRange = 100;     // NPC_MAX_MOVE_RANGE
    private const int LongAttackRange = 30;      // LONG_ATTACK_RANGE
    private const int ShortAttackRange = 3;      // SHORT_ATTACK_RANGE
    private const int FaintingDuration = 2;      // FAINTING_TIME
    private const byte NoAction = 0;             // NO_ACTION
    private const byte AttackToTrace = 1;        // ATTACK_TO_TRACE
    private const byte KarusMan = 1;             // KARUS_MAN
    private const byte InfoModify = 1;           // INFO_MODIFY (Packet.h)
    private const byte PacketSuccess = 0x02;     // SUCCESS
    private const byte BattlezoneOpen = 0x00;    // BATTLEZONE_OPEN
    private const byte BattlezoneClose = 0x01;   // BATTLEZONE_CLOSE
    private const byte NpcIn = 1;                // NPC_IN
    private const byte NpcOut = 2;               // NPC_OUT
    private const byte AttackSuccessResult = 1;  // ATTACK_SUCCESS (globals.h)
    private const byte MagicAttackTargetDead = 4; // MAGIC_ATTACK_TARGET_DEAD
    private const byte DurationAttack = 3;       // DURATION_ATTACK
    private const int MaxDungeonBossMonster = 20; // MAX_DUNGEON_BOSS_MONSTER

    // e_NpcType values referenced by the state machine (shared/globals.h).
    private const byte NpcTypeMonster = 0;          // NPCTYPE_MONSTER
    private const byte NpcTypeDungeonMonster = 4;   // NPC_DUNGEON_MONSTER
    private const byte NpcTypeGuard = 11;           // NPC_GUARD
    private const byte NpcTypePatrolGuard = 12;     // NPC_PATROL_GUARD
    private const byte NpcTypeStoreGuard = 13;      // NPC_STORE_GUARD
    private const byte NpcTypeHealer = 40;          // NPC_HEALER
    private const byte NpcTypeDoor = 50;            // NPC_DOOR / NPC_GATE
    private const byte NpcTypePhoenixGate = 51;     // NPC_PHOENIX_GATE
    private const byte NpcTypeSpecialGate = 52;     // NPC_SPECIAL_GATE
    private const byte NpcTypeGateLever = 55;       // NPC_GATE_LEVER
    private const byte NpcTypeArtifact = 60;        // NPC_ARTIFACT
    private const byte NpcTypeDestroyArtifact = 61; // NPC_DESTORY_ARTIFACT
    private const byte NpcTypeDomesticAnimal = 99;  // NPC_DOMESTIC_ANIMAL

    // 8-direction constants (Define.h).
    public const int DirDown = 0;
    public const int DirDownLeft = 1;
    public const int DirLeft = 2;
    public const int DirUpLeft = 3;
    public const int DirUp = 4;
    public const int DirUpRight = 5;
    public const int DirRight = 6;
    public const int DirDownRight = 7;

    private const float Pi = 3.141592654f; // __PI (MathUtils.h)

    // ---- m_pMain replacements ----

    /// <summary>World state (replaces the m_pMain AIServerApp pointer).</summary>
    public AiWorld? World;

    /// <summary>Replaces SendAll's zone-socket send (AIServerApp::Send to m_sCurZone).</summary>
    public Func<byte[], ValueTask>? SendToZone;

    /// <summary>AIServerApp::_nightMode (1 = day). Wired by the host; defaults to day.</summary>
    public Func<byte>? GetNightMode;

    /// <summary>AIServerApp::_battleEventType. Wired by the host; defaults to closed.</summary>
    public Func<byte>? GetBattleEventType;

    /// <summary>
    /// Called once from <see cref="SetLive"/> when the NPC first comes alive; the C++
    /// increments _loadedNpcCount and starts the game-server accept thread when all
    /// NPCs are initialized.
    /// </summary>
    public Action? OnFirstLive;

    /// <summary>m_pPoint[MAX_PATH_LINE]: waypoints of the active move.</summary>
    public readonly NpcPosition[] Points = new NpcPosition[AiConstants.MaxPathLine];

    // ---- helpers replacing globals ----

    private double TimeGet() => World?.Clock() ?? 0.0;

    private int MyRand(int min, int max) => World?.Rand(min, max) ?? min;

    private AiZone? GetMapByIndex()
        => World is { } w && ZoneIndex >= 0 && ZoneIndex < w.Zones.Count ? w.Zones[ZoneIndex] : null;

    /// <summary>AIServerApp::GetUserPtr.</summary>
    private AiUser? GetUserPtr(int nid)
    {
        if (World is null || nid < 0 || nid >= AiConstants.MaxUser)
            return null;

        AiUser? user = World.Users[nid];
        if (user is null)
            return null;

        if (user.Uid < 0 || user.Uid >= AiConstants.MaxUser)
            return null;

        return user.Uid == nid ? user : null;
    }

    private Npc? GetNpcPtr(int nid) => World?.Npcs.GetValueOrDefault(nid);

    private static bool Compare(int x, int min, int max) => x >= min && x < max; // COMPARE macro

    private static bool IsPointInRect(int px, int py, int left, int top, int right, int bottom)
        => px >= left && px <= right && py >= top && py <= bottom; // IsPointInRect (inclusive)

    private static float DegreesToRadians(float degrees) => degrees * (Pi / 180.0f);

    /// <summary>MAP::IsValidPosition.</summary>
    private static bool IsValidPosition(GameMap map, float x, float z)
    {
        int mapMaxX = (int)((map.MapSize - 1) * map.UnitDistance);
        int mapMaxZ = (int)((map.MapSize - 1) * map.UnitDistance);

        if (x < 0 || x > mapMaxX)
            return false;

        if (z < 0 || z > mapMaxZ)
            return false;

        return true;
    }

    private bool IsGateFamilyNpc()
        => NpcType is NpcTypeDoor or NpcTypeArtifact or NpcTypePhoenixGate or NpcTypeGateLever
            or NpcTypeDomesticAnimal or NpcTypeSpecialGate or NpcTypeDestroyArtifact;

    /// <summary>CNpc::SendAll — send on the NPC's zone socket (fire and forget).</summary>
    public void SendAll(ReadOnlySpan<byte> buf)
    {
        if (SendToZone is { } send)
            _ = send(buf.ToArray());
    }

    /// <summary>CNpc::NpcTrace — trace logging only, disabled by default in the C++.</summary>
    public void NpcTrace(string msg)
    {
        // useNpcTrace is false in the C++; no-op.
        _ = msg;
    }

    // ------------------------------------------------------------------
    //  Init / reset
    // ------------------------------------------------------------------

    /// <summary>CNpc::SetUid — region bookkeeping when the NPC moves.</summary>
    public bool SetUid(float x, float z, int id)
    {
        AiZone? zone = GetMapByIndex();
        if (zone is null)
            return false;

        int x1 = (int)x / TileSize;
        int z1 = (int)z / TileSize;
        int nRX = (int)x / AiConstants.ViewDistance;
        int nRZ = (int)z / AiConstants.ViewDistance;

        if (x1 < 0 || z1 < 0 || x1 > zone.Map.MapSize || z1 > zone.Map.MapSize)
            return false;

        if (nRX > zone.RegionsX - 1 || nRZ > zone.RegionsZ - 1 || nRX < 0 || nRZ < 0)
            return false;

        if (RegionX != nRX || RegionZ != nRZ)
        {
            int oldRX = RegionX;
            int oldRZ = RegionZ;
            RegionX = (short)nRX;
            RegionZ = (short)nRZ;

            Npc? npc = GetNpcPtr(id - NpcBand);
            if (npc is null)
                return false;

            zone.RegionNpcAdd(RegionX, RegionZ, id);
            zone.RegionNpcRemove(oldRX, oldRZ, id);
        }

        return true;
    }

    /// <summary>CNpc::Init.</summary>
    public void Init()
    {
        if (ZoneIndex == -1)
            ZoneIndex = (short)(World?.GetZoneIndex(CurZone) ?? -1);

        Delay = 0;
        DelayTime = TimeGet();

        // The C++ caches pMap->m_pMap into m_pOrgMap here; the port reads
        // Zone.Map.TileEvents directly wherever m_pOrgMap was used.
        _ = GetMapByIndex();
    }

    /// <summary>CNpc::InitTarget.</summary>
    public void InitTarget()
    {
        if (AttackPos != 0)
        {
            if (Target.Id >= 0 && Target.Id < NpcBand)
            {
                AiUser? user = GetUserPtr(Target.Id);
                if (user is not null && AttackPos > 0 && AttackPos < 9)
                    user.SurroundNpcNumber[AttackPos - 1] = -1;
            }
        }

        AttackPos = 0;
        Target.Id = -1;
        Target.X = 0.0f;
        Target.Y = 0.0f;
        Target.Z = 0.0f;
        Target.FailCount = 0;
    }

    /// <summary>CNpc::InitUserList.</summary>
    public void InitUserList()
    {
        MaxDamageUserId = -1;
        TotalDamage = 0;

        for (int i = 0; i < AiConstants.NpcMaxUserList; i++)
        {
            DamagedUserList[i].InSight = false;
            DamagedUserList[i].Uid = -1;
            DamagedUserList[i].Damage = 0;
            DamagedUserList[i].UserId = string.Empty;
        }
    }

    /// <summary>CNpc::InitPos — formation offset for path-following NPCs.</summary>
    public void InitPos()
    {
        const float fDD = 1.5f;
        if (BattlePos == 0)
        {
            BattlePosX = 0.0f;
            BattlePosZ = 0.0f;
            return;
        }

        int idx = PathCounter - 1;
        if (idx < 0 || idx > 4)
            return; // guard: the C++ indexes fx[m_byPathCount - 1] out of bounds here (UB)

        if (BattlePos == 1)
        {
            float[] fx = [0.0f, -(fDD * 2), -(fDD * 2), -(fDD * 4), -(fDD * 4)];
            float[] fz = [0.0f, fDD * 1, -(fDD * 1), fDD * 1, -(fDD * 1)];
            BattlePosX = fx[idx];
            BattlePosZ = fz[idx];
        }
        else if (BattlePos == 2)
        {
            float[] fx = [0.0f, 0.0f, -(fDD * 2), -(fDD * 2), -(fDD * 2)];
            float[] fz = [0.0f, -(fDD * 2), fDD * 1, fDD * 1, fDD * 3];
            BattlePosX = fx[idx];
            BattlePosZ = fz[idx];
        }
        else if (BattlePos == 3)
        {
            float[] fx = [0.0f, -(fDD * 2), -(fDD * 2), -(fDD * 2), -(fDD * 4)];
            float[] fz = [0.0f, fDD * 2, 0.0f, -(fDD * 2), 0.0f];
            BattlePosX = fx[idx];
            BattlePosZ = fz[idx];
        }
    }

    /// <summary>CNpc::InitMagicValuable.</summary>
    public void InitMagicValuable()
    {
        for (int i = 0; i < AiConstants.MaxMagicType4; i++)
        {
            MagicType4[i].Amount = 100;
            MagicType4[i].DurationTime = 0;
            MagicType4[i].StartTime = 0.0;
        }

        for (int i = 0; i < AiConstants.MaxMagicType3; i++)
        {
            MagicType3[i].AttackUserId = -1;
            MagicType3[i].HpAmount = 0;
            MagicType3[i].Duration = 0;
            MagicType3[i].Interval = 2;
            MagicType3[i].StartTime = 0.0;
        }
    }

    /// <summary>CNpc::ClearPathFindData.</summary>
    public void ClearPathFindData()
    {
        Array.Clear(PathMap);

        PathFlag = false;
        StepCount = 0;
        AniFrameCount = 0;
        AniFrameIndex = 0;
        AddX = 0.0f;
        AddZ = 0.0f;

        for (int i = 0; i < AiConstants.MaxPathLine; i++)
        {
            Points[i].Type = 0;
            Points[i].Speed = 0;
            Points[i].XPos = -1.0f;
            Points[i].ZPos = -1.0f;
        }
    }

    /// <summary>CNpc::NpcTypeParser — attack disposition from ActType.</summary>
    public void NpcTypeParser()
    {
        switch (ActType)
        {
            case 1:
                AttType = OldAttType = 0;
                break;

            case 2:
                AttType = OldAttType = 0;
                EndAttType = 0;
                break;

            case 3:
                GroupType = 1;
                AttType = OldAttType = 0;
                break;

            case 4:
                GroupType = 1;
                AttType = OldAttType = 0;
                EndAttType = 0;
                break;

            case 6:
                EndAttType = 0;
                break;

            case 5:
            case 7:
                AttType = OldAttType = 1;
                break;

            default:
                AttType = OldAttType = 1;
                break;
        }
    }

    /// <summary>CNpc::SetLive — (re)spawn handling.</summary>
    public bool SetLive()
    {
        HP = MaxHP;
        MP = MaxMP;
        PathCount = 0;
        PatternFrame = 0;
        ResetFlag = 0;
        ActionFlag = NoAction;
        MaxDamagedNation = KarusMan;

        RegionX = RegionZ = -1;
        AddX = AddZ = 0.0f;
        StartPointX = StartPointY = 0.0f;
        EndPointX = EndPointY = 0.0f;
        MinX = MinY = MaxX = MaxY = 0;

        InitTarget();
        ClearPathFindData();
        InitUserList();

        AiZone? zone = GetMapByIndex();
        if (zone is null)
            return false;

        if (FirstLive)
        {
            InitX = PrevX = CurX;
            InitY = PrevY = CurY;
            InitZ = PrevZ = CurZ;
        }

        if (NpcType != NpcTypeMonster)
        {
            CurX = PrevX = InitX;
            CurY = PrevY = InitY;
            CurZ = PrevZ = InitZ;
        }
        else
        {
            int retryCount = 0;
            const int maxRetry = 500;

            while (true)
            {
                int nX;
                int nRandom = Math.Abs(InitMinX - InitMaxX);
                if (nRandom <= 1)
                    nX = InitMinX;
                else
                    nX = InitMinX < InitMaxX ? MyRand(InitMinX, InitMaxX) : MyRand(InitMaxX, InitMinX);

                int nZ;
                nRandom = Math.Abs(InitMinY - InitMaxY);
                if (nRandom <= 1)
                    nZ = InitMinY;
                else
                    nZ = InitMinY < InitMaxY ? MyRand(InitMinY, InitMaxY) : MyRand(InitMaxY, InitMinY);

                int nTileX = nX / TileSize;
                int nTileZ = nZ / TileSize;

                if (nTileX >= zone.Map.MapSize - 1)
                    nTileX = zone.Map.MapSize - 1;
                if (nTileZ >= zone.Map.MapSize - 1)
                    nTileZ = zone.Map.MapSize - 1;

                if (nTileX < 0 || nTileZ < 0)
                    return false;

                if (zone.Map.TileEvents[nTileX, nTileZ] <= 0)
                {
                    if (retryCount >= maxRetry)
                    {
                        InitX = PrevX = CurX;
                        InitY = PrevY = CurY;
                        InitZ = PrevZ = CurZ;
                        return false;
                    }

                    retryCount++;
                    continue;
                }

                InitX = PrevX = CurX = nX;
                InitZ = PrevZ = CurZ = nZ;
                break;
            }
        }

        HpChangeTime = TimeGet();
        FaintingTime = 0.0;
        InitMagicValuable();

        if (FirstLive)
        {
            NpcTypeParser();
            FirstLive = false;

            // C++: ++_loadedNpcCount and GameServerAcceptThread() when all NPCs loaded.
            OnFirstLive?.Invoke();
        }

        if (MoveType == 3 && MaxPathCount == 2)
        {
            var vS = new Vector3(PathList[0].X, 0, PathList[0].Z);
            var vE = new Vector3(PathList[1].X, 0, PathList[1].Z);
            Vector3 vDir = KoMath.Normalized(vE - vS);
            Yaw2D(vDir.X, vDir.Z, out float fDir);

            Direction = (byte)fDir;
        }

        // Monster that starts out dead and appears later.
        if (SpecialType == 5 && ChangeType == 0)
            return false;

        SetUid(CurX, CurZ, Nid + NpcBand);
        DeadType = 0;

        var buf = new byte[2048];
        var w = new PacketWriter(buf);
        FillNpcInfo(ref w, InfoModify);
        SendAll(w.Written);

        return true;
    }

    // ------------------------------------------------------------------
    //  Per-tick dispatcher (CNpcThread::thread_loop body for one NPC)
    // ------------------------------------------------------------------

    /// <summary>
    /// Per-NPC body of CNpcThread::thread_loop: honors Delay/DelayTime, runs the
    /// 10s HP regen and the duration magics, then dispatches on the state.
    /// </summary>
    public void Tick(double currentTime)
    {
        if (Nid < 0)
            return;

        double fTime3 = currentTime - DelayTime;
        uint dwTickTime = (uint)(fTime3 * 1000);

        if (Delay > (int)dwTickTime && !FirstLive && Delay != 0)
        {
            if (Delay < 0)
                Delay = 0;

            // Enemy spotting while waiting (2002.04.23 load reduction).
            if (State == NpcState.Standing && CheckFindEnemy())
            {
                if (FindEnemy())
                {
                    State = NpcState.Attacking;
                    Delay = 0;
                }
            }

            return;
        }

        fTime3 = currentTime - HpChangeTime;
        dwTickTime = (uint)(fTime3 * 1000);

        // HP regen every 10 seconds.
        if (10000 < dwTickTime)
            HpChange();

        DurationMagic_4(currentTime);
        DurationMagic_3(currentTime);

        switch (State)
        {
            case NpcState.Live:
                NpcLive();
                break;

            case NpcState.Standing:
                NpcStanding();
                break;

            case NpcState.Moving:
                NpcMoving();
                break;

            case NpcState.Attacking:
                NpcAttacking();
                break;

            case NpcState.Tracing:
                NpcTracing();
                break;

            case NpcState.Fighting:
                NpcFighting();
                break;

            case NpcState.Back:
                NpcBack();
                break;

            case NpcState.Strategy:
                break;

            case NpcState.Dead:
                State = NpcState.Live;
                break;

            case NpcState.Sleeping:
                NpcSleeping();
                break;

            case NpcState.Fainting:
                NpcFainting(currentTime);
                break;

            case NpcState.Healing:
                NpcHealing();
                break;
        }
    }

    // ------------------------------------------------------------------
    //  State handlers
    // ------------------------------------------------------------------

    /// <summary>CNpc::NpcLive.</summary>
    public void NpcLive()
    {
        // Dungeon work: keep monsters that must not respawn in NPC_LIVE limbo.
        if (RegenType == 2 || (RegenType == 1 && ChangeType == 100))
        {
            State = NpcState.Live;
            Delay = RegenTime;
            DelayTime = TimeGet();
            return;
        }

        if (ChangeType == 1)
        {
            ChangeType = 2;
            ChangeMonsterInfo(1);
        }

        if (SetLive())
        {
            State = NpcState.Standing;
            Delay = StandTime;
            DelayTime = TimeGet();
        }
        else
        {
            State = NpcState.Live;
            Delay = StandTime;
            DelayTime = TimeGet();
        }
    }

    /// <summary>CNpc::NpcFighting.</summary>
    public void NpcFighting()
    {
        NpcTrace("NpcFighting()");

        if (HP <= 0)
        {
            Dead();
            return;
        }

        Delay = DoAttack();
        DelayTime = TimeGet();
    }

    /// <summary>CNpc::NpcTracing.</summary>
    public void NpcTracing()
    {
        if (StepCount != 0)
        {
            if (!(PrevX < 0 || PrevZ < 0))
            {
                CurX = PrevX;
                CurZ = PrevZ;
            }
        }

        NpcTrace("NpcTracing()");

        // Fixed guards never chase.
        if (IsGateFamilyNpc())
        {
            InitTarget();
            State = NpcState.Standing;
            Delay = StandTime;
            DelayTime = TimeGet();
            return;
        }

        int nFlag = IsCloseTarget(AttackRange, 1);

        // Close enough for melee?
        if (nFlag == 1)
        {
            NpcMoveEnd();
            State = NpcState.Fighting;
            Delay = 0;
            DelayTime = TimeGet();
            return;
        }
        else if (nFlag == -1) // target lost
        {
            InitTarget();
            NpcMoveEnd();
            State = NpcState.Standing;
            Delay = StandTime;
            DelayTime = TimeGet();
            return;
        }
        else if (nFlag == 2 && LongType == 2)
        {
            NpcMoveEnd();
            State = NpcState.Fighting;
            Delay = 0;
            DelayTime = TimeGet();
            return;
        }

        if (ActionFlag == AttackToTrace)
        {
            ActionFlag = NoAction;
            ResetFlag = 1;
        }

        if (ResetFlag == 1)
        {
            if (!ResetPath())
            {
                InitTarget();
                NpcMoveEnd();
                State = NpcState.Standing;
                Delay = StandTime;
                DelayTime = TimeGet();
                return;
            }
        }

        if (!PathFlag)
        {
            if (!StepMove(1))
            {
                State = NpcState.Standing;
                Delay = StandTime;
                DelayTime = TimeGet();
                return;
            }
        }
        else
        {
            if (!StepNoPathMove(1))
            {
                State = NpcState.Standing;
                Delay = StandTime;
                DelayTime = TimeGet();
                return;
            }
        }

        var buf = new byte[1024];
        var w = new PacketWriter(buf);

        if (IsMovingEnd())
        {
            w.SetByte(AiOpcode.MOVE_RESULT);
            w.SetByte(PacketSuccess);
            w.SetShort(Nid + NpcBand);
            w.SetFloat(CurX);
            w.SetFloat(CurZ);
            w.SetFloat(CurY);
            w.SetFloat(0);
            SendAll(w.Written);
        }
        else
        {
            w.SetByte(AiOpcode.MOVE_RESULT);
            w.SetByte(PacketSuccess);
            w.SetShort(Nid + NpcBand);
            w.SetFloat(PrevX);
            w.SetFloat(PrevZ);
            w.SetFloat(PrevY);
            float fMoveSpeed = SecForRealMoveMeter / (Speed / 1000.0f);
            w.SetFloat(fMoveSpeed);
            SendAll(w.Written);
        }

        if (nFlag == 2 && LongType == 0 && NpcType != NpcTypeHealer)
        {
            int nRet = TracingAttack();
            if (nRet == 0)
            {
                InitTarget();
                NpcMoveEnd();
                State = NpcState.Standing;
                Delay = StandTime;
                DelayTime = TimeGet();
                return;
            }
        }

        Delay = Speed;
        DelayTime = TimeGet();
    }

    /// <summary>CNpc::NpcAttacking.</summary>
    public void NpcAttacking()
    {
        NpcTrace("NpcAttacking()");

        if (HP <= 0)
        {
            Dead();
            return;
        }

        int ret = IsCloseTarget(AttackRange);

        if (ret == 1)
        {
            State = NpcState.Fighting;
            Delay = 0;
            DelayTime = TimeGet();
            return;
        }

        if (IsGateFamilyNpc())
        {
            State = NpcState.Standing;
            Delay = StandTime / 2;
            DelayTime = TimeGet();
            return;
        }

        int nValue = GetTargetPath();

        // Target lost or ran away.
        if (nValue == -1)
        {
            if (!RandomMove())
            {
                InitTarget();
                State = NpcState.Standing;
                Delay = StandTime;
                DelayTime = TimeGet();
                return;
            }

            InitTarget();
            State = NpcState.Moving;
            Delay = Speed;
            DelayTime = TimeGet();
            return;
        }
        else if (nValue == 0)
        {
            SecForMeter = Speed2; // run speed when attacking
            IsNoPathFind(SecForMeter);
        }

        State = NpcState.Tracing;
        Delay = 0;
        DelayTime = TimeGet();
    }

    /// <summary>CNpc::NpcMoving.</summary>
    public void NpcMoving()
    {
        NpcTrace("NpcMoving()");

        if (HP <= 0)
        {
            Dead();
            return;
        }

        if (StepCount != 0)
        {
            if (!(PrevX < 0 || PrevZ < 0))
            {
                CurX = PrevX;
                CurZ = PrevZ;
            }
        }

        if (FindEnemy())
        {
            NpcMoveEnd();
            State = NpcState.Attacking;
            Delay = Speed;
            DelayTime = TimeGet();
            return;
        }

        if (IsMovingEnd())
        {
            CurX = PrevX;
            CurZ = PrevZ;

            State = NpcState.Standing;
            Delay = StandTime;
            DelayTime = TimeGet();

            if (Delay < 0)
            {
                Delay = 0;
                DelayTime = TimeGet();
            }

            return;
        }

        if (!PathFlag)
        {
            if (!StepMove(1))
            {
                State = NpcState.Standing;
                Delay = StandTime;
                DelayTime = TimeGet();
                return;
            }
        }
        else
        {
            if (!StepNoPathMove(1))
            {
                State = NpcState.Standing;
                Delay = StandTime;
                DelayTime = TimeGet();
                return;
            }
        }

        var buf = new byte[1024];
        var w = new PacketWriter(buf);

        if (IsMovingEnd())
        {
            w.SetByte(AiOpcode.MOVE_RESULT);
            w.SetByte(PacketSuccess);
            w.SetShort(Nid + NpcBand);
            w.SetFloat(PrevX);
            w.SetFloat(PrevZ);
            w.SetFloat(PrevY);
            w.SetFloat(0);
            SendAll(w.Written);
        }
        else
        {
            w.SetByte(AiOpcode.MOVE_RESULT);
            w.SetByte(PacketSuccess);
            w.SetShort(Nid + NpcBand);
            w.SetFloat(PrevX);
            w.SetFloat(PrevZ);
            w.SetFloat(PrevY);
            float fMoveSpeed = SecForRealMoveMeter / (Speed / 1000.0f);
            w.SetFloat(fMoveSpeed);
            SendAll(w.Written);
        }

        Delay = Speed;
        DelayTime = TimeGet();
    }

    /// <summary>CNpc::NpcStanding.</summary>
    public void NpcStanding()
    {
        NpcTrace("NpcStanding()");

        AiZone? zone = GetMapByIndex();
        if (zone is null)
            return;

        // TODO(stage3.7): RoomEvent (dungeon rooms) not ported yet — the C++ keeps
        // standing while its room's m_byStatus == 1.

        if (RandomMove())
        {
            AniFrameCount = 0;
            State = NpcState.Moving;
            Delay = StandTime;
            DelayTime = TimeGet();
            return;
        }

        State = NpcState.Standing;
        Delay = StandTime;
        DelayTime = TimeGet();

        if (NpcType == NpcTypeSpecialGate
            && (GetBattleEventType?.Invoke() ?? BattlezoneClose) == BattlezoneOpen)
        {
            // The gate toggles open/closed on the standing-time cycle.
            GateOpen = GateOpen == 0 ? (byte)1 : (byte)0;

            var buf = new byte[128];
            var w = new PacketWriter(buf);
            w.SetByte(AiOpcode.AG_NPC_GATE_OPEN);
            w.SetShort(Nid + NpcBand);
            w.SetByte(GateOpen);
            SendAll(w.Written);
        }
    }

    /// <summary>CNpc::NpcBack — keep the target at attack range.</summary>
    public void NpcBack()
    {
        if (Target.Id >= 0 && Target.Id < NpcBand)
        {
            if (GetUserPtr(Target.Id - UserBand) is null)
            {
                State = NpcState.Standing;
                Delay = Speed;
                DelayTime = TimeGet();
                return;
            }
        }
        else if (Target.Id >= NpcBand && Target.Id < InvalidBand)
        {
            if (GetNpcPtr(Target.Id - NpcBand) is null)
            {
                State = NpcState.Standing;
                Delay = Speed;
                DelayTime = TimeGet();
                return;
            }
        }

        if (HP <= 0)
        {
            Dead();
            return;
        }

        if (StepCount != 0)
        {
            if (!(PrevX < 0 || PrevZ < 0))
            {
                CurX = PrevX;
                CurZ = PrevZ;
            }
        }

        if (IsMovingEnd())
        {
            CurX = PrevX;
            CurZ = PrevZ;

            var endBuf = new byte[1024];
            var endW = new PacketWriter(endBuf);
            endW.SetByte(AiOpcode.MOVE_RESULT);
            endW.SetByte(PacketSuccess);
            endW.SetShort(Nid + NpcBand);
            endW.SetFloat(CurX);
            endW.SetFloat(CurZ);
            endW.SetFloat(CurY);
            endW.SetFloat(0);
            SendAll(endW.Written);

            State = NpcState.Standing;
            Delay = StandTime;
            DelayTime = TimeGet();

            if (Delay < 0)
            {
                Delay = 0;
                DelayTime = TimeGet();
            }

            return;
        }

        if (!PathFlag)
        {
            if (!StepMove(1))
            {
                State = NpcState.Standing;
                Delay = StandTime;
                DelayTime = TimeGet();
                return;
            }
        }
        else
        {
            if (!StepNoPathMove(1))
            {
                State = NpcState.Standing;
                Delay = StandTime;
                DelayTime = TimeGet();
                return;
            }
        }

        var buf = new byte[1024];
        var w = new PacketWriter(buf);
        w.SetByte(AiOpcode.MOVE_RESULT);
        w.SetByte(PacketSuccess);
        w.SetShort(Nid + NpcBand);
        w.SetFloat(PrevX);
        w.SetFloat(PrevZ);
        w.SetFloat(PrevY);
        float fMoveSpeed = SecForRealMoveMeter / (Speed / 1000.0f);
        w.SetFloat(fMoveSpeed);
        SendAll(w.Written);

        Delay = Speed;
        DelayTime = TimeGet();
    }

    /// <summary>CNpc::NpcSleeping.</summary>
    public void NpcSleeping()
    {
        NpcTrace("NpcSleeping()");

        // Day
        if ((GetNightMode?.Invoke() ?? 1) == 1)
        {
            State = NpcState.Standing;
            Delay = 0;
        }
        // Night
        else
        {
            State = NpcState.Sleeping;
            Delay = StandTime;
        }

        DelayTime = TimeGet();
    }

    /// <summary>CNpc::NpcFainting.</summary>
    public void NpcFainting(double currentTime)
    {
        NpcTrace("NpcFainting()");

        // Stunned for 2 seconds, then back to standing.
        if (currentTime > FaintingTime + FaintingDuration)
        {
            State = NpcState.Standing;
            Delay = 0;
            DelayTime = TimeGet();
            FaintingTime = 0.0;
        }
    }

    /// <summary>
    /// CNpc::NpcHealing — only the non-healer fallback is ported here.
    /// TODO(stage3.7-part2): healer logic (IsCloseTarget(range, 2), heal magic,
    /// tracing transition); healers fall back to standing until then.
    /// </summary>
    public void NpcHealing()
    {
        NpcTrace("NpcHealing()");

        if (NpcType != NpcTypeHealer)
        {
            InitTarget();
            State = NpcState.Standing;
            Delay = StandTime;
            DelayTime = TimeGet();
            return;
        }

        // TODO(stage3.7-part2) — see summary above.
        State = NpcState.Standing;
        Delay = StandTime;
        DelayTime = TimeGet();
    }

    // ------------------------------------------------------------------
    //  Movement / pathing
    // ------------------------------------------------------------------

    /// <summary>CNpc::RandomMove.</summary>
    public bool RandomMove()
    {
        // Normal movement uses walking speed.
        SecForMeter = Speed1;

        if (SearchRange == 0)
            return false;

        // Stationary NPC.
        if (MoveType == 0)
            return false;

        // Only move when a user is in view.
        if (!GetUserInView())
            return false;

        float fDestX = -1.0f, fDestZ = -1.0f;

        AiZone? zone = GetMapByIndex();
        if (zone is null)
            return false;

        Vector3 vStart, vEnd, vNewPos;
        float fDis;

        int nPathCount;
        bool bPeedBack = false;

        // Small random wander.
        if (MoveType == 1)
        {
            bPeedBack = IsInRange((int)CurX, (int)CurZ);
            // (bPeedBack==false: left its initial area — trace only in the C++)

            if (PatternFrame == 0)
            {
                PatternPos.X = (short)InitX;
                PatternPos.Z = (short)InitZ;
            }

            int randomX = MyRand(3, 7);
            int randomZ = MyRand(3, 7);

            fDestX = CurX + randomX;
            fDestZ = CurZ + randomZ;

            if (PatternFrame == 2)
            {
                fDestX = PatternPos.X;
                fDestZ = PatternPos.Z;
                PatternFrame = 0;
            }
            else
            {
                PatternFrame++;
            }

            vStart = new Vector3(CurX, CurY, CurZ);
            vEnd = new Vector3(fDestX, 0, fDestZ);
            fDis = GetDistance(vStart, vEnd);

            // Left the 50m validity area.
            if (fDis > 50)
            {
                vNewPos = GetVectorPosition(vStart, vEnd, 40);
                fDestX = vNewPos.X;
                fDestZ = vNewPos.Z;
                PatternFrame = 2;
                bPeedBack = true;
            }
        }
        // Path-list follower.
        else if (MoveType == 2)
        {
            if (PathCount == MaxPathCount)
                PathCount = 0;

            if (PathCount != 0 && !IsInPathRange())
            {
                PathCount--;
                nPathCount = GetNearPathPoint();

                if (nPathCount == -1)
                {
                    // Force the NPC 40m towards the beginning of its path.
                    vStart = new Vector3(CurX, CurY, CurZ);
                    fDestX = PathList[0].X + BattlePosX;
                    fDestZ = PathList[0].Z + BattlePosZ;
                    vEnd = new Vector3(fDestX, 0, fDestZ);
                    vNewPos = GetVectorPosition(vStart, vEnd, 40);
                    fDestX = vNewPos.X;
                    fDestZ = vNewPos.Z;
                }
                else
                {
                    if (nPathCount < 0)
                        return false;

                    fDestX = PathList[nPathCount].X + BattlePosX;
                    fDestZ = PathList[nPathCount].Z + BattlePosZ;
                    PathCount = (short)nPathCount;
                }
            }
            else
            {
                if (PathCount < 0)
                    return false;

                fDestX = PathList[PathCount].X + BattlePosX;
                fDestZ = PathList[PathCount].Z + BattlePosZ;
            }

            PathCount++;
        }
        // One-shot path follower.
        else if (MoveType == 3)
        {
            if (PathCount == MaxPathCount)
            {
                MoveType = 0;
                PathCount = 0;
                return false;
            }

            if (PathCount != 0 && !IsInPathRange())
            {
                PathCount--;
                nPathCount = GetNearPathPoint();

                if (nPathCount == -1)
                {
                    vStart = new Vector3(CurX, CurY, CurZ);
                    fDestX = PathList[0].X + BattlePosX;
                    fDestZ = PathList[0].Z + BattlePosZ;
                    vEnd = new Vector3(fDestX, 0, fDestZ);
                    vNewPos = GetVectorPosition(vStart, vEnd, 40);
                    fDestX = vNewPos.X;
                    fDestZ = vNewPos.Z;
                }
                else
                {
                    if (nPathCount < 0)
                        return false;

                    fDestX = PathList[nPathCount].X + BattlePosX;
                    fDestZ = PathList[nPathCount].Z + BattlePosX; // verbatim C++ bug: BattlePosX added to Z
                    PathCount = (short)nPathCount;
                }
            }
            else
            {
                if (PathCount < 0)
                    return false;

                fDestX = PathList[PathCount].X + BattlePosX;
                fDestZ = PathList[PathCount].Z + BattlePosX; // verbatim C++ bug: BattlePosX added to Z
            }

            PathCount++;
        }

        vStart = new Vector3(CurX, 0, CurZ);
        vEnd = new Vector3(fDestX, 0, fDestZ);

        if (!IsValidPosition(zone.Map, CurX, CurZ))
            return false;

        if (!IsValidPosition(zone.Map, fDestX, fDestZ))
            return false;

        // Dungeon monsters must stay inside their area.
        if (NpcType == NpcTypeDungeonMonster)
        {
            if (!IsInRange((int)fDestX, (int)fDestZ))
                return false;
        }

        fDis = GetDistance(vStart, vEnd);

        // Further than 100m: stay standing.
        if (fDis > NpcMaxMoveRange)
        {
            if (MoveType == 2 || MoveType == 3)
            {
                PathCount--;
                if (PathCount <= 0)
                    PathCount = 0;
            }

            return false;
        }

        // Destination within one step: move directly.
        if (fDis <= SecForMeter)
        {
            ClearPathFindData();

            StartPointX = CurX;
            StartPointY = CurZ;
            EndPointX = fDestX;
            EndPointY = fDestZ;
            PathFlag = true;
            AniFrameIndex = 1;
            Points[0].XPos = EndPointX;
            Points[0].ZPos = EndPointY;
            return true;
        }

        float fTempRange = fDis + 2;
        int minX = (int)(CurX - fTempRange) / TileSize;
        if (minX < 0)
            minX = 0;

        int minZ = (int)(CurZ - fTempRange) / TileSize;
        if (minZ < 0)
            minZ = 0;

        int maxX = (int)(CurX + fTempRange) / TileSize;
        if (maxX >= zone.Map.MapSize)
            maxX = zone.Map.MapSize - 1;

        int maxZ = (int)(CurZ + fTempRange) / TileSize;
        if (minZ >= zone.Map.MapSize)
            minZ = zone.Map.MapSize - 1; // verbatim C++ bug: clamps min_z where max_z was meant

        (int X, int Y) start = ((int)(CurX / TileSize) - minX, (int)(CurZ / TileSize) - minZ);
        (int X, int Y) end = ((int)(fDestX / TileSize) - minX, (int)(fDestZ / TileSize) - minZ);

        if (start.X < 0 || start.Y < 0 || end.X < 0 || end.Y < 0)
            return false;

        StartPointX = CurX;
        StartPointY = CurZ;
        EndPointX = fDestX;
        EndPointY = fDestZ;

        MinX = (short)minX;
        MinY = (short)minZ;
        MaxX = (short)maxX;
        MaxY = (short)maxZ;

        // Path followers (and area feedback) skip pathfinding and go straight.
        if (MoveType == 2 || MoveType == 3 || bPeedBack)
        {
            IsNoPathFind(SecForMeter);
            return true;
        }

        int nValue = RunPathFind(start, end, SecForMeter);
        if (nValue == 1)
            return true;

        return false;
    }

    /// <summary>CNpc::RandomBackMove — flee away from the target user.</summary>
    public bool RandomBackMove()
    {
        // Fleeing uses running speed.
        SecForMeter = Speed2;

        if (SearchRange == 0)
            return false;

        AiZone? zone = GetMapByIndex();
        if (zone is null)
            return false;

        float fDestX = -1.0f, fDestZ = -1.0f;

        int maxXx = zone.Map.MapSize;
        int maxZz = zone.Map.MapSize;

        float fTempRange = SearchRange * 2;
        int minX = (int)(CurX - fTempRange) / TileSize;
        if (minX < 0)
            minX = 0;

        int minZ = (int)(CurZ - fTempRange) / TileSize;
        if (minZ < 0)
            minZ = 0;

        int maxX = (int)(CurX + fTempRange) / TileSize;
        if (maxX >= maxXx)
            maxX = maxXx - 1;

        int maxZ = (int)(CurZ + fTempRange) / TileSize;
        if (maxZ >= maxZz)
            maxZ = maxZz - 1;

        var vStart = new Vector3(CurX, CurY, CurZ);
        Vector3 vEnd, vEnd22;
        float fDis;

        int nID = Target.Id;

        int iDir = 0;
        int iRandomX = 0;
        int iRandomValue = MyRand(0, 1); // C++ uses rand() % 2 here (not myrand)

        // Target is a user.
        if (nID >= UserBand && nID < NpcBand)
        {
            AiUser? user = GetUserPtr(nID - UserBand);
            if (user is null)
                return false;

            // Choose the flee direction; x axis first.
            if ((int)user.CurX != (int)CurX)
            {
                iRandomX = MyRand(SearchRange, (int)(SearchRange * 1.5));

                if ((int)user.CurX > (int)CurX)
                    iDir = 1;
                else
                    iDir = 2;
            }
            // Then the z axis.
            else
            {
                iRandomX = MyRand(0, SearchRange);
                if ((int)user.CurZ > (int)CurZ)
                    iDir = 3;
                else
                    iDir = 4;
            }

            switch (iDir)
            {
                case 1:
                    fDestX = CurX - iRandomX;
                    fDestZ = iRandomValue == 0 ? CurZ - iRandomX : CurZ + iRandomX;
                    break;

                case 2:
                    fDestX = CurX + iRandomX;
                    fDestZ = iRandomValue == 0 ? CurZ - iRandomX : CurZ + iRandomX;
                    break;

                case 3:
                    fDestZ = CurZ - iRandomX;
                    fDestX = iRandomValue == 0 ? CurX - iRandomX : CurX + iRandomX;
                    break;

                case 4:
                    fDestZ = CurZ - iRandomX; // verbatim C++: same as case 3
                    fDestX = iRandomValue == 0 ? CurX - iRandomX : CurX + iRandomX;
                    break;
            }

            vEnd = new Vector3(fDestX, 0, fDestZ);
            fDis = GetDistance(vStart, vEnd);

            // Cap the flee distance at 20m.
            if (fDis > 20)
            {
                vEnd22 = GetVectorPosition(vStart, vEnd, 20);
                fDestX = vEnd22.X;
                fDestZ = vEnd22.Z;
            }
        }
        // Target is an NPC: the C++ leaves this branch empty.
        else if (nID >= NpcBand && Target.Id < InvalidBand)
        {
        }

        (int X, int Y) start = ((int)(CurX / TileSize) - minX, (int)(CurZ / TileSize) - minZ);
        (int X, int Y) end = ((int)(fDestX / TileSize) - minX, (int)(fDestZ / TileSize) - minZ);

        if (start.X < 0 || start.Y < 0 || end.X < 0 || end.Y < 0)
            return false;

        StartPointX = CurX;
        StartPointY = CurZ;
        EndPointX = fDestX;
        EndPointY = fDestZ;

        MinX = (short)minX;
        MinY = (short)minZ;
        MaxX = (short)maxX;
        MaxY = (short)maxZ;

        int nValue = RunPathFind(start, end, SecForMeter);
        if (nValue == 1)
            return true;

        return false;
    }

    /// <summary>CNpc::IsInPathRange.</summary>
    public bool IsInPathRange()
    {
        const float fPathRange = 40.0f;
        var vStart = new Vector3(CurX, CurY, CurZ);

        if (PathCount < 0)
            return false;

        if (PathCount >= AiConstants.NpcMaxPathList)
            return false; // guard: the C++ would index m_PathList out of bounds (UB)

        var vEnd = new Vector3(
            PathList[PathCount].X + BattlePosX,
            0,
            PathList[PathCount].Z + BattlePosZ);

        float fDistance = GetDistance(vStart, vEnd);

        if ((int)fDistance <= (int)fPathRange + 1)
            return true;

        return false;
    }

    /// <summary>CNpc::GetNearPathPoint.</summary>
    public int GetNearPathPoint()
    {
        const float fMaxPathRange = NpcMaxMoveRange;
        int nRet = -1;
        var vStart = new Vector3(CurX, CurY, CurZ);
        var vEnd = new Vector3(
            PathList[PathCount].X + BattlePosX,
            0,
            PathList[PathCount].Z + BattlePosZ);

        float fDis1 = GetDistance(vStart, vEnd);
        float fDis2;

        if (PathCount + 1 >= MaxPathCount)
        {
            if (PathCount - 1 > 0)
            {
                vEnd = new Vector3(PathList[PathCount - 1].X + BattlePosX, 0, PathList[PathCount - 1].Z + BattlePosZ);
                fDis2 = GetDistance(vStart, vEnd);
            }
            else
            {
                vEnd = new Vector3(PathList[0].X + BattlePosX, 0, PathList[0].Z + BattlePosZ);
                fDis2 = GetDistance(vStart, vEnd);
            }
        }
        else
        {
            vEnd = new Vector3(PathList[PathCount + 1].X + BattlePosX, 0, PathList[PathCount + 1].Z + BattlePosZ);
            fDis2 = GetDistance(vStart, vEnd);
        }

        if (fDis1 <= fDis2)
        {
            if (fDis1 <= fMaxPathRange)
                nRet = PathCount;
        }
        else
        {
            if (fDis2 <= fMaxPathRange)
                nRet = PathCount + 1;
        }

        return nRet;
    }

    /// <summary>CNpc::IsInRange — inside the initial activity area?</summary>
    public bool IsInRange(int nX, int nZ)
    {
        bool bFlag1 = false, bFlag2 = false;
        if (LimitMinX < LimitMaxX)
        {
            if (Compare(nX, LimitMinX, LimitMaxX))
                bFlag1 = true;
        }
        else
        {
            if (Compare(nX, LimitMaxX, LimitMinX))
                bFlag1 = true;
        }

        if (LimitMinZ < LimitMaxZ)
        {
            if (Compare(nZ, LimitMinZ, LimitMaxZ))
                bFlag2 = true;
        }
        else
        {
            if (Compare(nZ, LimitMaxZ, LimitMinZ))
                bFlag2 = true;
        }

        return bFlag1 && bFlag2;
    }

    /// <summary>
    /// CNpc::PathFind — renamed: the <see cref="PathFind"/> field (the CPathFind
    /// instance) already claims the name. Note the C++ quirk kept verbatim: the
    /// A*-branch epilogue checks <c>m_pPath == nullptr</c> which is always true
    /// after the parent walk, so this branch always reports failure (0).
    /// </summary>
    public int RunPathFind((int X, int Y) start, (int X, int Y) end, float fDistance)
    {
        ClearPathFindData();

        if (start.X < 0 || start.Y < 0 || end.X < 0 || end.Y < 0)
            return -1;

        // Small movement within the same tile.
        if (start.X == end.X && start.Y == end.Y)
        {
            PathFlag = true;
            AniFrameIndex = 1;
            Points[0].XPos = EndPointX;
            Points[0].ZPos = EndPointY;
            return 1;
        }

        // Straight-line walk instead of pathfinding when the line is clear.
        if (IsPathFindCheck(fDistance))
        {
            PathFlag = true;
            return 1;
        }

        int minX = MinX;
        int minY = MinY;
        int maxX = MaxX;
        int maxY = MaxY;

        MapSizeX = maxX - minX + 1;
        MapSizeY = maxY - minY + 1;

        AiZone? zone = GetMapByIndex();
        if (zone is null)
            return 0; // guard: the C++ dereferences the cached m_pOrgMap here

        if ((long)MapSizeX * MapSizeY > PathMap.Length)
            return 0; // guard: the C++ overruns m_pMap[MAX_MAP_SIZE] here (UB)

        short[,] tiles = zone.Map.TileEvents;
        int tilesX = tiles.GetLength(0);
        int tilesY = tiles.GetLength(1);

        for (int i = 0; i < MapSizeY; i++)
        {
            for (int j = 0; j < MapSizeX; j++)
            {
                if (minX + j < 0 || minY + i < 0)
                    return 0;

                if (j * MapSizeY + i < 0)
                    return 0;

                // guard: out-of-map tiles read garbage in the C++ (UB); treat as blocked.
                bool blocked = minX + j >= tilesX || minY + i >= tilesY
                    || tiles[minX + j, minY + i] == 0;

                PathMap[j * MapSizeY + i] = blocked ? 1 : 0;
            }
        }

        Path = null;
        PathFind.SetMap(MapSizeX, MapSizeY, PathMap);
        Path = PathFind.FindPath(end.X, end.Y, start.X, start.Y);
        int count = 0;

        PathFinder.PathNode? node = Path;
        while (node is not null)
        {
            node = node.Parent;
            if (node is null)
                break;

            if (count < AiConstants.MaxPathLine)
            {
                // guard: the C++ writes m_pPoint[count] unbounded (UB past MAX_PATH_LINE).
                Points[count].PointX = node.X + minX;
                Points[count].PointY = node.Y + minY;
            }

            count++;
        }

        Path = node;

        if (count <= 0 || Path is null || count >= AiConstants.MaxPathLine)
            return 0;

        // NOTE: unreachable in practice (Path is always null above) — kept verbatim.
        AniFrameIndex = (short)(count - 1);

        for (int i = 0; i < count; i++)
        {
            if (i == count - 1)
            {
                Points[i].XPos = EndPointX;
                Points[i].ZPos = EndPointY;
            }
            else
            {
                Points[i].XPos = Points[i].PointX * TileSize + AddX;
                Points[i].ZPos = Points[i].PointY * TileSize + AddZ;
            }
        }

        return 1;
    }

    /// <summary>CNpc::GetMyField — quadrant of the current region (1..4, 0 outside).</summary>
    public int GetMyField()
    {
        int iRet = 0;
        int iX = RegionX * AiConstants.ViewDistance;
        int iZ = RegionZ * AiConstants.ViewDistance;
        int iAdd = AiConstants.ViewDistance / 2;
        int iCurX = (int)CurX;
        int iCurZ = (int)CurZ;

        if (Compare(iCurX, iX, iX + iAdd) && Compare(iCurZ, iZ, iZ + iAdd))
            iRet = 1;
        else if (Compare(iCurX, iX + iAdd, iX + AiConstants.ViewDistance) && Compare(iCurZ, iZ, iZ + iAdd))
            iRet = 2;
        else if (Compare(iCurX, iX, iX + iAdd) && Compare(iCurZ, iZ + iAdd, iZ + AiConstants.ViewDistance))
            iRet = 3;
        else if (Compare(iCurX, iX + iAdd, iX + AiConstants.ViewDistance)
            && Compare(iCurZ, iZ + iAdd, iZ + AiConstants.ViewDistance))
            iRet = 4;

        return iRet;
    }

    /// <summary>CNpc::IsDamagedUserList.</summary>
    public bool IsDamagedUserList(AiUser? user)
    {
        if (user is null)
            return false;

        for (int i = 0; i < AiConstants.NpcMaxUserList; i++)
        {
            if (string.Equals(DamagedUserList[i].UserId, user.UserId, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>CNpc::IsMovable — is the tile (x, z) walkable? (Arguments are tile coords.)</summary>
    public bool IsMovable(float x, float z)
    {
        if (x < 0 || z < 0)
            return false;

        AiZone? zone = GetMapByIndex();
        if (zone is null)
            return false;

        if (x >= zone.Map.MapSize || z >= zone.Map.MapSize)
            return false;

        if (zone.Map.TileEvents[(int)x, (int)z] == 0)
            return false;

        return true;
    }

    /// <summary>CNpc::IsMovingEnd.</summary>
    public bool IsMovingEnd()
    {
        if (PrevX == EndPointX && PrevZ == EndPointY)
        {
            AniFrameCount = 0;
            return true;
        }

        return false;
    }

    /// <summary>CNpc::StepMove — advance one step along the found path.</summary>
    public bool StepMove(int nStep)
    {
        _ = nStep; // unused in the C++ as well

        if (State != NpcState.Moving && State != NpcState.Tracing && State != NpcState.Back)
            return false;

        float fOldCurX, fOldCurZ;
        if (StepCount == 0)
        {
            fOldCurX = CurX;
            fOldCurZ = CurZ;
        }
        else
        {
            fOldCurX = PrevX;
            fOldCurZ = PrevZ;
        }

        var vStart = new Vector3(fOldCurX, 0, fOldCurZ);

        if (AniFrameCount < 0 || AniFrameCount >= AiConstants.MaxPathLine)
        {
            // guard: the C++ indexes m_pPoint out of bounds here (UB); handled like the
            // bad-point safety branch below.
            PrevX = EndPointX;
            PrevZ = EndPointY;
            SetUid(PrevX, PrevZ, Nid + NpcBand);
            return false;
        }

        var vEnd = new Vector3(Points[AniFrameCount].XPos, 0, Points[AniFrameCount].ZPos);

        // Safety code.
        if (Points[AniFrameCount].XPos < 0 || Points[AniFrameCount].ZPos < 0)
        {
            PrevX = EndPointX;
            PrevZ = EndPointY;
            SetUid(PrevX, PrevZ, Nid + NpcBand);
            return false;
        }

        float fDis = GetDistance(vStart, vEnd);

        if (fDis >= SecForMeter)
        {
            Vector3 vDis = GetVectorPosition(vStart, vEnd, SecForMeter);
            PrevX = vDis.X;
            PrevZ = vDis.Z;
        }
        else
        {
            AniFrameCount++;
            if (AniFrameCount >= AiConstants.MaxPathLine)
            {
                // guard: the C++ reads m_pPoint[MAX_PATH_LINE] here (UB).
                PrevX = EndPointX;
                PrevZ = EndPointY;
            }
            else if (AniFrameCount == AniFrameIndex)
            {
                vEnd = new Vector3(Points[AniFrameCount].XPos, 0, Points[AniFrameCount].ZPos);
                fDis = GetDistance(vStart, vEnd);
                // The final point may move up to SecForMeter+1.
                if (fDis > SecForMeter)
                {
                    Vector3 vDis = GetVectorPosition(vStart, vEnd, SecForMeter);
                    PrevX = vDis.X;
                    PrevZ = vDis.Z;
                    AniFrameCount--;
                }
                else
                {
                    PrevX = EndPointX;
                    PrevZ = EndPointY;
                }
            }
            else
            {
                vEnd = new Vector3(Points[AniFrameCount].XPos, 0, Points[AniFrameCount].ZPos);
                fDis = GetDistance(vStart, vEnd);
                if (fDis >= SecForMeter)
                {
                    Vector3 vDis = GetVectorPosition(vStart, vEnd, SecForMeter);
                    PrevX = vDis.X;
                    PrevZ = vDis.Z;
                }
                else
                {
                    PrevX = EndPointX;
                    PrevZ = EndPointY;
                }
            }
        }

        vStart = new Vector3(fOldCurX, 0, fOldCurZ);
        vEnd = new Vector3(PrevX, 0, PrevZ);

        SecForRealMoveMeter = GetDistance(vStart, vEnd);

        if (StepCount == 0)
        {
            StepCount++;
        }
        else
        {
            StepCount++;
            CurX = fOldCurX;
            CurZ = fOldCurZ;

            if (!SetUid(CurX, CurZ, Nid + NpcBand))
                return false;
        }

        return true;
    }

    /// <summary>CNpc::StepNoPathMove — advance one waypoint of a straight-line move.</summary>
    public bool StepNoPathMove(int nStep)
    {
        _ = nStep; // unused in the C++ as well

        if (State != NpcState.Moving && State != NpcState.Tracing && State != NpcState.Back)
            return false;

        float fOldCurX, fOldCurZ;
        if (StepCount == 0)
        {
            fOldCurX = CurX;
            fOldCurZ = CurZ;
        }
        else
        {
            fOldCurX = PrevX;
            fOldCurZ = PrevZ;
        }

        if (StepCount < 0 || StepCount >= AniFrameIndex)
            return false;

        if (StepCount >= AiConstants.MaxPathLine)
            return false; // guard: the C++ would index m_pPoint out of bounds (UB)

        var vStart = new Vector3(fOldCurX, 0, fOldCurZ);
        PrevX = Points[StepCount].XPos;
        PrevZ = Points[StepCount].ZPos;
        var vEnd = new Vector3(PrevX, 0, PrevZ);

        if (PrevX == -1 || PrevZ == -1)
            return false;

        SecForRealMoveMeter = GetDistance(vStart, vEnd);

        if (++StepCount > 1)
        {
            if (fOldCurX < 0 || fOldCurZ < 0)
                return false;

            CurX = fOldCurX;
            CurZ = fOldCurZ;

            if (!SetUid(CurX, CurZ, Nid + NpcBand))
                return false;
        }

        return true;
    }

    /// <summary>CNpc::IsCloseTarget(int, int) — distance test against the current target.</summary>
    public int IsCloseTarget(int nRange, int flag = 0)
    {
        AiUser? user = null;
        Npc? npc = null;
        float fWillDis = 0.0f, fX = 0.0f, fZ = 0.0f;
        bool bUserType = false;
        var vNpc = new Vector3(CurX, CurY, CurZ);
        Vector3 vUser = default;

        if (Target.Id >= UserBand && Target.Id < NpcBand)
        {
            user = GetUserPtr(Target.Id - UserBand);
            if (user is null)
            {
                InitTarget();
                return -1;
            }

            vUser = new Vector3(user.CurX, user.CurY, user.CurZ);
            var vWillUser = new Vector3(user.WillX, user.WillY, user.WillZ);
            fX = user.CurX;
            fZ = user.CurZ;

            fWillDis = KoMath.Magnitude(vWillUser - vNpc);
            fWillDis -= Bulk;
            bUserType = true;
        }
        else if (Target.Id >= NpcBand && Target.Id < InvalidBand)
        {
            npc = GetNpcPtr(Target.Id - NpcBand);
            if (npc is null)
            {
                InitTarget();
                return -1;
            }

            vUser = new Vector3(npc.CurX, npc.CurY, npc.CurZ);
            fX = npc.CurX;
            fZ = npc.CurZ;
        }
        else
        {
            return -1;
        }

        float fDis = KoMath.Magnitude(vUser - vNpc);
        fDis -= Bulk;

        if (NpcType == NpcTypeDungeonMonster)
        {
            if (!IsInRange((int)vUser.X, (int)vUser.Z))
                return -1;
        }

        if (flag == 1)
        {
            ResetFlag = 1;
            if (user is not null)
            {
                if (Target.X == user.CurX && Target.Z == user.CurZ)
                    ResetFlag = 0;
            }
        }

        if ((int)fDis > nRange)
        {
            if (flag == 2)
            {
                ResetFlag = 1;
                Target.X = fX;
                Target.Z = fZ;
            }

            return 0;
        }

        // Refresh the target coords and the final point.
        EndPointX = CurX;
        EndPointY = CurZ;
        Target.X = fX;
        Target.Z = fZ;

        // Ranged attackers judge by attack distance.
        if (LongType == 1)
        {
            if (fDis < LongAttackRange)
                return 1;

            if (fDis > LongAttackRange && fDis <= nRange)
                return 2;
        }
        // Melee (direct attack).
        else
        {
            if (flag == 1)
            {
                if (fDis < ShortAttackRange + Bulk)
                    return 1;

                if (fDis > ShortAttackRange + Bulk && fDis <= nRange)
                    return 2;

                // Users also get checked against their Will position.
                if (bUserType)
                {
                    if (fWillDis > ShortAttackRange + Bulk && fWillDis <= nRange)
                        return 2;
                }
            }
            else
            {
                if (fDis < ShortAttackRange + Bulk)
                    return 1;

                if (fDis > ShortAttackRange + Bulk && fDis <= nRange)
                    return 2;
            }
        }

        return 0;
    }

    /// <summary>CNpc::GetTargetPath — path towards the current target.</summary>
    public int GetTargetPath(int option = 0)
    {
        // sungyong 2002.06.12
        int nInitType = InitMoveType;
        if (InitMoveType >= 100)
            nInitType = InitMoveType - 100;

        if (NpcType != 0)
        {
            // Allow it to return to its own spot.
            if (MoveType != nInitType)
                MoveType = (byte)nInitType;
        }

        // Chasing uses running speed.
        SecForMeter = Speed2;
        AiUser? targetUser = null;
        Npc? npcTarget = null;
        float chaseRange = 0.0f;
        Vector3 vUser = default, vNpc = default, vEnd22 = default;
        float fDis;

        // Target is a user.
        if (Target.Id >= UserBand && Target.Id < NpcBand)
        {
            targetUser = GetUserPtr(Target.Id - UserBand);
            if (targetUser is null)
            {
                InitTarget();
                return -1;
            }

            if (targetUser.HP <= 0 || targetUser.Live == 0)
            {
                InitTarget();
                return -1;
            }

            if (targetUser.CurZone != CurZone)
            {
                InitTarget();
                return -1;
            }

            // Attacked by magic or arrows.
            if (option == 1)
            {
                vNpc = new Vector3(CurX, CurY, CurZ);
                vUser = new Vector3(targetUser.CurX, targetUser.CurY, targetUser.CurZ);
                fDis = GetDistance(vNpc, vUser);

                if (fDis >= NpcMaxMoveRange)
                    return -1;

                chaseRange = fDis + 10;
            }
            else
            {
                chaseRange = SearchRange;

                // Larger range once damaged.
                if (IsDamagedUserList(targetUser))
                    chaseRange = TracingRange;
                else
                    chaseRange += 2;
            }
        }
        // Target is an NPC.
        else if (Target.Id >= NpcBand && Target.Id < InvalidBand)
        {
            npcTarget = GetNpcPtr(Target.Id - NpcBand);
            if (npcTarget is null)
            {
                InitTarget();
                return 0; // verbatim C++: 'return false;' in an int function
            }

            if (npcTarget.HP <= 0 || npcTarget.State == NpcState.Dead)
            {
                InitTarget();
                return -1;
            }

            chaseRange = TracingRange;
        }

        AiZone? zone = GetMapByIndex();
        if (zone is null)
            return -1;

        int maxXx = zone.Map.MapSize;
        int maxZz = zone.Map.MapSize;

        int minX = (int)(CurX - chaseRange) / TileSize;
        if (minX < 0)
            minX = 0;

        int minZ = (int)(CurZ - chaseRange) / TileSize;
        if (minZ < 0)
            minZ = 0;

        int maxX = (int)(CurX + chaseRange) / TileSize;
        if (maxX >= maxXx)
            maxX = maxXx - 1;

        int maxZ = (int)(CurZ + chaseRange) / TileSize;
        if (minZ >= maxZz)
            minZ = maxZz - 1; // verbatim C++ bug: clamps min_z where max_z was meant

        if (targetUser is not null)
        {
            // Is the user within search range?
            if (!IsPointInRect(
                    (int)(targetUser.CurX / TileSize), (int)(targetUser.CurZ / TileSize),
                    minX, minZ, maxX + 1, maxZ + 1))
                return -1;

            StartPointX = CurX;
            StartPointY = CurZ;

            vNpc = new Vector3(CurX, CurY, CurZ);
            vUser = new Vector3(targetUser.CurX, targetUser.CurY, targetUser.CurZ);

            // Pick the attack direction slot around the user.
            IsSurround(targetUser);

            if (AttackPos > 0 && AttackPos < 9)
            {
                float fDegree = (AttackPos - 1) * 45.0f;
                float fTargetDistance = 2.0f + Bulk;
                vEnd22 = ComputeDestPos(vUser, 0.0f, fDegree, fTargetDistance);
                float fSurX = vEnd22.X - vUser.X;
                float fSurZ = vEnd22.Z - vUser.Z;
                EndPointX = vUser.X + fSurX;
                EndPointY = vUser.Z + fSurZ;
            }
            else
            {
                vEnd22 = CalcAdaptivePosition(vNpc, vUser, 2.0f + Bulk);
                EndPointX = vEnd22.X;
                EndPointY = vEnd22.Z;
            }
        }
        else if (npcTarget is not null)
        {
            if (!IsPointInRect(
                    (int)(npcTarget.CurX / TileSize), (int)(npcTarget.CurZ / TileSize),
                    minX, minZ, maxX + 1, maxZ + 1))
                return -1;

            StartPointX = CurX;
            StartPointY = CurZ;

            vNpc = new Vector3(CurX, CurY, CurZ);
            vUser = new Vector3(npcTarget.CurX, npcTarget.CurY, npcTarget.CurZ);

            vEnd22 = CalcAdaptivePosition(vNpc, vUser, 2.0f + Bulk);
            EndPointX = vEnd22.X;
            EndPointY = vEnd22.Z;
        }

        Vector3 vDistance = vEnd22 - vNpc;
        fDis = KoMath.Magnitude(vDistance);

        if (fDis <= SecForMeter)
        {
            ClearPathFindData();
            PathFlag = true;
            AniFrameIndex = 1;
            Points[0].XPos = EndPointX;
            Points[0].ZPos = EndPointY;
            return 1;
        }

        if ((int)fDis > chaseRange)
            return -1;

        // Dungeon monsters always pathfind.
        if (NpcType != NpcTypeDungeonMonster)
        {
            // With an active target, skip pathfinding and go straight.
            if (Target.Id != -1)
                return 0;
        }

        (int X, int Y) start = ((int)(CurX / TileSize) - minX, (int)(CurZ / TileSize) - minZ);
        (int X, int Y) end = ((int)(vEnd22.X / TileSize) - minX, (int)(vEnd22.Z / TileSize) - minZ);

        if (NpcType == NpcTypeDungeonMonster)
        {
            if (!IsInRange((int)vEnd22.X, (int)vEnd22.Z))
                return -1;
        }

        MinX = (short)minX;
        MinY = (short)minZ;
        MaxX = (short)maxX;
        MaxY = (short)maxZ;

        return RunPathFind(start, end, SecForMeter);
    }

    private static readonly float[] SurroundFx =
        [0.0f, -1.4142f, -2.0f, -1.4167f, 0.0f, 1.4117f, 2.0000f, 1.4167f];

    private static readonly float[] SurroundFz =
        [2.0f, 1.4142f, 0.0f, -1.4167f, -2.0f, -1.4167f, -0.0035f, 1.4117f];

    /// <summary>CNpc::MoveAttack — snap next to the target while attacking.</summary>
    public void MoveAttack()
    {
        Vector3 vUser = default, vEnd22 = default;
        var vNpc = new Vector3(CurX, CurY, CurZ);

        if (Target.Id >= UserBand && Target.Id < NpcBand)
        {
            AiUser? user = GetUserPtr(Target.Id - UserBand);
            if (user is null)
            {
                InitTarget();
                return;
            }

            vUser = new Vector3(user.CurX, user.CurY, user.CurZ);
            vEnd22 = CalcAdaptivePosition(vNpc, vUser, 2);

            if (AttackPos > 0 && AttackPos < 9)
            {
                float fX = vUser.X + SurroundFx[AttackPos - 1];
                float fZ = vUser.Z + SurroundFz[AttackPos - 1];
                vEnd22 = new Vector3(fX, 0, fZ);
            }
        }
        else if (Target.Id >= NpcBand && Target.Id < InvalidBand)
        {
            Npc? npc = GetNpcPtr(Target.Id - NpcBand);
            if (npc is null)
            {
                InitTarget();
                return;
            }

            vUser = new Vector3(npc.CurX, npc.CurY, npc.CurZ);
            vEnd22 = CalcAdaptivePosition(vNpc, vUser, 2);
        }

        float fDis = KoMath.Magnitude(vUser - vNpc);

        // Under 3m the NPC attacks standing still.
        if ((int)fDis < 3)
            return;

        fDis = KoMath.Magnitude(vEnd22 - vNpc);
        CurX = vEnd22.X;
        CurZ = vEnd22.Z;

        // Move-attack packet.
        var buf = new byte[1024];
        var w = new PacketWriter(buf);
        w.SetByte(AiOpcode.MOVE_RESULT);
        w.SetByte(PacketSuccess);
        w.SetShort(Nid + NpcBand);
        w.SetFloat(CurX);
        w.SetFloat(CurZ);
        w.SetFloat(CurY);
        w.SetFloat(fDis);
        SendAll(w.Written);

        // Move-end packet.
        var endBuf = new byte[1024];
        var endW = new PacketWriter(endBuf);
        endW.SetByte(AiOpcode.MOVE_RESULT);
        endW.SetByte(PacketSuccess);
        endW.SetShort(Nid + NpcBand);
        endW.SetFloat(CurX);
        endW.SetFloat(CurZ);
        endW.SetFloat(CurY);
        endW.SetFloat(0);
        SendAll(endW.Written);

        SetUid(CurX, CurZ, Nid + NpcBand);

        // Refresh the final point with the target's latest coords.
        EndPointX = CurX;
        EndPointY = CurZ;
    }

    /// <summary>CNpc::IsChangePath — does the found path still reach the target?</summary>
    public bool IsChangePath(int nStep = 1)
    {
        _ = nStep; // unused in the C++ as well

        float fCurX = 0.0f, fCurZ = 0.0f;
        GetTargetPos(ref fCurX, ref fCurZ);

        var vStart = new Vector3(EndPointX, 0, EndPointY);
        var vEnd = new Vector3(fCurX, 0, fCurZ);

        float fDis = GetDistance(vStart, vEnd);
        const float fCompDis = 3.0f;

        if (fDis < fCompDis)
            return false;

        return true;
    }

    /// <summary>CNpc::GetTargetPos.</summary>
    public bool GetTargetPos(ref float x, ref float z)
    {
        if (Target.Id >= UserBand && Target.Id < NpcBand)
        {
            AiUser? user = GetUserPtr(Target.Id - UserBand);
            if (user is null)
                return false;

            x = user.CurX;
            z = user.CurZ;
        }
        else if (Target.Id >= NpcBand && Target.Id < InvalidBand)
        {
            Npc? npc = GetNpcPtr(Target.Id - NpcBand);
            if (npc is null)
                return false;

            x = npc.CurX;
            z = npc.CurZ;
        }

        return true;
    }

    /// <summary>CNpc::ResetPath.</summary>
    public bool ResetPath()
    {
        float curX = 0.0f, curZ = 0.0f;
        GetTargetPos(ref curX, ref curZ);

        Target.X = curX;
        Target.Z = curZ;

        int nValue = GetTargetPath();

        // Target has been lost or ran away.
        if (nValue == -1)
            return false;

        // Head straight for the target.
        if (nValue == 0)
        {
            SecForMeter = Speed2;
            IsNoPathFind(SecForMeter);
        }

        return true;
    }

    /// <summary>CNpc::IsPathFindCheck — can the target be reached in a straight line?</summary>
    public bool IsPathFindCheck(float fDistance)
    {
        var vStart = new Vector3(StartPointX, 0, StartPointY);
        var vEnd = new Vector3(EndPointX, 0, EndPointY);
        var vDis = new Vector3(StartPointX, 0, StartPointY);
        int count = 0;
        int nError = 0;

        AiZone? zone = GetMapByIndex();
        if (zone is null)
            return false;

        int nX = (int)(vStart.X / TileSize);
        int nZ = (int)(vStart.Z / TileSize);
        if (zone.Map.IsMovable(nX, nZ)) // MAP::IsMovable == true means the tile is blocked
            return false;

        nX = (int)(vEnd.X / TileSize);
        nZ = (int)(vEnd.Z / TileSize);
        if (zone.Map.IsMovable(nX, nZ))
            return false;

        while (true)
        {
            var vOldDis = new Vector3(vDis.X, 0, vDis.Z);
            vDis = GetVectorPosition(vDis, vEnd, fDistance);
            float fDis = GetDistance(vOldDis, vEnd);

            if (fDis > NpcMaxMoveRange)
            {
                nError = -1;
                break;
            }

            if (fDis <= fDistance)
            {
                nX = (int)(vDis.X / TileSize);
                nZ = (int)(vDis.Z / TileSize);
                if (zone.Map.IsMovable(nX, nZ))
                {
                    nError = -1;
                    break;
                }

                if (count >= AiConstants.MaxPathLine)
                {
                    nError = -1;
                    break;
                }

                Points[count].XPos = vEnd.X;
                Points[count].ZPos = vEnd.Z;
                count++;
                break;
            }
            else
            {
                nX = (int)(vDis.X / TileSize);
                nZ = (int)(vDis.Z / TileSize);
                if (zone.Map.IsMovable(nX, nZ))
                {
                    nError = -1;
                    break;
                }

                if (count >= AiConstants.MaxPathLine)
                {
                    nError = -1;
                    break;
                }

                Points[count].XPos = vDis.X;
                Points[count].ZPos = vDis.Z;
            }

            count++;
        }

        AniFrameIndex = (short)count;

        return nError != -1;
    }

    /// <summary>CNpc::IsNoPathFind — straight-line waypoints to the target.</summary>
    public void IsNoPathFind(float fDistance)
    {
        ClearPathFindData();
        PathFlag = true;

        var vStart = new Vector3(StartPointX, 0, StartPointY);
        var vEnd = new Vector3(EndPointX, 0, EndPointY);
        var vDis = new Vector3(StartPointX, 0, StartPointY);
        int count = 0;

        float fDis = GetDistance(vStart, vEnd);

        // Further than 100m: stay standing.
        if (fDis > NpcMaxMoveRange)
        {
            ClearPathFindData();
            return;
        }

        AiZone? zone = GetMapByIndex();
        if (zone is null)
        {
            ClearPathFindData();
            return;
        }

        while (true)
        {
            var vOldDis = new Vector3(vDis.X, 0, vDis.Z);
            vDis = GetVectorPosition(vDis, vEnd, fDistance);
            fDis = GetDistance(vOldDis, vEnd);

            if (fDis <= fDistance)
            {
                if (count < 0 || count >= AiConstants.MaxPathLine)
                {
                    ClearPathFindData();
                    return;
                }

                Points[count].XPos = vEnd.X;
                Points[count].ZPos = vEnd.Z;
                count++;
                break;
            }
            else
            {
                if (count < 0 || count >= AiConstants.MaxPathLine)
                {
                    ClearPathFindData();
                    return;
                }

                Points[count].XPos = vDis.X;
                Points[count].ZPos = vDis.Z;
            }

            count++;
        }

        if (count <= 0 || count >= AiConstants.MaxPathLine)
        {
            ClearPathFindData();
            return;
        }

        AniFrameIndex = (short)count;
    }

    /// <summary>CNpc::NpcMoveEnd.</summary>
    public void NpcMoveEnd()
    {
        SetUid(CurX, CurZ, Nid + NpcBand);

        var buf = new byte[1024];
        var w = new PacketWriter(buf);
        w.SetByte(AiOpcode.MOVE_RESULT);
        w.SetByte(PacketSuccess);
        w.SetShort(Nid + NpcBand);
        w.SetFloat(CurX);
        w.SetFloat(CurZ);
        w.SetFloat(CurY);
        w.SetFloat(0);
        SendAll(w.Written);
    }

    // ------------------------------------------------------------------
    //  Geometry (__Vector3 semantics via System.Numerics + KoMath)
    // ------------------------------------------------------------------

    /// <summary>CNpc::GetDir — 8-direction between two points, also sets AddX/AddZ.</summary>
    public int GetDir(float x1, float z1, float x2, float z2)
    {
        int nDir;               //  3 4 5
                                //  2 8 6
                                //  1 0 7
        int x11 = (int)x1 / TileSize;
        int y11 = (int)z1 / TileSize;
        int x22 = (int)x2 / TileSize;
        int y22 = (int)z2 / TileSize;

        int deltax = x22 - x11;
        int deltay = y22 - y11;

        int fx = (int)x1 / TileSize * TileSize;
        int fy = (int)z1 / TileSize * TileSize;

        float addX = x1 - fx;
        float addY = z1 - fy;

        if (deltay == 0)
        {
            nDir = x22 > x11 ? DirRight : DirLeft;
        }
        else if (deltax == 0)
        {
            nDir = y22 > y11 ? DirDown : DirUp;
        }
        else if (y22 > y11)
        {
            nDir = x22 > x11 ? DirDownRight : DirDownLeft;
        }
        else
        {
            nDir = x22 > x11 ? DirUpRight : DirUpLeft;
        }

        switch (nDir)
        {
            case DirDown:
                AddX = addX;
                AddZ = 3;
                break;

            case DirDownLeft:
                AddX = 1;
                AddZ = 3;
                break;

            case DirLeft:
                AddX = 1;
                AddZ = addY;
                break;

            case DirUpLeft:
                AddX = 1;
                AddZ = 1;
                break;

            case DirUp:
                AddX = addX;
                AddZ = 1;
                break;

            case DirUpRight:
                AddX = 3;
                AddZ = 1;
                break;

            case DirRight:
                AddX = 3;
                AddZ = addY;
                break;

            case DirDownRight:
                AddX = 3;
                AddZ = 3;
                break;
        }

        return nDir;
    }

    /// <summary>CNpc::GetDirection.</summary>
    public static Vector3 GetDirection(Vector3 vStart, Vector3 vEnd)
        => KoMath.Normalized(vEnd - vStart);

    /// <summary>CNpc::GetVectorPosition — fDis meters from vOrig towards vDest.</summary>
    public static Vector3 GetVectorPosition(Vector3 vOrig, Vector3 vDest, float fDis)
    {
        Vector3 vOff = KoMath.Normalized(vDest - vOrig);
        vOff *= fDis;
        return vOrig + vOff;
    }

    /// <summary>CNpc::GetDistance.</summary>
    public static float GetDistance(Vector3 vOrig, Vector3 vDest)
        => KoMath.Magnitude(vOrig - vDest);

    /// <summary>CNpc::CalcAdaptivePosition — fAttackDistance from vPosDest towards vPosOrig.</summary>
    public static Vector3 CalcAdaptivePosition(Vector3 vPosOrig, Vector3 vPosDest, float fAttackDistance)
    {
        Vector3 vTemp = KoMath.Normalized(vPosOrig - vPosDest);
        vTemp *= fAttackDistance;
        return vPosDest + vTemp;
    }

    /// <summary>CNpc::ComputeDestPos — rotate (0,0,1) by the Y-axis and scale.</summary>
    public static Vector3 ComputeDestPos(Vector3 vCur, float fDegree, float fDegreeOffset, float fDistance)
    {
        float rad = DegreesToRadians(fDegree + fDegreeOffset);
        // __Matrix44::RotationY applied to (0,0,1) yields (sin, 0, cos).
        var vDir = new Vector3(MathF.Sin(rad), 0.0f, MathF.Cos(rad));
        vDir *= fDistance;
        return vCur + vDir;
    }

    /// <summary>CNpc::Yaw2D — yaw (radians) of a normalized 2D direction.</summary>
    public static void Yaw2D(float fDirX, float fDirZ, out float fYawResult)
    {
        if (fDirX >= 0.0f)
        {
            if (fDirZ >= 0.0f)
                fYawResult = (float)Math.Asin(fDirX);
            else
                fYawResult = DegreesToRadians(90.0f) + (float)Math.Acos(fDirX);
        }
        else
        {
            if (fDirZ >= 0.0f)
                fYawResult = DegreesToRadians(270.0f) + (float)Math.Acos(-fDirX);
            else
                fYawResult = DegreesToRadians(180.0f) + (float)Math.Asin(-fDirX);
        }
    }

    // ------------------------------------------------------------------
    //  View / region checks
    // ------------------------------------------------------------------

    /// <summary>CNpc::GetUserInView — is any user within NPC_VIEW_RANGE?</summary>
    public bool GetUserInView()
    {
        AiZone? zone = GetMapByIndex();
        if (zone is null)
            return false;

        int maxXx = zone.RegionsX;
        int maxZz = zone.RegionsZ;
        int minX = (int)(CurX - AiConstants.NpcViewRange) / AiConstants.ViewDistance;
        if (minX < 0)
            minX = 0;

        int minZ = (int)(CurZ - AiConstants.NpcViewRange) / AiConstants.ViewDistance;
        if (minZ < 0)
            minZ = 0;

        int maxX = (int)(CurX + AiConstants.NpcViewRange) / AiConstants.ViewDistance;
        if (maxX >= maxXx)
            maxX = maxXx - 1;

        int maxZ = (int)(CurZ + AiConstants.NpcViewRange) / AiConstants.ViewDistance;
        if (maxZ >= maxZz)
            maxZ = maxZz - 1;

        int searchX = maxX - minX + 1;
        int searchZ = maxZ - minZ + 1;

        for (int i = 0; i < searchX; i++)
        {
            for (int j = 0; j < searchZ; j++)
            {
                if (GetUserInViewRange(minX + i, minZ + j))
                    return true;
            }
        }

        return false;
    }

    /// <summary>CNpc::GetUserInViewRange.</summary>
    public bool GetUserInViewRange(int x, int z)
    {
        AiZone? zone = GetMapByIndex();
        if (zone is null)
            return false;

        if (x < 0 || z < 0 || x > zone.RegionsX - 1 || z > zone.RegionsZ - 1)
            return false;

        var vStart = new Vector3(CurX, 0, CurZ);

        foreach (int userId in zone.Regions[x, z].Users)
        {
            if (userId < 0)
                continue;

            AiUser? user = GetUserPtr(userId);
            if (user is null)
                continue;

            var vEnd = new Vector3(user.CurX, 0, user.CurZ);
            float fDis = GetDistance(vStart, vEnd);
            if (fDis <= AiConstants.NpcViewRange)
                return true;
        }

        return false;
    }

    /// <summary>CNpc::CheckFindEnemy — should this NPC scan for enemies while waiting?</summary>
    public bool CheckFindEnemy()
    {
        // Guards also attack monsters, so they always scan.
        if (NpcType is NpcTypeGuard or NpcTypePatrolGuard or NpcTypeStoreGuard)
            return true;

        AiZone? zone = GetMapByIndex();
        if (zone is null)
            return false;

        if (RegionX > zone.RegionsX - 1 || RegionZ > zone.RegionsZ - 1 || RegionX < 0 || RegionZ < 0)
            return false;

        return zone.Regions[RegionX, RegionZ].Moving == 1;
    }

    // ------------------------------------------------------------------
    //  Packets
    // ------------------------------------------------------------------

    /// <summary>CNpc::FillNpcInfo — byte-exact AG_NPC_INFO payload.</summary>
    public void FillNpcInfo(ref PacketWriter w, byte flag)
    {
        _ = flag; // unused in the C++ as well

        w.SetByte(AiOpcode.AG_NPC_INFO);
        if (SpecialType == 5 && ChangeType == 0)
            w.SetByte(0); // don't register in the region
        else
            w.SetByte(1); // register in the region
        w.SetShort(Nid + NpcBand);
        w.SetShort(Sid);
        w.SetShort(Pid);
        w.SetShort(Size);
        w.SetInt(Weapon1);
        w.SetInt(Weapon2);
        w.SetShort(CurZone);
        w.SetShort(ZoneIndex);
        byte[] name = Encoding.Latin1.GetBytes(Name);
        w.SetByte((byte)name.Length); // SetVarString: uint8 length + raw bytes
        w.SetString(name);
        w.SetByte(Group);
        w.SetByte((byte)Level);
        w.SetFloat(CurX);
        w.SetFloat(CurZ);
        w.SetFloat(CurY);
        w.SetByte(Direction);

        if (HP <= 0)
            w.SetByte(0x00);
        else
            w.SetByte(0x01);

        w.SetByte(NpcType);
        w.SetInt(SellingGroup);
        w.SetDWord((uint)MaxHP);
        w.SetDWord((uint)HP);
        w.SetByte(GateOpen);
        w.SetShort(HitRate);
        w.SetByte(ObjectType);
        w.SetByte(TrapNumber);
    }

    /// <summary>CNpc::SendNpcInfoAll — NPC_INFO_ALL batch entry (no opcode byte).</summary>
    public void SendNpcInfoAll(ref PacketWriter w, int count)
    {
        _ = count; // unused in the C++ as well

        if (SpecialType == 5 && ChangeType == 0)
            w.SetByte(0); // don't register in the region
        else
            w.SetByte(1); // register in the region
        w.SetShort(Nid + NpcBand);
        w.SetShort(Sid);
        w.SetShort(Pid);
        w.SetShort(Size);
        w.SetInt(Weapon1);
        w.SetInt(Weapon2);
        w.SetShort(CurZone);
        w.SetShort(ZoneIndex);
        byte[] name = Encoding.Latin1.GetBytes(Name);
        w.SetByte((byte)name.Length);
        w.SetString(name);
        w.SetByte(Group);
        w.SetByte((byte)Level);
        w.SetFloat(CurX);
        w.SetFloat(CurZ);
        w.SetFloat(CurY);
        w.SetByte(Direction);
        w.SetByte(NpcType);
        w.SetInt(SellingGroup);
        w.SetDWord((uint)MaxHP);
        w.SetDWord((uint)HP);
        w.SetByte(GateOpen);
        w.SetShort(HitRate);
        w.SetByte(ObjectType);
        w.SetByte(TrapNumber);
    }

    /// <summary>CNpc::SendAttackSuccess — AG_ATTACK_RESULT broadcast.</summary>
    public void SendAttackSuccess(byte byResult, int tuid, short sDamage, int nHP = 0, byte byFlag = 0, byte byAttackType = 1)
    {
        int sid, tid;
        byte type;

        if (byFlag == 0)
        {
            type = 0x02;
            sid = Nid + NpcBand;
            tid = tuid;
        }
        else
        {
            type = 0x01;
            sid = tuid;
            tid = Nid + NpcBand;
        }

        var buf = new byte[256];
        var w = new PacketWriter(buf);
        w.SetByte(AiOpcode.AG_ATTACK_RESULT);
        w.SetByte(type);
        w.SetByte(byResult);
        w.SetShort(sid);
        w.SetShort(tid);
        w.SetShort(sDamage);
        w.SetDWord((uint)nHP);
        w.SetByte(byAttackType);

        SendAll(w.Written);
    }

    // ------------------------------------------------------------------
    //  HP / MP / duration magic
    // ------------------------------------------------------------------

    /// <summary>CNpc::HpChange — 10s HP regen tick + AG_USER_SET_HP broadcast.</summary>
    public void HpChange()
    {
        HpChangeTime = TimeGet();

        if (State == NpcState.Dead)
            return;

        // No regen when about to die.
        if (HP < 1)
            return;

        // Already at full HP.
        if (HP == MaxHP)
            return;

        int amount = MaxHP / 20;

        HP += amount;
        if (HP < 0)
            HP = 0;
        else if (HP > MaxHP)
            HP = MaxHP;

        var buf = new byte[256];
        var w = new PacketWriter(buf);
        w.SetByte(AiOpcode.AG_USER_SET_HP);
        w.SetShort(Nid + NpcBand);
        w.SetDWord((uint)HP);
        w.SetDWord((uint)MaxHP);

        SendAll(w.Written);
    }

    /// <summary>CNpc::MSpChange.</summary>
    public void MSpChange(int type, int amount)
    {
        if (type == 2)
        {
            MP = (short)(MP + amount);
            if (MP < 0)
                MP = 0;
            else if (MP > MaxMP)
                MP = MaxMP;
        }
        // Monsters have no SP.
        else if (type == 3)
        {
        }
    }

    /// <summary>CNpc::DurationMagic_4 — stat buff/debuff expiry.</summary>
    public void DurationMagic_4(double currentTime)
    {
        AiZone? zone = GetMapByIndex();
        if (zone is null)
            return;

        if (DungeonFamily > 0)
        {
            // TODO(stage3.7): RoomEvent not ported — the C++ kills this NPC
            // (RegenType 0 → 2, Dead(1)) when its dungeon room status becomes 3.
        }

        for (int i = 0; i < AiConstants.MaxMagicType4; i++)
        {
            if (MagicType4[i].DurationTime != 0)
            {
                if (currentTime > MagicType4[i].StartTime + MagicType4[i].DurationTime)
                {
                    MagicType4[i].DurationTime = 0;
                    MagicType4[i].StartTime = 0.0;
                    MagicType4[i].Amount = 0;

                    // Speed-related stat: restore.
                    if (i == 5)
                    {
                        Speed1 = OldSpeed1;
                        Speed2 = OldSpeed2;
                    }
                }
            }
        }
    }

    /// <summary>CNpc::DurationMagic_3 — damage-over-time processing.</summary>
    public void DurationMagic_3(double currentTime)
    {
        for (int i = 0; i < AiConstants.MaxMagicType3; i++)
        {
            if (MagicType3[i].Duration == 0)
                continue;

            // Every 2 seconds.
            if (currentTime < MagicType3[i].StartTime + MagicType3[i].Interval)
                continue;

            MagicType3[i].Interval = (byte)(MagicType3[i].Interval + 2);

            // Healing.
            if (MagicType3[i].HpAmount >= 0)
            {
            }
            // Damage.
            else
            {
                int durationDamage = Math.Abs((int)MagicType3[i].HpAmount);

                // NPC died from the tick.
                if (!SetDamage(0, durationDamage, "**duration**", MagicType3[i].AttackUserId))
                {
                    SendExpToUserList();
                    SendDead();
                    SendAttackSuccess(MagicAttackTargetDead, MagicType3[i].AttackUserId,
                        (short)durationDamage, HP, 1, DurationAttack);
                    MagicType3[i].StartTime = 0.0;
                    MagicType3[i].Duration = 0;
                    MagicType3[i].Interval = 2;
                    MagicType3[i].HpAmount = 0;
                    MagicType3[i].AttackUserId = -1;
                }
                else
                {
                    SendAttackSuccess(AttackSuccessResult, MagicType3[i].AttackUserId,
                        (short)durationDamage, HP, 1, DurationAttack);
                }
            }

            // Total duration elapsed.
            if (currentTime >= MagicType3[i].StartTime + MagicType3[i].Duration)
            {
                MagicType3[i].StartTime = 0.0;
                MagicType3[i].Duration = 0;
                MagicType3[i].Interval = 2;
                MagicType3[i].HpAmount = 0;
                MagicType3[i].AttackUserId = -1;
            }
        }
    }

    /// <summary>CNpc::ChangeMonsterInfo — swap the NPC row for morphing monsters.</summary>
    public void ChangeMonsterInfo(int iChangeType)
    {
        // Not a morphing monster.
        if (ChangeSid == 0 || ChangeType == 0)
            return;

        if (State != NpcState.Dead)
            return;

        if (World is null)
            return;

        Data.Models.Npc? row = null;
        if (InitMoveType < 100)
        {
            if (iChangeType == 1)
                row = World.MonsterTable.GetValueOrDefault(ChangeSid);
            else if (iChangeType == 2)
                row = World.MonsterTable.GetValueOrDefault(Sid);
        }
        else
        {
            if (iChangeType == 1)
                row = World.NpcTable.GetValueOrDefault(ChangeSid);
            else if (iChangeType == 2)
                row = World.NpcTable.GetValueOrDefault(Sid);
        }

        // The C++ Load() asserts and returns on a null row.
        if (row is null)
            return;

        Load(row, false);
    }

    /// <summary>CNpc::Dead — death without exp distribution.</summary>
    public void Dead(int iDeadType = 0)
    {
        AiZone? zone = GetMapByIndex();
        if (zone is null)
            return;

        HP = 0;
        State = NpcState.Dead;
        Delay = RegenTime;
        DelayTime = TimeGet();
        FirstLive = false;
        DeadType = 100; // died during the war event

        if (RegionX > zone.RegionsX - 1 || RegionZ > zone.RegionsZ - 1)
            return;

        zone.RegionNpcRemove(RegionX, RegionZ, Nid + NpcBand);

        // Not killed by a user: notify the clients directly.
        if (iDeadType == 1)
        {
            var buf = new byte[256];
            var w = new PacketWriter(buf);
            w.SetByte(AiOpcode.AG_DEAD);
            w.SetShort(Nid + NpcBand);
            SendAll(w.Written);
        }

        // Dungeon work: morphing monsters.
        if (SpecialType == 1 || SpecialType == 4)
        {
            if (ChangeType == 0)
            {
                ChangeType = 1;
            }
            else if (ChangeType == 2)
            {
                if (DungeonFamily >= MaxDungeonBossMonster)
                    return;
            }
        }
        else
        {
            ChangeType = 100;
        }
    }

    /// <summary>CNpc::Teleport.</summary>
    public bool Teleport()
    {
        int retryCount = 0;
        const int maxRetry = 500;
        int nX, nZ = 0, nTileX, nTileZ;

        AiZone? zone = GetMapByIndex();
        if (zone is null)
            return false;

        while (true)
        {
            retryCount++;
            nX = MyRand(0, 10);
            nX = MyRand(0, 10);      // verbatim C++: nZ is never randomized...
            nX = (int)CurX + nX;
            nZ = (int)CurZ + nZ;     // ...and accumulates CurZ per retry
            nTileX = nX / TileSize;
            nTileZ = nZ / TileSize;

            if (nTileX >= zone.Map.MapSize - 1)
                nTileX = zone.Map.MapSize - 1;

            if (nTileZ >= zone.Map.MapSize - 1)
                nTileZ = zone.Map.MapSize - 1;

            if (nTileX < 0 || nTileZ < 0)
                return false;

            if (zone.Map.TileEvents[nTileX, nTileZ] <= 0)
            {
                if (retryCount >= maxRetry)
                    return false;

                continue;
            }

            break;
        }

        var outBuf = new byte[256];
        var outW = new PacketWriter(outBuf);
        outW.SetByte(AiOpcode.AG_NPC_INOUT);
        outW.SetByte(NpcOut);
        outW.SetShort(Nid + NpcBand);
        outW.SetFloat(CurX);
        outW.SetFloat(CurZ);
        outW.SetFloat(CurY);
        SendAll(outW.Written);

        CurX = nX;
        CurZ = nZ;

        var inBuf = new byte[256];
        var inW = new PacketWriter(inBuf);
        inW.SetByte(AiOpcode.AG_NPC_INOUT);
        inW.SetByte(NpcIn);
        inW.SetShort(Nid + NpcBand);
        inW.SetFloat(CurX);
        inW.SetFloat(CurZ);
        inW.SetFloat(0);
        SendAll(inW.Written);

        SetUid(CurX, CurZ, Nid + NpcBand);

        return true;
    }

    // ------------------------------------------------------------------
    //  Combat hooks — stage 3.7 part 2. Stubs return the C++ "no action"
    //  values so the movement call sites above stay faithful.
    // ------------------------------------------------------------------

    /// <summary>TODO(stage3.7-part2): CNpc::FindEnemy — enemy acquisition scan.</summary>
    public bool FindEnemy() => false;

    /// <summary>
    /// TODO(stage3.7-part2): CNpc::Attack — returns the next attack delay.
    /// (Renamed: the <see cref="Attack"/> stat field already claims the name.)
    /// </summary>
    public int DoAttack() => AttackDelay;

    /// <summary>TODO(stage3.7-part2): CNpc::LongAndMagicAttack.</summary>
    public int LongAndMagicAttack() => AttackDelay;

    /// <summary>TODO(stage3.7-part2): CNpc::TracingAttack (0: target lost, 1: keep tracing).</summary>
    public int TracingAttack() => 1;

    /// <summary>
    /// TODO(stage3.7-part2): CNpc::IsSurround — claims an 8-direction attack slot via
    /// CUser::IsSurroundCheck. 0 keeps AttackPos unset (CalcAdaptivePosition branch).
    /// </summary>
    public int IsSurround(AiUser user)
    {
        _ = user;
        return 0;
    }

    /// <summary>TODO(stage3.7-part2): CNpc::SetDamage (false = the NPC died).</summary>
    public bool SetDamage(int attackType, int damage, string sourceName, int uid)
    {
        _ = attackType;
        _ = damage;
        _ = sourceName;
        _ = uid;
        return true;
    }

    /// <summary>TODO(stage3.7-part2): CNpc::SendDead.</summary>
    public int SendDead(int type = 1)
    {
        _ = type;
        return 0;
    }

    /// <summary>TODO(stage3.7-part2): CNpc::SendExpToUserList — exp distribution.</summary>
    public void SendExpToUserList()
    {
    }
}

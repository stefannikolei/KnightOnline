using OpenKO.Data.Models;

namespace OpenKO.Servers.AIServer.Ai;

/// <summary>
/// Port of <c>CNpc</c> (Server/AIServer/Npc.h) — data model. The AI method bodies
/// (Npc.cpp, ~7600 lines) are ported incrementally into partial-class files
/// (Npc.Movement.cs, Npc.Combat.cs, …).
/// </summary>
public partial class Npc
{
    /// <summary>m_MagicProcess — the C++ ctor points it back at this NPC.</summary>
    public readonly NpcMagicProcessor MagicProcess;

    public Npc() => MagicProcess = new NpcMagicProcessor(this);

    // ---- runtime combat/target state ----
    public NpcTarget Target;
    public short ItemUserLevel;
    public int TotalDamage;
    public readonly ExpUserEntry[] DamagedUserList = new ExpUserEntry[AiConstants.NpcMaxUserList];
    public short MaxDamageUserId;

    // ---- movement/pattern state ----
    public readonly PatternPos[] PathList = new PatternPos[AiConstants.NpcMaxPathList];
    public PatternPos PatternPos;
    public short PatternFrame;
    public byte MoveType;
    public byte InitMoveType;
    public short PathCount;
    public short MaxPathCount;

    public bool FirstLive = true;
    public NpcState State = NpcState.Live; // C++ ctor starts at NPC_LIVE
    public short ZoneIndex;
    public short Nid;

    public float InitX;
    public float InitY;
    public float InitZ;

    public short CurZone;
    public float CurX;
    public float CurY;
    public float CurZ;

    public float PrevX;
    public float PrevY;
    public float PrevZ;

    // ---- pathfind window ----
    public short MinX;
    public short MinY;
    public short MaxX;
    public short MaxY;
    public readonly int[] PathMap = new int[AiConstants.MaxMapArraySize];
    public int MapSizeX;
    public int MapSizeY;
    public float StartPointX;
    public float StartPointY;
    public float EndPointX;
    public float EndPointY;
    public short StepCount;
    public readonly PathFinder PathFind = new();
    public PathFinder.PathNode? Path;
    public int InitMinX;
    public int InitMinY;
    public int InitMaxX;
    public int InitMaxY;

    // ---- duration magic ----
    public double HpChangeTime;
    public double FaintingTime;
    public readonly NpcMagicType3[] MagicType3 = new NpcMagicType3[AiConstants.MaxMagicType3];
    public readonly NpcMagicType4[] MagicType4 = new NpcMagicType4[AiConstants.MaxMagicType4];

    // ---- K_NPC / K_MONSTER row (Load copies these) ----
    public short Sid;
    public string Name = string.Empty;
    public short Pid;
    public short Size = 100;
    public int Weapon1;
    public int Weapon2;
    public byte Group;
    public byte ActType;
    public byte Rank;
    public byte Title;
    public int SellingGroup;
    public short Level;
    public int Exp;
    public int Loyalty;
    public int MaxHP;
    public short MaxMP;
    public short Attack;
    public short Defense;
    public short HitRate;
    public short EvadeRate;
    public short Damage;
    public short AttackDelay;
    public short Speed;
    public float Speed1;
    public float Speed2;
    public short StandTime;
    public int Magic1;
    public int Magic2;
    public int Magic3;
    public short FireResist;
    public short ColdResist;
    public short LightningResist;
    public short MagicResist;
    public short DiseaseResist;
    public short PoisonResist;
    public short LightResist;
    public float Bulk;
    public byte SearchRange;
    public byte AttackRange;
    public byte TracingRange;
    public byte NpcType;          // 0: monster, 1: NPC
    public short FamilyType;
    public byte MoneyType;
    public int Money;
    public int Item;

    public int HP;
    public short MP;
    public float SecForMeter;

    // ---- AI behavior flags ----
    public byte LongType;         // 0 melee, 1 ranged, 2 both
    public byte AttType;          // 1 aggressive, 0 passive
    public byte OldAttType;
    public byte GroupType;
    public byte EndAttType;
    public byte AttackPos;
    public byte BattlePos;
    public byte WhatAttackType;
    public byte GateOpen;
    public byte MaxDamagedNation;
    public byte ObjectType;
    public byte DungeonFamily;
    public byte SpecialType;
    public byte TrapNumber;
    public byte ChangeType;
    public byte RegenType;
    public byte DeadType;
    public short ChangeSid;
    public short ControlSid;

    // ---- K_NPCPOS row ----
    public int Delay;
    public double DelayTime;
    public byte PosType;          // m_byType
    public int RegenTime;
    public int LimitMinX;
    public int LimitMinZ;
    public int LimitMaxX;
    public int LimitMaxZ;
    public float AddX;
    public float AddZ;
    public float BattlePosX;
    public float BattlePosZ;
    public float SecForRealMoveMeter;
    public byte Direction;
    public bool PathFlag;

    // ---- movement bookkeeping ----
    public short AniFrameIndex;
    public short AniFrameCount;
    public byte PathCounter;      // m_byPathCount
    public byte ResetFlag;
    public byte ActionFlag;
    public short RegionX;
    public short RegionZ;
    public readonly short[] FindX = new short[4];
    public readonly short[] FindY = new short[4];
    public float OldSpeed1;
    public float OldSpeed2;
    public short ThreadNumber;

    /// <summary>
    /// Port of <c>CNpc::Load</c> (exact field mapping; note it does NOT set Sid —
    /// that comes from the K_NPCPOS spawn row). <paramref name="transformSpeeds"/>
    /// scales monster speeds by MONSTER_SPEED/1000 like GetMonsterTableData.
    /// </summary>
    public void Load(Data.Models.Npc row, bool transformSpeeds)
    {
        const short MonsterSpeed = 1500;

        Name = row.Name;
        Pid = row.PictureId;
        Size = row.Size;
        Weapon1 = row.Weapon1;
        Weapon2 = row.Weapon2;
        Group = row.Group;
        ActType = row.ActType;
        Rank = row.Rank;
        Title = row.Title;
        SellingGroup = row.SellingGroup;
        Level = row.Level;
        Exp = row.Exp;
        Loyalty = row.Loyalty;
        HP = row.HitPoints;
        MaxHP = row.HitPoints;
        MP = row.ManaPoints;
        MaxMP = row.ManaPoints;
        Attack = row.Attack;
        Defense = row.Armor;
        HitRate = row.HitRate;
        EvadeRate = row.EvadeRate;
        Damage = row.Damage;
        AttackDelay = row.AttackDelay;

        Speed = MonsterSpeed;

        Speed1 = row.WalkSpeed;
        Speed2 = row.RunSpeed;
        OldSpeed1 = row.WalkSpeed;
        OldSpeed2 = row.RunSpeed;

        if (transformSpeeds)
        {
            const float dbSpeed = MonsterSpeed;
            Speed1 *= dbSpeed / 1000.0f;
            Speed2 *= dbSpeed / 1000.0f;
            OldSpeed1 *= dbSpeed / 1000.0f;
            OldSpeed2 *= dbSpeed / 1000.0f;
        }

        StandTime = row.StandTime;
        Magic1 = row.Magic1;
        Magic2 = row.Magic2;
        Magic3 = row.Magic3;
        FireResist = row.FireResist;
        ColdResist = row.ColdResist;
        LightningResist = row.LightningResist;
        MagicResist = row.MagicResist;
        DiseaseResist = row.DiseaseResist;
        PoisonResist = row.PoisonResist;
        LightResist = row.LightResist;
        Bulk = (float)((double)row.Bulk / 100 * ((double)row.Size / 100));
        SearchRange = row.SearchRange;
        AttackRange = row.AttackRange;
        TracingRange = row.TracingRange;
        NpcType = row.Type;
        FamilyType = row.Family;
        Money = row.Money;
        Item = row.Item;
        LongType = row.DirectAttack;
        WhatAttackType = row.DirectAttack;
    }
}

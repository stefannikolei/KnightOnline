namespace OpenKO.Servers.AIServer.Ai;

/// <summary>
/// Port of the AIServer-side <c>CUser</c> (Server/AIServer/User.h) — the mirror
/// of a player the game server reports via AG_USER_* packets. Method bodies
/// (combat/exp/heal, User.cpp) are ported into partial-class files.
/// </summary>
public partial class AiUser
{
    public string UserId = string.Empty;   // m_strUserID
    public int Uid;                        // m_iUserId
    public byte Live;                      // m_bLive

    public float CurX;
    public float CurY;
    public float CurZ;
    public float WillX;
    public float WillY;
    public float WillZ;
    public short Speed;
    public byte CurZone;
    public short ZoneIndex;

    public byte Nation;
    public byte Level;

    public short HP;
    public short MP;
    public short SP;
    public short MaxHP;
    public short MaxMP;
    public short MaxSP;

    public byte State;

    public short RegionX;
    public short RegionZ;
    public short OldRegionX;
    public short OldRegionZ;

    public byte ResHp;
    public byte ResMp;
    public byte ResSta;

    public byte NowParty;        // 1 party, 2 troop, 0 none
    public byte PartyTotalMan;
    public short PartyTotalLevel;
    public short PartyNumber;

    public short HitDamage;
    public float HitRate;
    public float AvoidRate;
    public short AC;
    public short ItemAC;

    public readonly short[] SurroundNpcNumber = new short[8];

    public byte IsOperator;
    public bool LoggingOut;

    public byte MagicTypeLeftHand;
    public byte MagicTypeRightHand;
    public short MagicAmountLeftHand;
    public short MagicAmountRightHand;

    /// <summary>Port of CUser::Initialize.</summary>
    public void Initialize()
    {
        UserId = string.Empty;
        Uid = -1;
        Live = 0;
        CurX = CurY = CurZ = 0;
        WillX = WillY = WillZ = 0;
        Speed = 0;
        CurZone = 0;
        ZoneIndex = 0;
        Nation = 0;
        Level = 0;
        HP = MP = SP = MaxHP = MaxMP = MaxSP = 0;
        State = 0;
        RegionX = RegionZ = OldRegionX = OldRegionZ = 0;
        ResHp = ResMp = ResSta = 0;
        NowParty = 0;
        PartyTotalMan = 0;
        PartyTotalLevel = 0;
        PartyNumber = -1;
        HitDamage = 0;
        HitRate = 0;
        AvoidRate = 0;
        AC = 0;
        ItemAC = 0;
        Array.Clear(SurroundNpcNumber);
        IsOperator = 0;
        LoggingOut = false;
        MagicTypeLeftHand = MagicTypeRightHand = 0;
        MagicAmountLeftHand = MagicAmountRightHand = 0;
    }
}

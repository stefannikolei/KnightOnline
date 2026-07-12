using OpenKO.Core.Protocol;
using OpenKO.Data.Models;
using OpenKO.Network;

namespace OpenKO.Servers.Ebenezer;

/// <summary>
/// The CUser respawn slice (User.cpp): Regene (WIZ_REGENE respawn +
/// resurrection), the blinking invulnerability window and the in-zone warp.
/// </summary>
public sealed partial class GameUser
{
    public const byte UserWarp = 0x04;   // USER_WARP

    private const int BlinkTime = 10;             // BLINK_TIME (seconds)
    private const byte RegeneNormal = 0;          // REGENE_NORMAL
    private const byte RegeneMagic = 1;           // REGENE_MAGIC
    private const int ResurrectionStoneId = 379006000;
    private const int ResurrectionStoneMagic = 490041;

    /// <summary>m_fBlinkStartTime.</summary>
    public double BlinkStartTime;

    /// <summary>m_bRegeneType.</summary>
    public byte RegeneType = RegeneNormal;

    /// <summary>CUser::Regene — respawn/resurrect (magicId &gt; 0 = clerical resurrection).</summary>
    public void Regene(ReadOnlySpan<byte> body, int magicId = 0)
    {
        if (UserData is not { } user)
            return;

        InitType3();
        InitType4();

        var reader = new PacketReader(body);
        byte regeneType = reader.GetByte();

        if (regeneType != 1 && regeneType != 2)
            regeneType = 1;

        if (regeneType == 2)
        {
            magicId = ResurrectionStoneMagic;

            if (ItemCountChange(ResurrectionStoneId, 1, 3 * user.Level) < 2)
                return;

            if (user.Level <= 5)
                return;
        }

        Home? home = world.HomeTable.GetValueOrDefault(user.Nation);
        if (home is null)
            return;

        GameZone? map = world.GetZoneByIndex(ZoneIndex);
        if (map is null)
            return;

        UserInOut(UserOut);

        float x = world.Rand(0, 400) / 100.0f;
        float z = world.Rand(0, 400) / 100.0f;

        if (x < 2.5f)
            x += 1.5f;

        if (z < 2.5f)
            z += 1.5f;

        ObjectEvent? bindEvent = map.GetObjectEvent(user.Bind);

        if (magicId == 0)
        {
            if (bindEvent is { Life: 1 })
            {
                // The bind point stores its position on the event once the SMD
                // map loader lands; until then Life stays 0 for all events.
                user.CurX = WillX = bindEvent.PosX + x;
                user.CurZ = WillZ = bindEvent.PosZ + z;
                user.CurY = 0;
            }
            else if (user.Nation != user.Zone)
            {
                if (user.Zone > 200)
                {
                    x = home.FreeZoneX + world.Rand(0, home.FreeZoneLX);
                    z = home.FreeZoneZ + world.Rand(0, home.FreeZoneLZ);
                }
                else if (user.Zone is > 100 and < 200)
                {
                    x = home.BattleZoneX + world.Rand(0, home.BattleZoneLX);
                    z = home.BattleZoneZ + world.Rand(0, home.BattleZoneLZ);

                    if (user.Zone == ZoneSnowBattle)
                    {
                        x = home.FreeZoneX + world.Rand(0, home.FreeZoneLX);
                        z = home.FreeZoneZ + world.Rand(0, home.FreeZoneLZ);
                    }
                }
                else if (user.Zone is > 10 and < 20)
                {
                    x = 527 + world.Rand(0, 10);
                    z = 543 + world.Rand(0, 10);
                }
                else if (user.Zone < 3)
                {
                    if (user.Nation == Karus)
                    {
                        x = home.ElmoZoneX + world.Rand(0, home.ElmoZoneLX);
                        z = home.ElmoZoneZ + world.Rand(0, home.ElmoZoneLZ);
                    }
                    else if (user.Nation == Elmorad)
                    {
                        x = home.KarusZoneX + world.Rand(0, home.KarusZoneLX);
                        z = home.KarusZoneZ + world.Rand(0, home.KarusZoneLZ);
                    }
                    else
                    {
                        return;
                    }
                }

                user.CurX = x;
                user.CurZ = z;
            }
            else
            {
                if (user.Nation == Karus)
                {
                    x = home.KarusZoneX + world.Rand(0, home.KarusZoneLX);
                    z = home.KarusZoneZ + world.Rand(0, home.KarusZoneLZ);
                }
                else if (user.Nation == Elmorad)
                {
                    x = home.ElmoZoneX + world.Rand(0, home.ElmoZoneLX);
                    z = home.ElmoZoneZ + world.Rand(0, home.ElmoZoneLZ);
                }
                else
                {
                    return;
                }

                user.CurX = x;
                user.CurZ = z;
            }
        }

        var buffer = new byte[16];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_REGENE);
        writer.SetShort((short)((ushort)user.CurX * 10)); // cast-before-scale like the C++
        writer.SetShort((short)((ushort)user.CurZ * 10));
        writer.SetShort((short)((short)user.CurY * 10));
        Send(writer.Written);

        // Clerical resurrection.
        if (magicId > 0)
        {
            MagicType5? type5 = world.MagicType5Table.GetValueOrDefault(magicId);
            if (type5 is null)
                return;

            AbnormalType = AbnormalBlinking;
            ResHpType = UserStanding;
            BlinkStartTime = world.Clock();
            MSpChange(-MaxMp); // empty out MP

            if (WhoKilledMe == -1 && regeneType == 1)
                ExpChange(LostExp * type5.ExpRecover / 100);

            RegeneType = RegeneMagic;
        }
        else
        {
            AbnormalType = AbnormalBlinking;
            BlinkStartTime = world.Clock();
            ResHpType = UserStanding;
            RegeneType = RegeneNormal;
        }

        LastRegeneTime = world.Clock();
        WhoKilledMe = -1;
        LostExp = 0;

        // C++ quirk kept as-is: this AI notify is dead code — the abnormal type
        // is always ABNORMAL_BLINKING at this point (BlinkTimeCheck notifies).
        if (AbnormalType != AbnormalBlinking)
        {
            var aiBuffer = new byte[8];
            var aiWriter = new PacketWriter(aiBuffer);
            aiWriter.SetByte(AiOpcode.AG_USER_REGENE);
            aiWriter.SetShort(SocketId);
            aiWriter.SetShort(user.Hp);
            world.SendToAiServer?.Invoke(user.Zone, aiWriter.Written.ToArray());
        }

        RegionX = (short)(user.CurX / GameZone.ViewDistance);
        RegionZ = (short)(user.CurZ / GameZone.ViewDistance);

        UserInOut(UserRegene);

        world.RegionUserInOutForMe(this);
        world.RegionNpcInfoForMe(this);

        StateChange([3, AbnormalType]);

        // WIZ_PARTY/PARTY_STATUSCHANGE attaches with the party slice.
    }

    /// <summary>CUser::BlinkTimeCheck — end the invulnerability, refill and notify the AI.</summary>
    public void BlinkTimeCheck(double currentTime)
    {
        if (UserData is not { } user)
            return;

        if (currentTime - BlinkStartTime <= BlinkTime)
            return;

        BlinkStartTime = 0.0;
        AbnormalType = AbnormalNormalState;

        if (RegeneType == RegeneMagic)
            HpChange(MaxHp / 2);
        else
            HpChange(MaxHp);

        RegeneType = RegeneNormal;

        StateChange([3, AbnormalNormalState]);

        var buffer = new byte[8];
        var writer = new PacketWriter(buffer);
        writer.SetByte(AiOpcode.AG_USER_REGENE);
        writer.SetShort(SocketId);
        writer.SetShort(user.Hp);
        world.SendToAiServer?.Invoke(user.Zone, writer.Written.ToArray());

        SendAiUserInOut(UserRegene, user);
    }

    private const byte AbnormalNormalState = 1; // ABNORMAL_NORMAL

    /// <summary>
    /// CUser::Warp — the in-zone position jump ([x*10 u16][z*10 u16] body).
    /// Named WarpProcess because the m_bWarp flag already claims "Warp".
    /// </summary>
    public void WarpProcess(ReadOnlySpan<byte> body)
    {
        if (Warp != 0)
            return;

        if (UserData is not { } user)
            return;

        var reader = new PacketReader(body);
        var warpX = (ushort)reader.GetShort();
        var warpZ = (ushort)reader.GetShort();

        GameZone? map = world.GetZoneByIndex(ZoneIndex);
        if (map is null)
            return;

        float realX = warpX / 10.0f;
        float realZ = warpZ / 10.0f;

        if (!map.IsValidPosition(realX, realZ))
            return;

        var buffer = new byte[8];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_WARP);
        writer.SetShort((short)warpX);
        writer.SetShort((short)warpZ);
        Send(writer.Written);

        UserInOut(UserOut);

        user.CurX = WillX = realX;
        user.CurZ = WillZ = realZ;

        RegionX = (short)(user.CurX / GameZone.ViewDistance);
        RegionZ = (short)(user.CurZ / GameZone.ViewDistance);

        UserInOut(UserWarp);
        world.RegionUserInOutForMe(this);
        world.RegionNpcInfoForMe(this);
    }
}

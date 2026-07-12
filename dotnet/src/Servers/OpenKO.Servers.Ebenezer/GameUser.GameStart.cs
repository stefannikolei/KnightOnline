using System.Text;
using Microsoft.Extensions.Logging;
using OpenKO.Core.Protocol;
using OpenKO.Data;
using OpenKO.Data.Models;
using OpenKO.Network;

namespace OpenKO.Servers.Ebenezer;

/// <summary>
/// CUser::GameStart and its send helpers (SendMyInfo, SendTimeStatus,
/// SendNotice, SetZoneAbilityChange).
/// </summary>
public sealed partial class GameUser
{
    private const byte Karus = 1;   // KARUS / nation
    private const byte Elmorad = 2; // ELMORAD

    private const int ZoneMoradon = 21;
    private const int ZoneDelos = 30;
    private const int ZoneDesperationAbyss = 32;
    private const int ZoneHellAbyss = 33;
    private const int ZoneArena = 48;
    private const int ZoneCaitharosArena = 55;

    // e_ZoneAbility (shared/globals.h).
    private const byte ZoneAbilityNeutral = 0;
    private const byte ZoneAbilityPvp = 1;
    private const byte ZoneAbilitySiegeDisabled = 6;
    private const byte ZoneAbilityCaitharosArena = 7;
    private const byte ZoneAbilityPvpNeutralNpcs = 8;
    private const byte ZoneAbilityUpdate = 1; // ZONE_ABILITY_UPDATE

    /// <summary>CUser::GameStart — opcode 1 (loading) / 2 (finished loading).</summary>
    public void GameStart(ReadOnlySpan<byte> body)
    {
        if (UserData is not { } user)
            return;

        int opcode = body[0];

        if (opcode == 1)
        {
            SendMyInfo(0);
            world.UserInOutForMe(this);
            world.NpcInOutForMe(this);
            SendNotice();
            SendTimeStatus();

            logger.LogDebug("GameStart: loading [charId={CharId} socketId={SocketId}]", user.CharId, SocketId);

            Send([(byte)GameOpcode.WIZ_GAMESTART]);
        }
        else if (opcode == 2)
        {
            State = ConnectionState.GameStart;

            logger.LogDebug("GameStart: in game [charId={CharId} socketId={SocketId}]", user.CharId, SocketId);

            UserInOut(UserRegene);

            if (user.City == 0 && user.Hp <= 0)
                user.City = 0xFF;

            if (user.City is 0 or 0xFF)
            {
                LostExp = 0;
            }
            else
            {
                int level = user.Level;
                if (user.City <= 100)
                    --level;

                // m_LevelUpTableArray[level] → the LEVEL_UP row for level + 1.
                LostExp = world.LevelUpTable.GetValueOrDefault(level + 1);
                LostExp = LostExp * (user.City % 10) / 100;

                if (user.City % 100 / 10 == 1)
                    LostExp /= 2;
            }

            if (LostExp > 0 || user.City == 0xFF)
                HpChange(-MaxHp);

            SendMyInfo(2); // no-op upstream: SendMyInfo early-returns for type != 0

            SetUserAbility();

            // Permanent chat broadcast attaches with the chat slice.
        }
    }

    /// <summary>
    /// CUser::SendMyInfo — the WIZ_MYINFO detail blob plus the AG_USER_INFO push
    /// to the AI server. Quirk kept: the upstream marked the type handling TODO
    /// and returns immediately for any type other than 0.
    /// </summary>
    public void SendMyInfo(int type)
    {
        if (type != 0)
            return;

        if (UserData is not { } user)
            return;

        GameZone? zone = ZoneIndex >= 0 && ZoneIndex < world.Zones.Count ? world.Zones[ZoneIndex] : null;
        if (zone is null)
            return;

        // Out-of-map positions respawn at the nation's HOME rectangle.
        if (!zone.IsValidPosition(user.CurX, user.CurZ))
        {
            Home? home = world.HomeTable.GetValueOrDefault(user.Nation);
            if (home is null)
                return;

            int x, z;
            if (user.Nation != user.Zone && user.Zone > 200)
            {
                x = home.FreeZoneX + Rand(home.FreeZoneLX);
                z = home.FreeZoneZ + Rand(home.FreeZoneLZ);
            }
            else if (user.Nation != user.Zone && user.Zone < 3)
            {
                if (user.Nation == Karus)
                {
                    x = home.ElmoZoneX + Rand(home.ElmoZoneLX);
                    z = home.ElmoZoneZ + Rand(home.ElmoZoneLZ);
                }
                else if (user.Nation == Elmorad)
                {
                    x = home.KarusZoneX + Rand(home.KarusZoneLX);
                    z = home.KarusZoneZ + Rand(home.KarusZoneLZ);
                }
                else
                {
                    return;
                }
            }
            else
            {
                if (user.Nation == Karus)
                {
                    x = home.KarusZoneX + Rand(home.KarusZoneLX);
                    z = home.KarusZoneZ + Rand(home.KarusZoneLZ);
                }
                else if (user.Nation == Elmorad)
                {
                    x = home.ElmoZoneX + Rand(home.ElmoZoneLX);
                    z = home.ElmoZoneZ + Rand(home.ElmoZoneLZ);
                }
                else
                {
                    return;
                }
            }

            user.CurX = x;
            user.CurZ = z;
        }

        var buffer = new byte[2048];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_MYINFO);
        writer.SetShort(SocketId);
        writer.SetString1(Encoding.Latin1.GetBytes(user.CharId));

        writer.SetShort((short)(ushort)(user.CurX * 10));
        writer.SetShort((short)(ushort)(user.CurZ * 10));
        writer.SetShort((short)(user.CurY * 10));

        writer.SetByte(user.Nation);
        writer.SetByte(user.Race);
        writer.SetShort(user.Class);
        writer.SetByte(user.Face);
        writer.SetByte(user.HairColor);
        writer.SetByte(user.Rank);
        writer.SetByte(user.Title);
        writer.SetByte(user.Level);
        writer.SetByte(user.Points);
        writer.SetDWord((uint)MaxExp);
        writer.SetDWord((uint)user.Exp);
        writer.SetDWord((uint)user.Loyalty);
        writer.SetDWord((uint)user.LoyaltyMonthly);
        writer.SetByte(user.City);
        writer.SetShort(user.Knights);
        writer.SetByte(user.Fame);

        // Knights lookup attaches with the KnightsManager slice; the empty
        // clan block matches the C++ null path.
        writer.SetShort(0);   // alliance knights
        writer.SetByte(0);    // flag
        writer.SetByte(0);    // name (empty SetString1)
        writer.SetByte(0);    // grade
        writer.SetByte(0);    // ranking
        writer.SetShort(0);   // mark version
        writer.SetShort(-1);  // cape

        writer.SetShort(MaxHp);
        writer.SetShort(user.Hp);
        writer.SetShort(MaxMp);
        writer.SetShort(user.Mp);
        writer.SetShort(GetMaxWeightForClient());
        writer.SetShort(GetCurrentWeightForClient());
        writer.SetByte(user.Str);
        writer.SetByte((byte)ItemStr);
        writer.SetByte(user.Sta);
        writer.SetByte((byte)ItemSta);
        writer.SetByte(user.Dex);
        writer.SetByte((byte)ItemDex);
        writer.SetByte(user.Intel);
        writer.SetByte((byte)ItemIntel);
        writer.SetByte(user.Cha);
        writer.SetByte((byte)ItemCham);
        writer.SetShort(TotalHit);
        writer.SetShort(TotalAc);
        writer.SetByte(FireR);
        writer.SetByte(ColdR);
        writer.SetByte(LightningR);
        writer.SetByte(MagicR);
        writer.SetByte(DiseaseR);
        writer.SetByte(PoisonR);
        writer.SetDWord((uint)user.Gold);
        writer.SetByte(user.Authority);

        writer.SetByte(0); // knights rank
        writer.SetByte(0); // personal rank

        for (int i = 0; i < GameConstants.MaxSkills; i++)
            writer.SetByte(user.Skills[i]);

        for (int i = 0; i < GameConstants.InventoryTotal; i++)
        {
            writer.SetDWord((uint)user.Items[i].Num);
            writer.SetShort(user.Items[i].Duration);
            writer.SetShort(user.Items[i].Count);
            writer.SetByte(user.Items[i].Flag);
            writer.SetShort((short)user.Items[i].TimeRemaining);
        }

        writer.SetByte(0); // account status
        writer.SetByte(user.PremiumType);
        writer.SetShort(user.PremiumTime);
        writer.SetByte(0); // is chicken
        writer.SetDWord((uint)user.MannerPoint);

        Send(writer.Written);

        SetZoneAbilityChange(user.Zone);

        // AG_USER_INFO push to the AI server.
        var aiBuffer = new byte[256];
        var aiWriter = new PacketWriter(aiBuffer);
        aiWriter.SetByte(AiOpcode.AG_USER_INFO);
        aiWriter.SetShort(SocketId);
        aiWriter.SetString2(Encoding.Latin1.GetBytes(user.CharId));
        aiWriter.SetByte(user.Zone);
        aiWriter.SetShort(ZoneIndex);
        aiWriter.SetByte(user.Nation);
        aiWriter.SetByte(user.Level);
        aiWriter.SetShort(user.Hp);
        aiWriter.SetShort(user.Mp);
        aiWriter.SetShort(TotalHit * AttackAmount / 100);
        aiWriter.SetShort(TotalAc + AcAmount);
        aiWriter.SetFloat(TotalHitRate);
        aiWriter.SetFloat(TotalEvasionRate);
        aiWriter.SetShort(ItemAc);
        aiWriter.SetByte(MagicTypeLeftHand);
        aiWriter.SetByte(MagicTypeRightHand);
        aiWriter.SetShort(MagicAmountLeftHand);
        aiWriter.SetShort(MagicAmountRightHand);
        aiWriter.SetByte(user.Authority);
        world.SendToAiServer?.Invoke(user.Zone, aiWriter.Written.ToArray());
    }

    /// <summary>CUser::SendTimeStatus — WIZ_TIME + WIZ_WEATHER.</summary>
    public void SendTimeStatus()
    {
        var buffer = new byte[16];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_TIME);
        writer.SetShort(world.Year);
        writer.SetShort(world.Month);
        writer.SetShort(world.Date);
        writer.SetShort(world.Hour);
        writer.SetShort(world.Minute);
        Send(writer.Written);

        var weather = new byte[8];
        var weatherWriter = new PacketWriter(weather);
        weatherWriter.SetByte((byte)GameOpcode.WIZ_WEATHER);
        weatherWriter.SetByte((byte)world.Weather);
        weatherWriter.SetShort(world.WeatherAmount);
        Send(weatherWriter.Written);
    }

    /// <summary>CUser::SendNotice — the WIZ_NOTICE line list.</summary>
    public void SendNotice()
    {
        var buffer = new byte[2048];
        var writer = new PacketWriter(buffer) { Index = 2 };
        int count = 0;

        for (int i = 0; i < world.Notices.Length; i++)
        {
            if (string.IsNullOrEmpty(world.Notices[i]))
                continue;

            writer.SetString1(Encoding.Latin1.GetBytes(world.Notices[i]));
            count++;
        }

        buffer[0] = (byte)GameOpcode.WIZ_NOTICE;
        buffer[1] = (byte)count;
        Send(buffer.AsSpan(0, writer.Index));
    }

    /// <summary>CUser::SetZoneAbilityChange — WIZ_ZONEABILITY flags + tariff.</summary>
    public void SetZoneAbilityChange(int zone)
    {
        const short tariffBase = 10;

        bool canTradeWithOtherNation = false;
        bool canTalkToOtherNation = false;
        byte zoneAbilityType = ZoneAbilityNeutral;
        short tariff = tariffBase;

        if (zone == ZoneMoradon || (zone / 10 == 5 && zone != ZoneCaitharosArena))
        {
            canTradeWithOtherNation = true;
            zoneAbilityType = ZoneAbilityNeutral;
            canTalkToOtherNation = true;
        }
        else
        {
            switch (zone)
            {
                case ZoneArena:
                    zoneAbilityType = ZoneAbilityNeutral;
                    canTalkToOtherNation = true;
                    break;

                case ZoneCaitharosArena:
                    zoneAbilityType = ZoneAbilityCaitharosArena;
                    canTalkToOtherNation = true;
                    break;

                case ZoneDesperationAbyss:
                case ZoneHellAbyss:
                    zoneAbilityType = ZoneAbilityPvpNeutralNpcs;
                    canTalkToOtherNation = true;
                    break;

                case ZoneFrontier:
                    zoneAbilityType = ZoneAbilityPvp;
                    tariff = tariffBase + 10;
                    break;

                case ZoneDelos:
                    canTradeWithOtherNation = true;
                    zoneAbilityType = ZoneAbilitySiegeDisabled;
                    canTalkToOtherNation = true;
                    break;

                default:
                    zoneAbilityType = ZoneAbilityPvp;
                    break;
            }
        }

        var buffer = new byte[8];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_ZONEABILITY);
        writer.SetByte(ZoneAbilityUpdate);
        writer.SetByte(canTradeWithOtherNation ? (byte)1 : (byte)0);
        writer.SetByte(zoneAbilityType);
        writer.SetByte(canTalkToOtherNation ? (byte)1 : (byte)0);
        writer.SetShort(tariff);
        Send(writer.Written);
    }

    /// <summary>myrand(0, n) on the world's shared RNG.</summary>
    private static int Rand(int maxInclusive)
        => maxInclusive <= 0 ? 0 : Random.Shared.Next(0, maxInclusive + 1);
}

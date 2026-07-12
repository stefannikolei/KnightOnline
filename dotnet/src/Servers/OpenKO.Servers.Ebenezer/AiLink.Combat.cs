using System.Text;
using Microsoft.Extensions.Logging;
using OpenKO.Core.Protocol;
using OpenKO.Network;

namespace OpenKO.Servers.Ebenezer;

/// <summary>
/// The AISocket combat handlers (AISocket.cpp): attack results, magic echoes,
/// exp/loyalty grants, loot drops and the desync repair path.
/// </summary>
public sealed partial class AiLink
{
    private MagicProcessor? _magicProcess;

    /// <summary>The link's CMagicProcess (m_pSrcUser stays null — NPC-cast magic only).</summary>
    public MagicProcessor MagicProcess => _magicProcess ??= new MagicProcessor(world, null, logger);

    private const byte MagicAttack = 2;    // MAGIC_ATTACK (attack type)
    private const byte DurationAttack = 3; // DURATION_ATTACK

    private const byte MagicCasting = 1;   // MAGIC_CASTING
    private const byte MagicEffecting = 3; // MAGIC_EFFECTING

    private const short SendAllTarget = 0x03; // SEND_ALL

    private const byte NpcTypePatrolGuard = 12; // NPC_PATROL_GUARD

    /// <summary>CAISocket::RecvNpcAttack — AG_ATTACK_RESULT.</summary>
    private void RecvNpcAttack(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        byte type = reader.GetByte();
        byte result = reader.GetByte();
        short sid = reader.GetShort();
        short tid = reader.GetShort();
        short damage = reader.GetShort();
        reader.GetDWord(); // AI-side HP, only logged by the C++
        byte attackType = reader.GetByte();

        // user attack -> npc
        if (type == 0x01)
        {
            GameNpc? npc = world.Npcs.GetValueOrDefault(tid);
            if (npc is null)
                return;

            npc.HP -= damage;
            if (npc.HP < 0)
                npc.HP = 0;

            var buffer = new byte[16];
            var writer = new PacketWriter(buffer);

            if (result == 0x04)
            {
                // Death by magic broadcasts WIZ_DEAD instead of the attack echo.
                writer.SetByte((byte)GameOpcode.WIZ_DEAD);
                writer.SetShort(tid);
                world.SendRegion(writer.Written, npc.CurZone, npc.RegionX, npc.RegionZ, except: null, direct: false);
            }
            else
            {
                writer.SetByte((byte)GameOpcode.WIZ_ATTACK);
                writer.SetByte(attackType);
                writer.SetByte(result);
                writer.SetShort(sid);
                writer.SetShort(tid);
                world.SendRegion(writer.Written, npc.CurZone, npc.RegionX, npc.RegionZ, except: null, direct: false);
            }

            GameUser? attacker = sid >= 0 && sid < world.Users.Length ? world.Users[sid] : null;
            if (attacker is not null)
            {
                attacker.SendTargetHP(0, tid, -damage);

                if (attackType != MagicAttack && attackType != DurationAttack)
                {
                    attacker.ItemWoreOut(GameUser.DurabilityTypeAttack, damage);

                    // C++ quirk kept as-is: the drain amount scales with the
                    // magic TYPE value, not the magic amount.
                    var tempDamage = (short)(damage * attacker.MagicTypeLeftHand / 100);
                    switch (attacker.MagicTypeLeftHand)
                    {
                        case GameUser.ItemTypeHpDrain: attacker.HpChange(tempDamage, 0); break;
                        case GameUser.ItemTypeMpDrain: attacker.MSpChange(tempDamage); break;
                    }

                    tempDamage = (short)(damage * attacker.MagicTypeRightHand / 100);
                    switch (attacker.MagicTypeRightHand)
                    {
                        case GameUser.ItemTypeHpDrain: attacker.HpChange(tempDamage, 0); break;
                        case GameUser.ItemTypeMpDrain: attacker.MSpChange(tempDamage); break;
                    }
                }
            }

            // npc dead
            if (result is 0x02 or 0x04)
            {
                GameZone? map = world.GetZoneByIndex(npc.ZoneIndex);
                if (map is null)
                    return;

                map.RegionNpcRemove(npc.RegionX, npc.RegionZ, tid);
                npc.RegionX = 0;
                npc.RegionZ = 0;
                npc.NpcState = GameNpc.StateDead;

                if (npc.ObjectType == GameNpc.SpecialObject
                    && map.GetObjectEvent(npc.Sid) is { } objectEvent)
                {
                    objectEvent.Life = 0;
                }

                // Exit NPCs hand out the teleport scroll.
                if (npc.NpcType == 2)
                    attacker?.GiveItem(900001000, 1);
            }
        }
        // npc attack -> user / monster
        else if (type == 0x02)
        {
            GameNpc? npc = world.Npcs.GetValueOrDefault(sid);
            if (npc is null)
                return;

            if (tid >= EbenezerWorld.UserBand && tid < EbenezerWorld.NpcBand)
            {
                GameUser? user = tid >= 0 && tid < world.Users.Length ? world.Users[tid] : null;
                if (user?.UserData is not { } userData)
                    return;

                // Being hit interrupts an ongoing cast.
                if (user.Magic.MagicState == MagicProcessor.StateCasting)
                    user.Magic.IsAvailable(0, -1, -1, MagicProcessor.MagicEffecting, 0, 0, 0);

                user.HpChange(-damage, 1, attack: true);
                user.ItemWoreOut(GameUser.DurabilityTypeDefence, damage);

                var buffer = new byte[16];
                var writer = new PacketWriter(buffer);
                writer.SetByte((byte)GameOpcode.WIZ_ATTACK);
                writer.SetByte(attackType);
                writer.SetByte(result == 0x03 ? (byte)0x00 : result);
                writer.SetShort(sid);
                writer.SetShort(tid);
                world.SendRegion(writer.Written, npc.CurZone, npc.RegionX, npc.RegionZ, except: null, direct: false);

                // user dead
                if (result == 0x02)
                {
                    if (user.ResHpType == UserDead)
                        return;

                    // The victim gets the dead packet immediately once more.
                    user.Send(writer.Written);
                    user.ResHpType = UserDead;

                    logger.LogDebug("AiLink: user is dead [charId={CharId}]", userData.CharId);

                    if (userData.Fame == FameCommandCaptain)
                    {
                        userData.Fame = FameChief;

                        var authBuffer = new byte[8];
                        var authWriter = new PacketWriter(authBuffer);
                        authWriter.SetByte((byte)GameOpcode.WIZ_AUTHORITY_CHANGE);
                        authWriter.SetByte(0x01); // COMMAND_AUTHORITY
                        authWriter.SetShort(user.SocketId);
                        authWriter.SetByte(userData.Fame);
                        world.SendRegion(authWriter.Written, userData.Zone, user.RegionX, user.RegionZ);
                        user.Send(authWriter.Written);

                        // Announcement(*_CAPTAIN_DEPRIVE_NOTIFY) attaches with
                        // the chat slice (DB string resources).
                    }

                    // Patrol guards always take 1%, otherwise 1% abroad / 5% at home.
                    if (npc.NpcType == NpcTypePatrolGuard)
                    {
                        user.ExpChange(-user.MaxExp / 100);
                    }
                    else if (userData.Zone != userData.Nation && userData.Zone < 3)
                    {
                        user.ExpChange(-user.MaxExp / 100);
                    }
                    else
                    {
                        user.ExpChange(-user.MaxExp / 20);
                    }
                }
            }
            else if (tid >= EbenezerWorld.NpcBand)
            {
                GameNpc? monster = world.Npcs.GetValueOrDefault(tid);
                if (monster is null)
                    return;

                monster.HP -= damage;
                if (monster.HP < 0)
                    monster.HP = 0;

                var buffer = new byte[16];
                var writer = new PacketWriter(buffer);
                writer.SetByte((byte)GameOpcode.WIZ_ATTACK);
                writer.SetByte(attackType);
                writer.SetByte(result);
                writer.SetShort(sid);
                writer.SetShort(tid);

                if (result == 0x02)
                {
                    GameZone? map = world.GetZoneByIndex(monster.ZoneIndex);
                    if (map is null)
                        return;

                    map.RegionNpcRemove(monster.RegionX, monster.RegionZ, tid);
                    monster.RegionX = 0;
                    monster.RegionZ = 0;
                    monster.NpcState = GameNpc.StateDead;

                    // C++ quirk kept as-is: the ATTACKER's object type gates
                    // the VICTIM's object event.
                    if (npc.ObjectType == GameNpc.SpecialObject
                        && map.GetObjectEvent(monster.Sid) is { } objectEvent)
                    {
                        objectEvent.Life = 0;
                    }
                }

                world.SendRegion(writer.Written, npc.CurZone, npc.RegionX, npc.RegionZ, except: null, direct: false);
            }
        }
    }

    private const byte UserDead = 3;             // USER_DEAD (e_UserResHpType)
    private const byte FameChief = 0x01;         // CHIEF
    private const byte FameCommandCaptain = 100; // COMMAND_CAPTAIN

    /// <summary>CAISocket::RecvMagicAttackResult — NPC magic echoes.</summary>
    private void RecvMagicAttackResult(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        byte command = reader.GetByte();
        uint magicId = reader.GetDWord();
        short sid = reader.GetShort();
        short tid = reader.GetShort();
        Span<short> data = stackalloc short[6];
        for (int i = 0; i < 6; i++)
            data[i] = reader.GetShort();

        var buffer = new byte[32];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_MAGIC_PROCESS);
        writer.SetByte(command);
        writer.SetDWord(magicId);
        writer.SetShort(sid);
        writer.SetShort(tid);
        for (int i = 0; i < 6; i++)
            writer.SetShort(data[i]);

        if (command == MagicCasting)
        {
            GameNpc? npc = world.Npcs.GetValueOrDefault(sid);
            if (npc is null)
                return;

            world.SendRegion(writer.Written, npc.CurZone, npc.RegionX, npc.RegionZ, except: null, direct: false);
        }
        else if (command == MagicEffecting)
        {
            if (sid >= EbenezerWorld.UserBand && sid < EbenezerWorld.NpcBand)
            {
                GameUser? user = sid >= 0 && sid < world.Users.Length ? world.Users[sid] : null;
                if (user?.UserData is not { } userData || user.ResHpType == UserDead)
                    return;

                world.SendRegion(writer.Written, userData.Zone, user.RegionX, user.RegionZ, except: null, direct: false);
            }
            else if (sid >= EbenezerWorld.NpcBand)
            {
                if (tid >= EbenezerWorld.NpcBand)
                {
                    GameNpc? npc = world.Npcs.GetValueOrDefault(tid);
                    if (npc is null)
                        return;

                    world.SendRegion(writer.Written, npc.CurZone, npc.RegionX, npc.RegionZ, except: null, direct: false);
                    return;
                }

                // NPC magic hitting a user runs through CMagicProcess::MagicPacket.
                var inner = new byte[32];
                var innerWriter = new PacketWriter(inner);
                innerWriter.SetByte(command);
                innerWriter.SetDWord(magicId);
                innerWriter.SetShort(sid);
                innerWriter.SetShort(tid);
                for (int i = 0; i < 6; i++)
                    innerWriter.SetShort(data[i]);

                MagicProcess.MagicPacket(innerWriter.Written);
            }
        }
    }

    /// <summary>CAISocket::RecvUserExp — AG_USER_EXP.</summary>
    private void RecvUserExp(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        short userId = reader.GetShort();
        short exp = reader.GetShort();
        short loyalty = reader.GetShort();

        GameUser? user = userId >= 0 && userId < world.Users.Length ? world.Users[userId] : null;
        if (user?.UserData is not { } userData)
        {
            logger.LogError("AiLink: exp/loyalty grant for invalid user [userId={UserId}]", userId);
            return;
        }

        if (exp < 0 || loyalty < 0)
        {
            logger.LogError("AiLink: invalid exp or loyalty amount [userId={UserId} exp={Exp} loyalty={Loyalty}]",
                userId, exp, loyalty);
            return;
        }

        userData.Loyalty += loyalty;
        user.ExpChange(exp);

        if (loyalty > 0)
        {
            var buffer = new byte[8];
            var writer = new PacketWriter(buffer);
            writer.SetByte((byte)GameOpcode.WIZ_LOYALTY_CHANGE);
            writer.SetDWord((uint)userData.Loyalty);
            user.Send(writer.Written);
        }
    }

    /// <summary>CAISocket::RecvSystemMsg — only the SEND_ALL branch does anything.</summary>
    private void RecvSystemMsg(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        byte type = reader.GetByte();
        short who = reader.GetShort();
        short length = reader.GetShort();
        ReadOnlySpan<byte> message = reader.GetString(length);

        if (who != SendAllTarget)
            return;

        var buffer = new byte[16 + message.Length];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_CHAT);
        writer.SetByte(type);
        writer.SetByte(0x01); // nation
        writer.SetShort(-1);  // sid
        writer.SetByte(0);    // sender name length
        writer.SetString2(message);
        world.SendAll(writer.Written);
    }

    /// <summary>CAISocket::RecvNpcGiveItem — drops a loot bundle and notifies the killer.</summary>
    private void RecvNpcGiveItem(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        short uid = reader.GetShort();
        short nid = reader.GetShort();
        short zone = reader.GetShort();
        short regionX = reader.GetShort();
        short regionZ = reader.GetShort();
        float x = reader.GetFloat();
        float z = reader.GetFloat();
        float y = reader.GetFloat();
        byte count = reader.GetByte();

        Span<int> itemIds = stackalloc int[6];
        Span<short> itemCounts = stackalloc short[6];
        for (int i = 0; i < count && i < 6; i++)
        {
            itemIds[i] = reader.GetInt();
            itemCounts[i] = reader.GetShort();
        }

        GameUser? user = uid >= 0 && uid < world.Users.Length ? world.Users[uid] : null;
        if (user is null)
            return;

        GameZone? map = world.GetZoneById(zone);
        if (map is null)
            return;

        var item = new ZoneItem
        {
            BundleIndex = map.Bundle,
            Time = world.Clock(),
            X = x,
            Z = z,
            Y = y,
        };

        for (int i = 0; i < count && i < 6; i++)
        {
            if (world.ItemTable.ContainsKey(itemIds[i]))
            {
                item.ItemId[i] = itemIds[i];
                item.Count[i] = itemCounts[i];
            }
        }

        if (!map.RegionItemAdd(regionX, regionZ, item))
            return;

        var buffer = new byte[8];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_ITEM_DROP);
        writer.SetShort(nid);
        writer.SetDWord(item.BundleIndex);

        // Send_PartyMember for grouped killers attaches with the party slice.
        user.Send(writer.Written);
    }

    /// <summary>CAISocket::RecvUserFail — HP desync repair: the AI says the user died.</summary>
    private void RecvUserFail(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        short instanceId = reader.GetShort();
        short npcId = reader.GetShort();

        GameUser? user = instanceId >= 0 && instanceId < world.Users.Length ? world.Users[instanceId] : null;
        if (user?.UserData is not { } userData)
            return;

        user.HpChange(-10000, 1);

        var buffer = new byte[16];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_ATTACK);
        writer.SetByte(0x01);
        writer.SetByte(0x02);
        writer.SetShort(npcId);
        writer.SetShort(instanceId);
        world.SendRegion(writer.Written, userData.Zone, user.RegionX, user.RegionZ);

        logger.LogDebug("AiLink: RecvUserFail [npcId={NpcId} serial={Serial} charId={CharId}]",
            npcId, instanceId, userData.CharId);
    }

    /// <summary>CAISocket::RecvNpcEventItem — EventMoneyItemGet is a no-op upstream.</summary>
    private void RecvNpcEventItem(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        short uid = reader.GetShort();
        reader.GetShort(); // nid, unused
        int itemNumber = reader.GetInt();
        int count = reader.GetInt();

        GameUser? user = uid >= 0 && uid < world.Users.Length ? world.Users[uid] : null;
        user?.EventMoneyItemGet(itemNumber, count);
    }

    // e_BattleEvent (shared/packets.h).
    private const byte BattleEventOpen = 1;
    private const byte BattleMapEventResult = 2;
    private const byte BattleEventResult = 3;
    private const byte BattleEventMaxUser = 4;

    /// <summary>CAISocket::RecvBattleEvent — invasion gates, war result and kill fame.</summary>
    private void RecvBattleEvent(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        byte type = reader.GetByte();
        byte result = reader.GetByte();

        byte[] udp = [];

        if (type == BattleEventOpen)
        {
            // no-op upstream
        }
        else if (type == BattleMapEventResult)
        {
            if (world.BattleOpen == 0)
            {
                logger.LogError("RecvBattleEvent: No active battle [type={Type}]", type);
                return;
            }

            if (result == 1)
                world.KarusOpenFlag = 1;   // Karus land can be invaded
            else if (result == 2)
                world.ElmoradOpenFlag = 1; // El Morad land can be invaded

            udp = [0xD1, type, result]; // UDP_BATTLE_EVENT_PACKET
        }
        else if (type == BattleEventResult)
        {
            if (world.BattleOpen == 0)
            {
                logger.LogError("RecvBattleEvent: No active battle [type={Type}]", type);
                return;
            }

            int nameLen = reader.GetByte();
            if (nameLen > 0 && nameLen <= MaxIdSizeChars)
            {
                string maxUser = Encoding.Latin1.GetString(reader.GetString(nameLen));
                if (world.BattleSave == 0)
                {
                    // WIZ_BATTLE_EVENT save (UPDATE_BATTLE_EVENT proc via the host hook).
                    world.SaveBattleResult?.Invoke(maxUser, result);
                    world.BattleSave = 1;
                }
            }

            world.Victory = result;
            world.OldVictory = result;
            world.KarusOpenFlag = 0;
            world.ElmoradOpenFlag = 0;
            world.BanishFlag = 1;

            udp = [0xD1, type, result];
        }
        else if (type == BattleEventMaxUser)
        {
            int nameLen = reader.GetByte();
            if (nameLen > 0 && nameLen <= MaxIdSizeChars)
            {
                string maxUser = Encoding.Latin1.GetString(reader.GetString(nameLen));

                string knightsName = string.Empty;
                GameUser? killer = world.GetUserByCharId(maxUser);
                if (killer?.UserData is { } killerData
                    && world.Knights.GetValueOrDefault(killerData.Knights) is { } clan)
                    knightsName = clan.Name;

                // result: 1 captain, 2 gatekeeper, 3..6 fortress sentries, 7/8 gatekeeper again.
                string chat = result switch
                {
                    1 => world.FormatResource(135, knightsName, maxUser),      // IDS_KILL_CAPTAIN
                    2 or 7 or 8 => world.FormatResource(134, knightsName, maxUser), // IDS_KILL_GATEKEEPER
                    3 => world.FormatResource(136, knightsName, maxUser),      // IDS_KILL_KARUS_GUARD1
                    4 => world.FormatResource(137, knightsName, maxUser),      // IDS_KILL_KARUS_GUARD2
                    5 => world.FormatResource(138, knightsName, maxUser),      // IDS_KILL_ELMO_GUARD1
                    6 => world.FormatResource(139, knightsName, maxUser),      // IDS_KILL_ELMO_GUARD2
                    _ => string.Empty,
                };

                chat = world.FormatResource(126, chat); // IDP_ANNOUNCEMENT

                SendBattleChat(8, chat); // WAR_SYSTEM_CHAT
                SendBattleChat(1, chat); // PUBLIC_CHAT

                byte[] nameBytes = Encoding.Latin1.GetBytes(maxUser);
                udp = new byte[4 + nameBytes.Length];
                udp[0] = 0xD1;
                udp[1] = type;
                udp[2] = result;
                udp[3] = (byte)nameBytes.Length;
                nameBytes.CopyTo(udp, 4);
            }
        }

        world.SendUdpAll?.Invoke(udp);
    }

    private const int MaxIdSizeChars = 20; // MAX_ID_SIZE

    private void SendBattleChat(byte chatType, string text)
    {
        byte[] chat = Encoding.Latin1.GetBytes(text);
        var buffer = new byte[10 + chat.Length];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_CHAT);
        writer.SetByte(chatType);
        writer.SetByte(1);
        writer.SetShort(-1);
        writer.SetByte(0); // sender name length
        writer.SetString2(chat);
        world.SendAll(writer.Written);
    }
}

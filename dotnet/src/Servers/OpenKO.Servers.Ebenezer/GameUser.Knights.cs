using System.Text;
using OpenKO.Core.Protocol;
using OpenKO.Network;

namespace OpenKO.Servers.Ebenezer;

/// <summary>
/// Port of <c>CKnightsManager</c> (Server/Ebenezer/KnightsManager.cpp) as a
/// GameUser slice: the WIZ_KNIGHTS_PROCESS dispatch. The C++ round-trips every
/// state change through the Aujard queue; the port awaits the Aujard library
/// and runs the Recv* handler inline with the DB result.
/// </summary>
public sealed partial class GameUser
{
    // e_KnightsOpcode (shared/packets.h).
    public const byte KnightsCreate = 0x01;
    public const byte KnightsJoin = 0x02;
    public const byte KnightsWithdraw = 0x03;
    public const byte KnightsRemove = 0x04;
    public const byte KnightsDestroy = 0x05;
    public const byte KnightsAdmit = 0x06;
    public const byte KnightsReject = 0x07;
    public const byte KnightsPunish = 0x08;
    public const byte KnightsChief = 0x09;
    public const byte KnightsViceChief = 0x0A;
    public const byte KnightsOfficer = 0x0B;
    public const byte KnightsAllListReq = 0x0C;
    public const byte KnightsMemberReq = 0x0D;
    public const byte KnightsCurrentReq = 0x0E;
    public const byte KnightsStash = 0x0F;
    public const byte KnightsModifyFame = 0x10;
    public const byte KnightsJoinReq = 0x11;

    // Fame ranks (GameDefine.h; CHIEF was renumbered upstream and now aliases PUNISH).
    private const byte FameViceChief = 0x02;
    private const byte FameKnight = 0x03;
    private const byte FameOfficer = 0x04;
    private const byte FameTrainee = 0x05;
    private const byte FamePunish = 0x01;

    /// <summary>CKnightsManager::PacketProcess.</summary>
    public async ValueTask KnightsProcessAsync(ReadOnlyMemory<byte> body)
    {
        if (body.Length < 1)
            return;

        byte command = body.Span[0];
        ReadOnlyMemory<byte> rest = body[1..];

        switch (command)
        {
            case KnightsCreate:
                await KnightsCreateAsync(rest);
                break;

            case KnightsJoin:
                KnightsJoinInvite(rest.Span);
                break;

            case KnightsWithdraw:
                await KnightsWithdrawAsync();
                break;

            case KnightsRemove:
            case KnightsAdmit:
            case KnightsReject:
            case KnightsChief:
            case KnightsViceChief:
            case KnightsOfficer:
            case KnightsPunish:
                await KnightsModifyMemberAsync(rest, command);
                break;

            case KnightsDestroy:
                await KnightsDestroyAsync();
                break;

            case KnightsAllListReq:
                KnightsAllList(rest.Span);
                break;

            case KnightsMemberReq:
                KnightsAllMembers();
                break;

            case KnightsCurrentReq:
                KnightsCurrentMembers(rest.Span);
                break;

            case KnightsStash:
                break; // no-op upstream

            case KnightsJoinReq:
                await KnightsJoinReqAsync(rest);
                break;
        }
    }

    private void SendKnightsFail(byte command, byte retValue)
    {
        var buffer = new byte[4];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_KNIGHTS_PROCESS);
        writer.SetByte(command);
        writer.SetByte(retValue);
        Send(writer.Written);
    }

    /// <summary>The DB-error reply (CKnightsManager::ReceiveKnightsProcess, result &gt; 0).</summary>
    private void SendKnightsDbFail(byte command, byte result)
    {
        byte[] message = Encoding.Latin1.GetBytes(world.FormatResource(122)); // IDP_KNIGHT_DB_FAIL
        var buffer = new byte[8 + message.Length];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_KNIGHTS_PROCESS);
        writer.SetByte(command);
        writer.SetByte(result);
        writer.SetString2(message);
        Send(writer.Written);
    }

    /// <summary>CKnightsManager::CreateKnights + RecvCreateKnights.</summary>
    public async ValueTask KnightsCreateAsync(ReadOnlyMemory<byte> body)
    {
        if (UserData is not { } user)
            return;

        var reader = new PacketReader(body.Span);
        int idLen = reader.GetShort();
        if (idLen > MaxIdSize || idLen < 0)
        {
            SendKnightsFail(KnightsCreate, 3);
            return;
        }

        string name = Encoding.Latin1.GetString(reader.GetString(idLen));

        if (!IsAvailableKnightsName(name))
        {
            SendKnightsFail(KnightsCreate, 3);
            return;
        }

        if (user.Knights != 0)
        {
            SendKnightsFail(KnightsCreate, 5);
            return;
        }

        if (world.ServerGroup == 2)
        {
            SendKnightsFail(KnightsCreate, 8);
            return;
        }

        if (user.Level < 20)
        {
            SendKnightsFail(KnightsCreate, 2);
            return;
        }

        if (user.Gold < 500_000)
        {
            SendKnightsFail(KnightsCreate, 4);
            return;
        }

        int knightsIndex = GetKnightsIndex(user.Nation);
        if (knightsIndex == -1)
        {
            SendKnightsFail(KnightsCreate, 6);
            return;
        }

        short result = await dbAgent.CreateKnightsAsync(
            knightsIndex, user.Nation, name, user.CharId, KnightsClan.ClanType);
        if (result > 0)
        {
            SendKnightsDbFail(KnightsCreate, (byte)result);
            return;
        }

        // CKnightsManager::RecvCreateKnights.
        var clan = new KnightsClan
        {
            Index = (short)knightsIndex,
            Flag = KnightsClan.ClanType,
            Nation = user.Nation,
            Name = name,
            Chief = user.CharId,
            Members = 1,
            Grade = 5,
        };
        world.Knights[knightsIndex] = clan;

        user.Knights = (short)knightsIndex;
        user.Fame = FameChief;
        int money = user.Gold - 500_000;
        user.Gold = money;

        world.AddKnightsUser(knightsIndex, user.CharId);

        byte[] nameBytes = Encoding.Latin1.GetBytes(name);
        var buffer = new byte[24 + nameBytes.Length];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_KNIGHTS_PROCESS);
        writer.SetByte(KnightsCreate);
        writer.SetByte(0x01);
        writer.SetShort(SocketId);
        writer.SetShort(knightsIndex);
        writer.SetShort(nameBytes.Length);
        writer.SetString(nameBytes);
        writer.SetByte(5); // knights grade
        writer.SetByte(0);
        writer.SetDWord((uint)money);
        world.SendRegion(writer.Written, user.Zone, RegionX, RegionZ, except: null, direct: false);

        // The UDP_KNIGHTS_PROCESS cross-server broadcast is not ported (no UDP channel).
    }

    /// <summary>CKnightsManager::IsAvailableName.</summary>
    private bool IsAvailableKnightsName(string name)
    {
        foreach (KnightsClan clan in world.Knights.Values)
        {
            if (string.Equals(clan.Name, name, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    /// <summary>CKnightsManager::GetKnightsIndex — Karus &lt; 15000 ≤ El Morad ≤ 30000.</summary>
    private int GetKnightsIndex(int nation)
    {
        int knightsIndex = 0;
        if (nation == 2) // ELMORAD
            knightsIndex = 15000;

        foreach (KnightsClan clan in world.Knights.Values)
        {
            if (knightsIndex < clan.Index)
            {
                if (nation == 1 && clan.Index >= 15000)
                    continue;

                knightsIndex = clan.Index;
            }
        }

        knightsIndex++;
        if (nation == 1)
        {
            if (knightsIndex is >= 15000 or < 0)
                return -1;
        }
        else if (nation == 2)
        {
            if (knightsIndex is < 15000 or > 30000)
                return -1;
        }

        if (world.Knights.ContainsKey(knightsIndex))
            return -1;

        return knightsIndex;
    }

    /// <summary>CKnightsManager::JoinKnights — the chief invites a target user.</summary>
    public void KnightsJoinInvite(ReadOnlySpan<byte> body)
    {
        if (UserData is not { } user)
            return;

        if (user.Zone > 2)
        {
            SendKnightsFail(KnightsJoin, 12);
            return;
        }

        if (user.Fame != FameChief && user.Fame != FameViceChief)
        {
            SendKnightsFail(KnightsJoin, 6);
            return;
        }

        int knightsIndex = user.Knights;
        KnightsClan? clan = world.Knights.GetValueOrDefault(knightsIndex);
        if (clan is null)
        {
            SendKnightsFail(KnightsJoin, 7);
            return;
        }

        var reader = new PacketReader(body);
        short memberId = reader.GetShort();

        GameUser? target = memberId >= 0 && memberId < world.Users.Length ? world.Users[memberId] : null;
        if (target?.UserData is not { } targetData)
        {
            SendKnightsFail(KnightsJoin, 2);
            return;
        }

        if (target.ResHpType == UserDeadResHpType)
        {
            SendKnightsFail(KnightsJoin, 3);
            return;
        }

        if (targetData.Nation != user.Nation)
        {
            SendKnightsFail(KnightsJoin, 4);
            return;
        }

        if (targetData.Knights > 0)
        {
            SendKnightsFail(KnightsJoin, 5);
            return;
        }

        byte[] clanName = Encoding.Latin1.GetBytes(clan.Name);
        var buffer = new byte[10 + clanName.Length];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_KNIGHTS_PROCESS);
        writer.SetByte(KnightsJoinReq);
        writer.SetByte(0x01);
        writer.SetShort(SocketId);
        writer.SetShort(knightsIndex);
        writer.SetString2(clanName);
        target.Send(writer.Written);
    }

    /// <summary>CKnightsManager::JoinKnightsReq — the invited user answers.</summary>
    public async ValueTask KnightsJoinReqAsync(ReadOnlyMemory<byte> body)
    {
        var reader = new PacketReader(body.Span);
        byte flag = reader.GetByte();
        short sid = reader.GetShort();

        GameUser? inviter = sid >= 0 && sid < world.Users.Length ? world.Users[sid] : null;
        if (inviter is null)
        {
            SendKnightsFail(KnightsJoin, 2);
            return;
        }

        if (flag == 0)
        {
            // Declined — the INVITER gets the notification.
            inviter.SendKnightsFail(KnightsJoin, 11);
            return;
        }

        int knightsIndex = reader.GetShort();
        if (!world.Knights.ContainsKey(knightsIndex))
        {
            SendKnightsFail(KnightsJoin, 7);
            return;
        }

        if (UserData is not { } user)
            return;

        // Aujard KNIGHTS_JOIN (0x12) — UPDATE_KNIGHTS for the accepting user.
        short result = await dbAgent.UpdateKnightsAsync(KnightsJoin + 0x10, user.CharId, knightsIndex, 0);
        if (result > 0)
        {
            SendKnightsDbFail(KnightsJoin, (byte)result);
            return;
        }

        RecvKnightsJoinWithdraw(knightsIndex, join: true);
    }

    /// <summary>CKnightsManager::WithdrawKnights (chief withdrawal destroys the clan).</summary>
    public async ValueTask KnightsWithdrawAsync()
    {
        if (UserData is not { } user)
            return;

        if (user.Knights is < 1 or > 30000)
        {
            SendKnightsFail(KnightsWithdraw, 10);
            return;
        }

        if (user.Zone > 2)
        {
            SendKnightsFail(KnightsWithdraw, 12);
            return;
        }

        if (user.Fame == FameChief)
        {
            short destroyResult = await dbAgent.DeleteKnightsAsync(user.Knights);
            if (destroyResult > 0)
            {
                SendKnightsDbFail(KnightsDestroy, (byte)destroyResult);
                return;
            }

            RecvKnightsDestroy(user.Knights);
            return;
        }

        short result = await dbAgent.UpdateKnightsAsync(KnightsWithdraw + 0x10, user.CharId, user.Knights, 0);
        if (result > 0)
        {
            SendKnightsDbFail(KnightsWithdraw, (byte)result);
            return;
        }

        RecvKnightsJoinWithdraw(user.Knights, join: false);
    }

    /// <summary>CKnightsManager::DestroyKnights.</summary>
    public async ValueTask KnightsDestroyAsync()
    {
        if (UserData is not { } user)
            return;

        if (user.Fame != FameChief)
        {
            SendKnightsFail(KnightsDestroy, 0);
            return;
        }

        if (user.Zone > 2)
        {
            SendKnightsFail(KnightsDestroy, 12);
            return;
        }

        // C++ quirk kept as-is: the success path falls through into
        // fail_return, so the destroy request also emits a [DESTROY][0] reply.
        SendKnightsFail(KnightsDestroy, 0);

        short result = await dbAgent.DeleteKnightsAsync(user.Knights);
        if (result > 0)
        {
            SendKnightsDbFail(KnightsDestroy, (byte)result);
            return;
        }

        RecvKnightsDestroy(user.Knights);
    }

    /// <summary>CKnightsManager::ModifyKnightsMember (remove/admit/reject/ranks/punish).</summary>
    public async ValueTask KnightsModifyMemberAsync(ReadOnlyMemory<byte> body, byte command)
    {
        if (UserData is not { } user)
            return;

        var reader = new PacketReader(body.Span);
        int idLen = reader.GetShort();
        if (idLen > MaxIdSize || idLen <= 0)
        {
            SendKnightsFail(command, 2);
            return;
        }

        string targetId = Encoding.Latin1.GetString(reader.GetString(idLen));

        if (user.Zone > 2)
        {
            SendKnightsFail(command, 12);
            return;
        }

        if (string.Equals(targetId, user.CharId, StringComparison.OrdinalIgnoreCase))
        {
            SendKnightsFail(command, 9);
            return;
        }

        // NOTE: upstream renumbered CHIEF to 0x01, so these rank gates now
        // compare against the NEW values — a chief (1) fails "fame >= OFFICER
        // (4)". Kept verbatim.
        if (command is KnightsAdmit or KnightsReject)
        {
            if (user.Fame < FameOfficer)
            {
                SendKnightsFail(command, 0);
                return;
            }
        }
        else if (command == KnightsPunish)
        {
            if (user.Fame < FameViceChief)
            {
                SendKnightsFail(command, 0);
                return;
            }
        }
        else if (user.Fame != FameChief)
        {
            SendKnightsFail(command, 6);
            return;
        }

        GameUser? target = world.GetUserByCharId(targetId);
        byte removeFlag;
        if (target?.UserData is not { } targetData)
        {
            // Offline targets can only be removed.
            if (command == KnightsRemove)
            {
                removeFlag = 0;
                short removeResult = await dbAgent.UpdateKnightsAsync(
                    command + 0x10, targetId, user.Knights, removeFlag);
                if (removeResult > 0)
                {
                    SendKnightsDbFail(command, (byte)removeResult);
                    return;
                }

                RecvKnightsModifyFame(user.Knights, targetId, command);
                return;
            }

            SendKnightsFail(command, 2);
            return;
        }

        if (user.Nation != targetData.Nation)
        {
            SendKnightsFail(command, 4);
            return;
        }

        if (user.Knights != targetData.Knights)
        {
            SendKnightsFail(command, 5);
            return;
        }

        if (command == KnightsViceChief)
        {
            if (targetData.Fame == FameViceChief)
            {
                SendKnightsFail(command, 8);
                return;
            }

            if (!world.Knights.ContainsKey(user.Knights))
            {
                SendKnightsFail(command, 7);
                return;
            }
        }

        removeFlag = 1;
        short result = await dbAgent.UpdateKnightsAsync(command + 0x10, targetId, user.Knights, removeFlag);
        if (result > 0)
        {
            SendKnightsDbFail(command, (byte)result);
            return;
        }

        RecvKnightsModifyFame(user.Knights, targetId, command);
    }

    /// <summary>CKnightsManager::AllKnightsList — a 10-per-page nation clan list.</summary>
    public void KnightsAllList(ReadOnlySpan<byte> body)
    {
        if (UserData is not { } user)
            return;

        var reader = new PacketReader(body);
        int page = reader.GetShort();
        int start = page * 10;

        var entries = new byte[4096];
        var entryWriter = new PacketWriter(entries);
        int count = 0;

        foreach (KnightsClan clan in world.Knights.Values.OrderBy(c => c.Index))
        {
            // Only the knights list (not clans) is browsable here.
            if (clan.Flag != KnightsClan.KnightsType)
                continue;

            if (clan.Nation != user.Nation)
                continue;

            if (count < start)
            {
                count++;
                continue;
            }

            entryWriter.SetShort(clan.Index);
            entryWriter.SetString2(Encoding.Latin1.GetBytes(clan.Name));
            entryWriter.SetShort(clan.Members);
            entryWriter.SetString2(Encoding.Latin1.GetBytes(clan.Chief));
            entryWriter.SetDWord((uint)clan.Points);

            count++;
            if (count >= start + 10)
                break;
        }

        var buffer = new byte[10 + entryWriter.Index];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_KNIGHTS_PROCESS);
        writer.SetByte(KnightsAllListReq);
        writer.SetByte(0x01);
        writer.SetShort(page);
        writer.SetShort(count - start);
        writer.SetString(entries.AsSpan(0, entryWriter.Index));
        Send(writer.Written);
    }

    /// <summary>CKnightsManager::AllKnightsMember.</summary>
    public void KnightsAllMembers()
    {
        if (UserData is not { } user)
            return;

        if (user.Knights <= 0)
        {
            SendKnightsFail(KnightsMemberReq, 2);
            return;
        }

        KnightsClan? clan = world.Knights.GetValueOrDefault(user.Knights);
        if (clan is null)
        {
            SendKnightsFail(KnightsMemberReq, 7);
            return;
        }

        var entries = new byte[4096];
        var entryWriter = new PacketWriter(entries);

        // C++ quirk kept as-is: the online sweep always writes into the buffer
        // first; for a chief the full member sweep APPENDS to it (duplicates).
        int onlineCount = GetKnightsAllMembers(user.Knights, ref entryWriter, type: 0);
        int count = user.Fame == FameChief
            ? GetKnightsAllMembers(user.Knights, ref entryWriter, type: 1)
            : onlineCount;

        int pktSize = entryWriter.Index + 4;
        if (count > KnightsClan.MaxClan)
            return;

        var buffer = new byte[16 + entryWriter.Index];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_KNIGHTS_PROCESS);
        writer.SetByte(KnightsMemberReq);
        writer.SetByte(0x01);
        writer.SetShort(pktSize);
        writer.SetShort(onlineCount);
        writer.SetShort(clan.Members);
        writer.SetShort(count);
        writer.SetString(entries.AsSpan(0, entryWriter.Index));
        Send(writer.Written);
    }

    /// <summary>EbenezerApp::GetKnightsAllMembers.</summary>
    private int GetKnightsAllMembers(int knightsIndex, ref PacketWriter writer, int type)
    {
        if (knightsIndex <= 0)
            return 0;

        int count = 0;
        if (type == 0)
        {
            foreach (GameUser? member in world.Users)
            {
                if (member?.UserData is not { } data || data.Knights != knightsIndex)
                    continue;

                writer.SetString2(Encoding.Latin1.GetBytes(data.CharId));
                writer.SetByte(data.Fame);
                writer.SetByte(data.Level);
                writer.SetShort(data.Class);
                writer.SetByte(1);
                count++;
            }
        }
        else
        {
            KnightsClan? clan = world.Knights.GetValueOrDefault(knightsIndex);
            if (clan is null)
                return 0;

            for (int i = 0; i < KnightsClan.MaxClan; i++)
            {
                if (clan.Users[i].Used != 1)
                    continue;

                GameUser? member = world.GetUserByCharId(clan.Users[i].UserName);
                if (member?.UserData is { } data)
                {
                    if (data.Knights == knightsIndex)
                    {
                        writer.SetString2(Encoding.Latin1.GetBytes(data.CharId));
                        writer.SetByte(data.Fame);
                        writer.SetByte(data.Level);
                        writer.SetShort(data.Class);
                        writer.SetByte(1);
                        count++;
                    }
                    else
                    {
                        // Left/kicked in another zone — drop from the cache.
                        world.RemoveKnightsUser(knightsIndex, data.CharId);
                    }
                }
                else
                {
                    writer.SetString2(Encoding.Latin1.GetBytes(clan.Users[i].UserName));
                    writer.SetByte(0);
                    writer.SetByte(0);
                    writer.SetShort(0);
                    writer.SetByte(0);
                    count++;
                }
            }
        }

        return count;
    }

    /// <summary>CKnightsManager::CurrentKnightsMember.</summary>
    public void KnightsCurrentMembers(ReadOnlySpan<byte> body)
    {
        if (UserData is not { } user)
            return;

        KnightsClan? clan = user.Knights > 0 ? world.Knights.GetValueOrDefault(user.Knights) : null;
        if (clan is null)
        {
            byte[] message = Encoding.Latin1.GetBytes(world.FormatResource(121)); // IDP_KNIGHT_NOT_REGISTERED
            var failBuffer = new byte[8 + message.Length];
            var failWriter = new PacketWriter(failBuffer);
            failWriter.SetByte((byte)GameOpcode.WIZ_KNIGHTS_PROCESS);
            failWriter.SetByte(KnightsCurrentReq);
            failWriter.SetByte(0x00);
            failWriter.SetString2(message);
            Send(failWriter.Written);
            return;
        }

        var reader = new PacketReader(body);
        int page = reader.GetShort();
        int start = page * 10;

        var entries = new byte[4096];
        var entryWriter = new PacketWriter(entries);
        int count = 0;

        foreach (GameUser? member in world.Users)
        {
            if (member?.UserData is not { } data || data.Knights != user.Knights)
                continue;

            if (count < start)
            {
                count++;
                continue;
            }

            // C++ quirk kept as-is: the loop writes the REQUESTER's own
            // id/fame/level/class for every row, not the member's.
            entryWriter.SetString2(Encoding.Latin1.GetBytes(user.CharId));
            entryWriter.SetByte(user.Fame);
            entryWriter.SetByte(user.Level);
            entryWriter.SetShort(user.Class);

            count++;
            if (count >= start + 10)
                break;
        }

        byte[] chief = Encoding.Latin1.GetBytes(clan.Chief);
        var buffer = new byte[12 + chief.Length + entryWriter.Index];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_KNIGHTS_PROCESS);
        writer.SetByte(KnightsCurrentReq);
        writer.SetByte(0x01);
        writer.SetString2(chief);
        writer.SetShort(page);
        writer.SetShort(count - start);
        writer.SetString(entries.AsSpan(0, entryWriter.Index));
        Send(writer.Written);
    }

    // ---- the Recv* handlers (Aujard replies in the C++) ----

    /// <summary>CKnightsManager::RecvJoinKnights (join = KNIGHTS_JOIN, else withdraw).</summary>
    private void RecvKnightsJoinWithdraw(int knightsIndex, bool join)
    {
        if (UserData is not { } user)
            return;

        KnightsClan? clan = world.Knights.GetValueOrDefault(knightsIndex);
        string message;

        if (join)
        {
            user.Knights = (short)knightsIndex;
            user.Fame = FameTrainee;
            message = world.FormatResource(146, user.CharId); // IDS_KNIGHTS_JOIN
            world.AddKnightsUser(knightsIndex, user.CharId);
        }
        else
        {
            user.Knights = 0;
            user.Fame = 0;
            world.RemoveKnightsUser(knightsIndex, user.CharId);
            message = world.FormatResource(147, user.CharId); // IDS_KNIGHTS_WITHDRAW
        }

        var buffer = new byte[64];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_KNIGHTS_PROCESS);
        writer.SetByte(join ? KnightsJoin : KnightsWithdraw);
        writer.SetByte(0x01);
        writer.SetShort(SocketId);
        writer.SetShort(user.Knights);
        writer.SetByte(user.Fame);

        if (clan is not null)
        {
            writer.SetString2(Encoding.Latin1.GetBytes(clan.Name));
            writer.SetByte(clan.Grade);
            writer.SetByte(clan.Ranking);
        }

        world.SendRegion(writer.Written, user.Zone, RegionX, RegionZ, except: null, direct: false);

        SendKnightsChat(knightsIndex, message);
    }

    /// <summary>CKnightsManager::RecvModifyFame.</summary>
    private void RecvKnightsModifyFame(int knightsIndex, string targetId, byte command)
    {
        GameUser? target = world.GetUserByCharId(targetId);
        string message = string.Empty;

        switch (command)
        {
            case KnightsRemove:
                if (target?.UserData is { } removed)
                {
                    removed.Knights = 0;
                    removed.Fame = 0;
                    message = world.FormatResource(148, removed.CharId); // IDS_KNIGHTS_REMOVE
                    world.RemoveKnightsUser(knightsIndex, removed.CharId);
                }
                else
                {
                    world.RemoveKnightsUser(knightsIndex, targetId);
                }
                break;

            case KnightsAdmit:
                if (target?.UserData is { } admitted)
                    admitted.Fame = FameKnight;
                break;

            case KnightsReject:
                if (target?.UserData is { } rejected)
                {
                    rejected.Knights = 0;
                    rejected.Fame = 0;
                    world.RemoveKnightsUser(knightsIndex, rejected.CharId);
                }
                break;

            case KnightsChief:
                if (target?.UserData is { } chief)
                {
                    chief.Fame = FameChief;
                    message = world.FormatResource(149, chief.CharId); // IDS_KNIGHTS_CHIEF
                }
                break;

            case KnightsViceChief:
                if (target?.UserData is { } vice)
                {
                    vice.Fame = FameViceChief;
                    message = world.FormatResource(150, vice.CharId); // IDS_KNIGHTS_VICECHIEF
                }
                break;

            case KnightsOfficer:
                if (target?.UserData is { } officer)
                    officer.Fame = FameOfficer;
                break;

            case KnightsPunish:
                if (target?.UserData is { } punished)
                    punished.Fame = FamePunish;
                break;
        }

        if (target?.UserData is { } targetData)
        {
            var buffer = new byte[10];
            var writer = new PacketWriter(buffer);
            writer.SetByte((byte)GameOpcode.WIZ_KNIGHTS_PROCESS);
            writer.SetByte(KnightsModifyFame);
            writer.SetByte(0x01);
            writer.SetShort(target.SocketId);
            writer.SetShort(targetData.Knights);
            writer.SetByte(targetData.Fame);

            if (command == KnightsRemove)
                world.SendRegion(writer.Written, targetData.Zone, target.RegionX, target.RegionZ, except: null, direct: false);
            else
                target.Send(writer.Written);

            if (command == KnightsRemove)
                target.SendKnightsChatTo(message);
        }

        SendKnightsChat(knightsIndex, message);
    }

    /// <summary>CKnightsManager::RecvDestroyKnights.</summary>
    private void RecvKnightsDestroy(int knightsIndex)
    {
        KnightsClan? clan = world.Knights.GetValueOrDefault(knightsIndex);
        if (clan is null)
            return;

        string message = world.FormatResource(152, clan.Name); // IDS_CLAN_DESTORY
        SendKnightsChat(knightsIndex, message);

        foreach (GameUser? member in world.Users)
        {
            if (member?.UserData is not { } data || data.Knights != knightsIndex)
                continue;

            data.Knights = 0;
            data.Fame = 0;
            world.RemoveKnightsUser(knightsIndex, data.CharId);

            var fameBuffer = new byte[10];
            var fameWriter = new PacketWriter(fameBuffer);
            fameWriter.SetByte((byte)GameOpcode.WIZ_KNIGHTS_PROCESS);
            fameWriter.SetByte(KnightsModifyFame);
            fameWriter.SetByte(0x01);
            fameWriter.SetShort(member.SocketId);
            fameWriter.SetShort(data.Knights);
            fameWriter.SetByte(data.Fame);
            world.SendRegion(fameWriter.Written, data.Zone, member.RegionX, member.RegionZ, except: null, direct: false);
        }

        world.Knights.Remove(knightsIndex);

        var buffer = new byte[4];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_KNIGHTS_PROCESS);
        writer.SetByte(KnightsDestroy);
        writer.SetByte(0x01);
        Send(writer.Written);
    }

    /// <summary>The KNIGHTS_CHAT broadcast to all clan members.</summary>
    private void SendKnightsChat(int knightsIndex, string message)
    {
        byte[] text = Encoding.Latin1.GetBytes(message);
        var buffer = new byte[10 + text.Length];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_CHAT);
        writer.SetByte(KnightsChat);
        writer.SetByte(1);
        writer.SetShort(-1);
        writer.SetByte(0); // sender name length
        writer.SetString2(text);
        world.SendKnightsMember(knightsIndex, writer.Written);
    }

    /// <summary>The KNIGHTS_CHAT line to one (just removed) member.</summary>
    private void SendKnightsChatTo(string message)
    {
        byte[] text = Encoding.Latin1.GetBytes(message);
        var buffer = new byte[10 + text.Length];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_CHAT);
        writer.SetByte(KnightsChat);
        writer.SetByte(1);
        writer.SetShort(-1);
        writer.SetByte(0);
        writer.SetString2(text);
        Send(writer.Written);
    }
}

using System.Text;
using OpenKO.Core.Protocol;
using OpenKO.Data.Models;
using OpenKO.Network;

namespace OpenKO.Servers.Ebenezer;

/// <summary>
/// The CUser party slice (User.cpp): the WIZ_PARTY flow (create/permit/insert/
/// remove/delete), the AI-server notifications and the loot-routing helper.
/// </summary>
public sealed partial class GameUser
{
    // e_PartyOpcode (shared/packets.h).
    public const byte PartyCreate = 0x01;
    public const byte PartyPermit = 0x02;
    public const byte PartyInsert = 0x03;
    public const byte PartyRemove = 0x04;
    public const byte PartyDelete = 0x05;
    public const byte PartyHpChange = 0x06;
    public const byte PartyLevelChange = 0x07;
    public const byte PartyClassChange = 0x08;
    public const byte PartyStatusChange = 0x09;

    /// <summary>CUser::PartyProcess — the WIZ_PARTY dispatch.</summary>
    public void PartyProcess(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        byte subcommand = reader.GetByte();

        switch (subcommand)
        {
            case PartyCreate:
            case PartyInsert:
            {
                int idLength = reader.GetShort();
                if (idLength <= 0 || idLength > 20) // MAX_ID_SIZE
                    return;

                string name = Encoding.Latin1.GetString(reader.GetString(idLength));
                GameUser? member = world.GetUserByCharId(name);
                if (member is not null)
                    PartyRequest(member.SocketId, create: subcommand == PartyCreate);

                break;
            }

            case PartyPermit:
                if (reader.GetByte() != 0)
                    PartyInsertSelf();
                else
                    PartyCancel();

                break;

            case PartyRemove:
                PartyRemoveMember(reader.GetShort());
                break;

            case PartyDelete:
                PartyDisband();
                break;
        }
    }

    /// <summary>CUser::PartyCancel — the invitee declined; tell the leader.</summary>
    public void PartyCancel()
    {
        if (PartyIndex == -1)
            return;

        PartyGroup? party = world.Parties.GetValueOrDefault(PartyIndex);
        if (party is null)
        {
            PartyIndex = -1;
            return;
        }

        PartyIndex = -1;

        int leaderId = party.Uid[0];
        GameUser? leader = leaderId >= 0 && leaderId < world.Users.Length ? world.Users[leaderId] : null;
        if (leader is null)
            return;

        int count = 0;
        for (int i = 0; i < 8; i++)
        {
            if (party.Uid[i] >= 0)
                count++;
        }

        // A lone leader means the party breaks up again.
        if (count == 1)
            leader.PartyDisband();

        var buffer = new byte[8];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_PARTY);
        writer.SetByte(PartyInsert);
        writer.SetShort(-1);
        leader.Send(writer.Written);
    }

    /// <summary>CUser::PartyRequest — the leader invites (create or extend).</summary>
    public void PartyRequest(int memberId, bool create)
    {
        if (UserData is not { } user)
            return;

        short result = -1;

        GameUser? member = memberId >= 0 && memberId < world.Users.Length ? world.Users[memberId] : null;
        if (member?.UserData is not { } memberData || member.PartyIndex != -1)
        {
            SendPartyFail(result);
            return;
        }

        if (user.Nation != memberData.Nation)
        {
            SendPartyFail(-3);
            return;
        }

        // C++ quirk kept as-is: the first clause only accepts an exact
        // level == 1.5 * leader level match; the ±8 band does the real work.
        if (!((memberData.Level <= (int)(user.Level * 1.5) && memberData.Level >= (int)(user.Level * 1.5))
            || (memberData.Level <= user.Level + 8 && memberData.Level >= user.Level - 8)))
        {
            SendPartyFail(-2);
            return;
        }

        PartyGroup? party;
        if (!create)
        {
            party = world.Parties.GetValueOrDefault(PartyIndex);
            if (party is null)
            {
                SendPartyFail(result);
                return;
            }

            int i;
            for (i = 0; i < 8; i++)
            {
                if (party.Uid[i] < 0)
                    break;
            }

            if (i == 8)
            {
                SendPartyFail(result);
                return;
            }
        }
        else
        {
            if (PartyIndex != -1)
            {
                SendPartyFail(result);
                return;
            }

            party = new PartyGroup();
            party.Uid[0] = SocketId;
            party.MaxHp[0] = MaxHp;
            party.Hp[0] = user.Hp;
            party.Level[0] = user.Level;
            party.Class[0] = user.Class;

            PartyIndex = world.NextPartyIndex++;
            if (world.NextPartyIndex == 32767)
                world.NextPartyIndex = 0;

            party.Index = (ushort)PartyIndex;
            if (!world.Parties.TryAdd(party.Index, party))
            {
                PartyIndex = -1;
                SendPartyFail(result);
                return;
            }

            var aiBuffer = new byte[16];
            var aiWriter = new PacketWriter(aiBuffer);
            aiWriter.SetByte(AiOpcode.AG_USER_PARTY);
            aiWriter.SetByte(PartyCreate);
            aiWriter.SetShort((short)party.Index);
            aiWriter.SetShort(party.Uid[0]);
            world.SendToAiServer?.Invoke(user.Zone, aiWriter.Written.ToArray());
        }

        member.PartyIndex = PartyIndex;

        var buffer = new byte[32];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_PARTY);
        writer.SetByte(PartyPermit);
        writer.SetShort(SocketId);
        writer.SetString2(Encoding.Latin1.GetBytes(user.CharId));
        member.Send(writer.Written);
    }

    private void SendPartyFail(short result)
    {
        var buffer = new byte[8];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_PARTY);
        writer.SetByte(PartyInsert);
        writer.SetShort(result);
        Send(writer.Written);
    }

    /// <summary>CUser::PartyInsert — the invitee accepted and joins.</summary>
    public void PartyInsertSelf()
    {
        if (UserData is not { } user)
            return;

        if (PartyIndex == -1)
            return;

        PartyGroup? party = world.Parties.GetValueOrDefault(PartyIndex);
        if (party is null)
        {
            PartyIndex = -1;
            return;
        }

        // Send the existing members to the newcomer.
        for (int i = 0; i < 8; i++)
        {
            if (party.Uid[i] == SocketId)
                continue;

            GameUser? existing = party.Uid[i] >= 0 && party.Uid[i] < world.Users.Length
                ? world.Users[party.Uid[i]]
                : null;
            if (existing?.UserData is not { } existingData)
                continue;

            var infoBuffer = new byte[64];
            var infoWriter = new PacketWriter(infoBuffer);
            infoWriter.SetByte((byte)GameOpcode.WIZ_PARTY);
            infoWriter.SetByte(PartyInsert);
            infoWriter.SetShort(party.Uid[i]);
            infoWriter.SetString2(Encoding.Latin1.GetBytes(existingData.CharId));
            infoWriter.SetShort(party.MaxHp[i]);
            infoWriter.SetShort(party.Hp[i]);
            infoWriter.SetByte(party.Level[i]);
            infoWriter.SetShort(party.Class[i]);
            infoWriter.SetShort(existing.MaxMp);
            infoWriter.SetShort(existingData.Mp);
            Send(infoWriter.Written);
        }

        int slot;
        for (slot = 0; slot < 8; slot++)
        {
            if (party.Uid[slot] != -1)
                continue;

            party.Uid[slot] = SocketId;
            party.MaxHp[slot] = MaxHp;
            party.Hp[slot] = user.Hp;
            party.Level[slot] = user.Level;
            party.Class[slot] = user.Class;
            break;
        }

        // Party-BBS bookkeeping: nobody in a party still needs one.
        GameUser? leader = party.Uid[0] >= 0 && party.Uid[0] < world.Users.Length
            ? world.Users[party.Uid[0]]
            : null;
        if (leader is null)
            return;

        if (leader.NeedParty == 2 && leader.PartyIndex != -1)
        {
            leader.NeedParty = 1;
            leader.StateChange([2, leader.NeedParty]);
        }

        if (NeedParty == 2 && PartyIndex != -1)
        {
            NeedParty = 1;
            StateChange([2, NeedParty]);
        }

        var buffer = new byte[64];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_PARTY);
        writer.SetByte(PartyInsert);
        writer.SetShort(SocketId);
        writer.SetString2(Encoding.Latin1.GetBytes(user.CharId));
        writer.SetShort(MaxHp);
        writer.SetShort(user.Hp);
        writer.SetByte(user.Level);
        writer.SetShort(user.Class);
        writer.SetShort(MaxMp);
        writer.SetShort(user.Mp);
        world.SendPartyMember(PartyIndex, writer.Written);

        var aiBuffer = new byte[16];
        var aiWriter = new PacketWriter(aiBuffer);
        aiWriter.SetByte(AiOpcode.AG_USER_PARTY);
        aiWriter.SetByte(PartyInsert);
        aiWriter.SetShort((short)party.Index);
        aiWriter.SetByte((byte)slot);
        aiWriter.SetShort(slot < 8 ? party.Uid[slot] : (short)-1);
        world.SendToAiServer?.Invoke(user.Zone, aiWriter.Written.ToArray());
    }

    /// <summary>CUser::PartyRemove — leave or kick (leader only for kicks).</summary>
    public void PartyRemoveMember(int memberId)
    {
        if (UserData is not { } user)
            return;

        if (PartyIndex == -1)
            return;

        GameUser? member = memberId >= 0 && memberId < world.Users.Length ? world.Users[memberId] : null;
        if (member is null)
            return;

        PartyGroup? party = world.Parties.GetValueOrDefault(PartyIndex);
        if (party is null)
        {
            member.PartyIndex = -1;
            PartyIndex = -1;
            return;
        }

        if (memberId != SocketId)
        {
            // Only the leader may kick.
            if (party.Uid[0] != SocketId)
                return;
        }
        else if (party.Uid[0] == memberId)
        {
            // The leader leaving disbands the party.
            PartyDisband();
            return;
        }

        int count = 0;
        for (int i = 0; i < 8; i++)
        {
            if (party.Uid[i] != -1 && party.Uid[i] != memberId)
                count++;
        }

        if (count == 1)
        {
            PartyDisband();
            return;
        }

        var buffer = new byte[8];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_PARTY);
        writer.SetByte(PartyRemove);
        writer.SetShort((short)memberId);
        world.SendPartyMember(PartyIndex, writer.Written);

        for (int i = 0; i < 8; i++)
        {
            if (party.Uid[i] != -1 && party.Uid[i] == memberId)
            {
                party.Uid[i] = -1;
                party.Hp[i] = 0;
                party.Level[i] = 0;
                party.Class[i] = 0;
                member.PartyIndex = -1;
            }
        }

        var aiBuffer = new byte[16];
        var aiWriter = new PacketWriter(aiBuffer);
        aiWriter.SetByte(AiOpcode.AG_USER_PARTY);
        aiWriter.SetByte(PartyRemove);
        aiWriter.SetShort((short)party.Index);
        aiWriter.SetShort((short)memberId);
        world.SendToAiServer?.Invoke(user.Zone, aiWriter.Written.ToArray());
    }

    /// <summary>CUser::PartyDelete — disband the whole group.</summary>
    public void PartyDisband()
    {
        if (PartyIndex == -1)
            return;

        PartyGroup? party = world.Parties.GetValueOrDefault(PartyIndex);
        if (party is null)
        {
            PartyIndex = -1;
            return;
        }

        for (int i = 0; i < 8; i++)
        {
            GameUser? member = party.Uid[i] >= 0 && party.Uid[i] < world.Users.Length
                ? world.Users[party.Uid[i]]
                : null;
            if (member is not null)
                member.PartyIndex = -1;
        }

        var buffer = new byte[4];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_PARTY);
        writer.SetByte(PartyDelete);
        world.SendPartyMember(party.Index, writer.Written);

        var aiBuffer = new byte[8];
        var aiWriter = new PacketWriter(aiBuffer);
        aiWriter.SetByte(AiOpcode.AG_USER_PARTY);
        aiWriter.SetByte(PartyDelete);
        aiWriter.SetShort((short)party.Index);
        world.SendToAiServer?.Invoke(UserData?.Zone ?? 0, aiWriter.Written.ToArray());

        world.Parties.Remove(party.Index);
    }

    /// <summary>CUser::GetItemRoutingUser — round-robin loot distribution.</summary>
    public GameUser? GetItemRoutingUser(int itemId, short itemCount)
    {
        _ = itemCount;

        if (PartyIndex == -1)
            return null;

        PartyGroup? party = world.Parties.GetValueOrDefault(PartyIndex);
        if (party is null)
            return null;

        if (party.ItemRouting > 7)
            return null;

        Item? table = world.ItemTable.GetValueOrDefault(itemId);
        if (table is null)
            return null;

        int count = 0;
        while (count < 8)
        {
            int selected = party.Uid[party.ItemRouting];
            GameUser? user = selected >= 0 && selected < world.Users.Length ? world.Users[selected] : null;
            if (user is not null)
            {
                // C++ quirk kept as-is: the weight check multiplies by the
                // loop counter, not the item count.
                int addedWeight = table.Countable != 0 ? table.Weight * count : table.Weight;
                if (addedWeight + user.ItemWeight <= user.MaxWeight)
                {
                    party.ItemRouting++;
                    if (party.ItemRouting > 6)
                        party.ItemRouting = 0;

                    return user;
                }
            }

            // C++ quirk kept as-is: the fallback advance wraps BEFORE incrementing.
            if (party.ItemRouting > 6)
                party.ItemRouting = 0;
            else
                party.ItemRouting++;

            count++;
        }

        return null;
    }
}

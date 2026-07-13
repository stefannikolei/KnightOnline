using System.Text;
using OpenKO.Core.Protocol;
using OpenKO.Network;

namespace OpenKO.Client.Game.Net;

/// <summary>WIZ_VERSION_CHECK reply — the version and the crypt public key.</summary>
public readonly record struct VersionCheckResult(short Version, ulong PublicKey);

/// <summary>One visible equipment slot in a character-select entry.</summary>
public readonly record struct EquipmentSlot(uint ItemId, short Durability);

/// <summary>One character-select slot (empty when <see cref="CharId"/> is blank).</summary>
public sealed record CharacterSlot(
    string CharId, byte Race, short Class, byte Level, byte Face, byte Hair, byte Zone,
    IReadOnlyList<EquipmentSlot> Equipment)
{
    public bool IsEmpty => CharId.Length == 0;
}

/// <summary>WIZ_ALLCHAR_INFO_REQ reply (result + exactly three slots).</summary>
public readonly record struct AllCharInfoResult(byte Result, IReadOnlyList<CharacterSlot> Slots);

/// <summary>WIZ_SEL_CHAR reply (result + spawn zone/position).</summary>
public readonly record struct SelectCharResult(
    byte Result, byte Zone, ushort X, ushort Z, short Y, byte VictoryNation)
{
    public bool Success => Result == 1;
}

/// <summary>
/// The client Ebenezer (game-server) request builders and reply parsers for the
/// login→char-select opcode set (the WIZ_* half of CGameProcedure /
/// CGameProcCharacterSelect). Field order is pinned against the C# Ebenezer.
/// Payloads are opcode + body; the socket core adds crypto + framing.
/// </summary>
public static class GameProtocol
{
    public const int MaxCharacters = 3; // MAX_AVAILABLE_CHARACTER
    public const int VisibleEquipment = 8;

    private static readonly Encoding Ascii = Encoding.Latin1;

    public static byte[] BuildVersionCheck() => [(byte)GameOpcode.WIZ_VERSION_CHECK];

    public static byte[] BuildAllCharInfoRequest() => [(byte)GameOpcode.WIZ_ALLCHAR_INFO_REQ];

    public static byte[] BuildGameLogin(string account, string password)
    {
        var buffer = new byte[5 + account.Length + password.Length];
        var w = new PacketWriter(buffer);
        w.SetByte((byte)GameOpcode.WIZ_LOGIN);
        w.SetString2(Ascii.GetBytes(account));
        w.SetString2(Ascii.GetBytes(password));
        return w.Written.ToArray();
    }

    public static byte[] BuildSelectNation(byte nation) => [(byte)GameOpcode.WIZ_SEL_NATION, nation];

    public static byte[] BuildSelectCharacter(string account, string charId, byte zoneInit, byte zoneCur)
    {
        var buffer = new byte[7 + account.Length + charId.Length];
        var w = new PacketWriter(buffer);
        w.SetByte((byte)GameOpcode.WIZ_SEL_CHAR);
        w.SetString2(Ascii.GetBytes(account));
        w.SetString2(Ascii.GetBytes(charId));
        w.SetByte(zoneInit);
        w.SetByte(zoneCur);
        return w.Written.ToArray();
    }

    public static byte[] BuildDeleteCharacter(byte slot, string charId, string verify)
    {
        var buffer = new byte[6 + charId.Length + verify.Length];
        var w = new PacketWriter(buffer);
        w.SetByte((byte)GameOpcode.WIZ_DEL_CHAR);
        w.SetByte(slot);
        w.SetString2(Ascii.GetBytes(charId));
        w.SetString2(Ascii.GetBytes(verify));
        return w.Written.ToArray();
    }

    public static byte[] BuildNewCharacter(
        byte slot, string charId, byte race, short charClass, byte face, byte hair,
        byte str, byte sta, byte dex, byte intel, byte cha)
    {
        var buffer = new byte[16 + charId.Length];
        var w = new PacketWriter(buffer);
        w.SetByte((byte)GameOpcode.WIZ_NEW_CHAR);
        w.SetByte(slot);
        w.SetString2(Ascii.GetBytes(charId));
        w.SetByte(race);
        w.SetShort(charClass);
        w.SetByte(face);
        w.SetByte(hair);
        w.SetByte(str);
        w.SetByte(sta);
        w.SetByte(dex);
        w.SetByte(intel);
        w.SetByte(cha);
        return w.Written.ToArray();
    }

    /// <summary>MsgSend_GameStart phase 1 ([WIZ_GAMESTART][0x01]).</summary>
    public static byte[] BuildGameStartRequest() => [(byte)GameOpcode.WIZ_GAMESTART, 0x01];

    /// <summary>The client's phase-2 reply after the server's WIZ_GAMESTART.</summary>
    public static byte[] BuildGameStartAck() => [(byte)GameOpcode.WIZ_GAMESTART, 0x02];

    public static VersionCheckResult ParseVersionCheck(ReadOnlySpan<byte> payload)
    {
        var r = new PacketReader(payload);
        r.GetByte(); // opcode
        short version = r.GetShort();
        ulong key = unchecked((ulong)r.GetInt64());
        return new VersionCheckResult(version, key);
    }

    /// <summary>WIZ_LOGIN reply: the nation byte (0 not selected, 1 Karus, 2 El Morad, 0xFF fail).</summary>
    public static byte ParseGameLogin(ReadOnlySpan<byte> payload) => payload[1];

    public static byte ParseSelectNation(ReadOnlySpan<byte> payload) => payload[1];

    public static AllCharInfoResult ParseAllCharInfo(ReadOnlySpan<byte> payload)
    {
        var r = new PacketReader(payload);
        r.GetByte(); // opcode
        byte result = r.GetByte();

        var slots = new List<CharacterSlot>(MaxCharacters);
        for (int i = 0; i < MaxCharacters && r.Remaining > 0; i++)
        {
            int idLen = r.GetShort();
            string charId = idLen > 0 ? Ascii.GetString(r.GetString(idLen)) : string.Empty;
            byte race = r.GetByte();
            short cls = r.GetShort();
            byte level = r.GetByte();
            byte face = r.GetByte();
            byte hair = r.GetByte();
            byte zone = r.GetByte();

            var equipment = new EquipmentSlot[VisibleEquipment];
            for (int e = 0; e < VisibleEquipment; e++)
                equipment[e] = new EquipmentSlot(r.GetDWord(), r.GetShort());

            slots.Add(new CharacterSlot(charId, race, cls, level, face, hair, zone, equipment));
        }

        return new AllCharInfoResult(result, slots);
    }

    public static SelectCharResult ParseSelectCharacter(ReadOnlySpan<byte> payload)
    {
        var r = new PacketReader(payload);
        r.GetByte(); // opcode
        byte result = r.GetByte();
        if (result != 1)
            return new SelectCharResult(result, 0, 0, 0, 0, 0);

        byte zone = r.GetByte();
        ushort x = (ushort)r.GetShort();
        ushort z = (ushort)r.GetShort();
        short y = r.GetShort();
        byte victory = r.GetByte();
        return new SelectCharResult(result, zone, x, z, y, victory);
    }
}

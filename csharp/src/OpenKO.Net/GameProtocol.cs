using OpenKO.Common;

namespace OpenKO.Net;

/// <summary>Character slot shown on the character-select screen.</summary>
public readonly record struct CharacterSlotInfo(string Name, byte Race, short ClassId, byte Level, byte Face, byte Hair, byte Zone);

/// <summary>Response for <see cref="GameOpcode.AllCharInfoReq"/>.</summary>
public readonly record struct CharacterListResult(bool Success, IReadOnlyList<CharacterSlotInfo> Characters);

/// <summary>Protocol helpers for game-server pre-game flow (version/login/character list/select).</summary>
public static class GameProtocol
{
    public const int MaxCharacterSlots = 3;

    /// <summary>Build <c>WIZ_ALLCHAR_INFO_REQ</c>.</summary>
    public static Packet BuildAllCharacterInfoRequest() => new(GameOpcode.AllCharInfoReq);

    /// <summary>Build <c>WIZ_SEL_CHAR</c> with account/character and zone metadata.</summary>
    public static Packet BuildCharacterSelect(string account, string characterName, byte zoneInit, byte zoneCurrent)
    {
        if (string.IsNullOrEmpty(account) || string.IsNullOrEmpty(characterName))
            throw new ArgumentException("Account and character name are required.");

        var pkt = new Packet(GameOpcode.SelChar);
        pkt.DByte();
        pkt.AppendString(account);
        pkt.AppendString(characterName);
        pkt.Append(zoneInit);
        pkt.Append(zoneCurrent);
        return pkt;
    }

    /// <summary>Parse <c>WIZ_ALLCHAR_INFO_REQ</c> response as emitted by the original server.</summary>
    public static CharacterListResult ParseCharacterList(Packet packet)
    {
        packet.SyncForRead();
        packet.Read<byte>(); // opcode
        byte result = packet.Read<byte>();
        if (result != 0x01)
            return new CharacterListResult(false, Array.Empty<CharacterSlotInfo>());

        var characters = new List<CharacterSlotInfo>(MaxCharacterSlots);
        for (int i = 0; i < MaxCharacterSlots; i++)
        {
            short nameLength = packet.Read<short>();
            string name = nameLength > 0 ? packet.ReadStringFixed(nameLength) : string.Empty;
            byte race = packet.Read<byte>();
            short classId = packet.Read<short>();
            byte level = packet.Read<byte>();
            byte face = packet.Read<byte>();
            byte hair = packet.Read<byte>();
            byte zone = packet.Read<byte>();

            // appearance equipment block (kept for packet alignment)
            packet.Read<uint>(); packet.Read<short>(); // helmet
            packet.Read<uint>(); packet.Read<short>(); // upper
            packet.Read<uint>(); packet.Read<short>(); // cloak
            packet.Read<uint>(); packet.Read<short>(); // right hand
            packet.Read<uint>(); packet.Read<short>(); // left hand
            packet.Read<uint>(); packet.Read<short>(); // lower
            packet.Read<uint>(); packet.Read<short>(); // gloves
            packet.Read<uint>(); packet.Read<short>(); // shoes

            characters.Add(new CharacterSlotInfo(name, race, classId, level, face, hair, zone));
        }

        return new CharacterListResult(true, characters);
    }
}

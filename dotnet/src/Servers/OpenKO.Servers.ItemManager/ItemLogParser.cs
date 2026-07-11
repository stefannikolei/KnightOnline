using System.Text;
using OpenKO.Core.Protocol;
using OpenKO.Core.Text;
using OpenKO.Network;

namespace OpenKO.Servers.ItemManager;

/// <summary>
/// Port of <c>ItemManagerApp::ItemLogWrite / ExpLogWrite</c>: parses the queue
/// message bodies and produces the same log lines the C++ spdlog loggers write
/// ("src, tar, type, serial, item, count, dure" / "account, char, type, level,
/// exp, loyalty, money"). Names are CP949 on the wire.
/// </summary>
public static class ItemLogParser
{
    /// <summary>WIZ_ITEM_LOG body (after the opcode). Returns null on validation failure.</summary>
    public static string? ParseItemLog(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        try
        {
            int srcLen = reader.GetShort();
            if (srcLen <= 0 || srcLen > ProtocolConstants.MaxIdSize || srcLen > reader.Remaining)
                return null;

            string srcId = KoEncoding.Cp949.GetString(reader.GetString(srcLen));

            int tarLen = reader.GetShort();
            if (tarLen <= 0 || tarLen > ProtocolConstants.MaxIdSize || tarLen > reader.Remaining)
                return null;

            string tarId = KoEncoding.Cp949.GetString(reader.GetString(tarLen));

            byte type = reader.GetByte();
            long serial = reader.GetInt64();
            uint itemId = reader.GetDWord();
            short count = reader.GetShort();
            short durability = reader.GetShort();

            return $"{srcId}, {tarId}, {type}, {serial}, {itemId}, {count}, {durability}";
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
        catch (IndexOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>WIZ_DATASAVE body (after the opcode). Returns null on validation failure.</summary>
    public static string? ParseExpLog(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        try
        {
            int accountLen = reader.GetShort();
            if (accountLen <= 0 || accountLen > ProtocolConstants.MaxIdSize || accountLen > reader.Remaining)
                return null;

            string accountName = KoEncoding.Cp949.GetString(reader.GetString(accountLen));

            int charLen = reader.GetShort();
            if (charLen <= 0 || charLen > ProtocolConstants.MaxIdSize || charLen > reader.Remaining)
                return null;

            string charId = KoEncoding.Cp949.GetString(reader.GetString(charLen));

            byte type = reader.GetByte();
            byte level = reader.GetByte();
            uint exp = reader.GetDWord();
            uint loyalty = reader.GetDWord();
            uint money = reader.GetDWord();

            return $"{accountName}, {charId}, {type}, {level}, {exp}, {loyalty}, {money}";
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
        catch (IndexOutOfRangeException)
        {
            return null;
        }
    }
}

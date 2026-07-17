using System.Buffers.Binary;
using System.Text;
using OpenKO.Client.Game.Net;
using OpenKO.Core.Protocol;
using Xunit;

namespace OpenKO.Client.Game.Tests;

/// <summary>
/// Sub-slice 9.9 pins: the quest/NPC-dialogue + notice protocol. Every builder is asserted byte-exact
/// against the cited C++ layout, and the WIZ_SELECT_MSG / WIZ_NPC_SAY / WIZ_NOTICE parsers are exercised
/// (including the -1 menu filter, the two discarded talk headers and the read-past-end → 0 rule).
/// </summary>
public class QuestProtocolTests
{
    private const uint Empty = 0xFFFFFFFF; // -1 slot

    private sealed class Pkt
    {
        private readonly List<byte> _b = [];
        public Pkt Byte(int v) { _b.Add((byte)v); return this; }
        public Pkt Short(int v) { Span<byte> s = stackalloc byte[2]; BinaryPrimitives.WriteInt16LittleEndian(s, (short)v); _b.AddRange(s.ToArray()); return this; }
        public Pkt DWord(uint v) { Span<byte> s = stackalloc byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(s, v); _b.AddRange(s.ToArray()); return this; }
        public Pkt Str1(string v) { byte[] raw = Encoding.ASCII.GetBytes(v); _b.Add((byte)raw.Length); _b.AddRange(raw); return this; }
        public byte[] Done() => _b.ToArray();
    }

    // ---- builders ----------------------------------------------------------

    [Fact]
    public void BuildSelectMenu_IsOpcodeThenIndexByte()
    {
        byte[] p = QuestProtocol.BuildSelectMenu(3);
        Assert.Equal([(byte)GameOpcode.WIZ_SELECT_MSG, 3], p);
    }

    [Fact]
    public void BuildNpcEvent_IsOpcodeThenInt16Target()
    {
        byte[] p = QuestProtocol.BuildNpcEvent(0x0102);
        Assert.Equal(3, p.Length);
        Assert.Equal((byte)GameOpcode.WIZ_NPC_EVENT, p[0]);
        Assert.Equal((short)0x0102, BitConverter.ToInt16(p, 1));
    }

    // ---- WIZ_SELECT_MSG parse ----------------------------------------------

    [Fact]
    public void ParseSelectMsg_ReadsNpcMainAndFiltersEmptyMenuSlots()
    {
        var pkt = new Pkt().Byte((byte)GameOpcode.WIZ_SELECT_MSG).Short(1234).DWord(5678);
        // 10 menu ids: keep 10, 20, 30; drop -1 and the high-bit 0x80000000.
        pkt.DWord(10).DWord(Empty).DWord(20).DWord(Empty).DWord(30)
           .DWord(0x80000000).DWord(Empty).DWord(Empty).DWord(Empty).DWord(Empty);

        QuestMenuData data = QuestProtocol.ParseSelectMsg(pkt.Done());
        Assert.Equal((short)1234, data.NpcId);
        Assert.Equal(5678u, data.MainTalkId);
        Assert.Equal([10u, 20u, 30u], data.MenuIds);
    }

    [Fact]
    public void ParseSelectMsg_KeepsZeroAsAValidMenuId()
    {
        // menu id 0 is a legitimate table row (only -1 / high-bit are empty slots).
        var pkt = new Pkt().Byte((byte)GameOpcode.WIZ_SELECT_MSG).Short(1).DWord(2)
            .DWord(0).DWord(Empty).DWord(Empty).DWord(Empty).DWord(Empty)
            .DWord(Empty).DWord(Empty).DWord(Empty).DWord(Empty).DWord(Empty);

        QuestMenuData data = QuestProtocol.ParseSelectMsg(pkt.Done());
        Assert.Equal([0u], data.MenuIds);
    }

    // ---- WIZ_NPC_SAY parse -------------------------------------------------

    [Fact]
    public void ParseNpcSay_DiscardsTwoHeadersAndKeepsNonZeroTalkIds()
    {
        var pkt = new Pkt().Byte((byte)GameOpcode.WIZ_NPC_SAY)
            .DWord(Empty).DWord(Empty)          // two discarded headers
            .DWord(100).DWord(200).DWord(0).DWord(300); // 0 = no talk, kept ids in order

        QuestTalkData data = QuestProtocol.ParseNpcSay(pkt.Done());
        Assert.Equal([100u, 200u, 300u], data.TalkIds);
    }

    [Fact]
    public void ParseNpcSay_ReadsPastEndAsZero()
    {
        // Only the two headers present: the 10-slot loop reads past the end → all 0 → no pages.
        var pkt = new Pkt().Byte((byte)GameOpcode.WIZ_NPC_SAY).DWord(Empty).DWord(Empty);
        QuestTalkData data = QuestProtocol.ParseNpcSay(pkt.Done());
        Assert.Empty(data.TalkIds);
    }

    // ---- WIZ_NOTICE parse --------------------------------------------------

    [Fact]
    public void ParseNotice_ReadsCountAndString1Lines()
    {
        var pkt = new Pkt().Byte((byte)GameOpcode.WIZ_NOTICE).Byte(2).Str1("Welcome").Str1("Server restart at 6pm");
        IReadOnlyList<string> lines = NoticeProtocol.ParseNotice(pkt.Done());
        Assert.Equal(2, lines.Count);
        Assert.Equal("Welcome", lines[0]);
        Assert.Equal("Server restart at 6pm", lines[1]);
    }

    [Fact]
    public void ParseNotice_EmptyIsNoLines()
    {
        byte[] payload = new Pkt().Byte((byte)GameOpcode.WIZ_NOTICE).Byte(0).Done();
        Assert.Empty(NoticeProtocol.ParseNotice(payload));
    }
}

using System.Diagnostics;
using OpenKO.Core.Compression;
using OpenKO.Core.Crypto;
using OpenKO.Core.Protocol;
using OpenKO.Network;

namespace OpenKO.Servers.Ebenezer;

/// <summary>
/// The EbenezerApp AISocket bookkeeping (EbenezerApp.cpp): the per-index AI
/// link table, the round-robin Send_AIServer, the SERVER_INFO/USER_INFO_ALL
/// startup download and the NPC-list teardown after a lost AI connection.
/// </summary>
public sealed partial class EbenezerWorld
{
    public const int MaxAiSocket = 10; // MAX_AI_SOCKET

    public const byte ServerInfoStart = 1; // SERVER_INFO_START
    public const byte ServerInfoEnd = 2;   // SERVER_INFO_END

    public const int UserBand = 0;     // USER_BAND
    public const int NpcBand = 10000;  // NPC_BAND

    private static readonly Stopwatch StartClock = Stopwatch.StartNew();

    /// <summary>m_AISocketMap keyed by the socket index 0..9.</summary>
    public readonly Dictionary<int, AiLink> AiSockets = [];

    /// <summary>TimeGet() — seconds since start; injectable for the reconnect-window tests.</summary>
    public Func<double> Clock = () => StartClock.Elapsed.TotalSeconds;

    public short SocketCount;       // m_sSocketCount
    public short ReSocketCount;     // m_sReSocketCount
    public double ReConnectStart;   // m_fReConnectStart
    public bool ServerCheckFlag;    // m_bServerCheckFlag
    public bool FirstServerFlag;    // m_bFirstServerFlag
    public short ZoneCount;         // m_sZoneCount
    public short ErrorSocketCount;  // m_sErrorSocketCount
    public short SendSocket;        // m_sSendSocket (round-robin cursor)

    /// <summary>UserAcceptThread() — the host starts accepting game clients here.</summary>
    public Action? UserAccept;

    // SendCompressedData accumulator (m_CompBuf/m_iCompIndex/m_CompCount).
    private readonly byte[] _compBuffer = new byte[10240];
    private int _compIndex;
    private short _compCount;

    /// <summary>
    /// EbenezerApp::Send_AIServer — the zone argument is ignored (like the C++);
    /// packets round-robin over the ten AI links so the AIServer's receive
    /// threads share the load.
    /// </summary>
    public void SendAiServer(int zone, byte[] packet)
    {
        _ = zone;

        for (int i = 0; i < MaxAiSocket; i++)
        {
            AiLink? link = AiSockets.GetValueOrDefault(i);
            if (link is null)
            {
                SendSocket++;
                if (SendSocket >= MaxAiSocket)
                    SendSocket = 0;

                continue;
            }

            if (i == SendSocket)
            {
                bool sent = link.Send(packet);
                SendSocket++;
                if (SendSocket >= MaxAiSocket)
                    SendSocket = 0;

                if (!sent)
                    continue;

                return;
            }
        }
    }

    /// <summary>
    /// EbenezerApp::SendAllUserInfo — SERVER_INFO START, the in-game users in
    /// compressed batches of 20, the party groups, SERVER_INFO END. The C++
    /// remainder condition `count < tot - 1` silently drops a leftover batch of
    /// exactly 19 users; kept as-is.
    /// </summary>
    public void SendAllUserInfo()
    {
        var start = new byte[] { AiOpcode.AG_SERVER_INFO, ServerInfoStart };
        SendAiServer(1000, start);

        const int tot = 20;
        var buffer = new byte[2048];
        var writer = new PacketWriter(buffer) { Index = 2 };
        int count = 0;

        foreach (GameUser? user in Users)
        {
            // The C++ writes every allocated socket's MMF block; sockets that
            // have no character data yet would desync count vs blob here.
            if (user?.UserData is null)
                continue;

            user.SendUserInfo(ref writer);
            count++;

            if (count == tot)
            {
                buffer[0] = AiOpcode.AG_USER_INFO_ALL;
                buffer[1] = (byte)count;

                _compCount++;
                buffer.AsSpan(0, writer.Index).CopyTo(_compBuffer);
                _compIndex = writer.Index;
                SendCompressedData();

                Array.Clear(buffer);
                writer = new PacketWriter(buffer) { Index = 2 };
                count = 0;
            }
        }

        if (count != 0 && count < tot - 1)
        {
            buffer[0] = AiOpcode.AG_USER_INFO_ALL;
            buffer[1] = (byte)count;
            SendAiServer(1000, buffer.AsSpan(0, writer.Index).ToArray());
        }

        // Re-announce the party groups.
        foreach (PartyGroup party in Parties.Values)
        {
            var partyBuffer = new byte[24];
            var partyWriter = new PacketWriter(partyBuffer);
            partyWriter.SetByte(AiOpcode.AG_PARTY_INFO_ALL);
            partyWriter.SetShort((short)party.Index);
            for (int j = 0; j < 8; j++)
                partyWriter.SetShort(party.Uid[j]);

            SendAiServer(1000, partyBuffer.AsSpan(0, partyWriter.Index).ToArray());
        }

        var end = new byte[] { AiOpcode.AG_SERVER_INFO, ServerInfoEnd };
        SendAiServer(1000, end);
    }

    /// <summary>EbenezerApp::SendCompressedData — wraps the accumulator into AG_COMPRESSED_DATA.</summary>
    public void SendCompressedData()
    {
        if (_compCount <= 0 || _compIndex <= 0)
        {
            _compCount = 0;
            _compIndex = 0;
            return;
        }

        var compressed = new byte[32000];
        int compLen = Lzf.Compress(_compBuffer.AsSpan(0, _compIndex), compressed);
        if (compLen == 0 || compLen > compressed.Length)
        {
            return;
        }

        uint crc = KoCrc32.Compute(_compBuffer.AsSpan(0, _compIndex));

        var packet = new byte[11 + compLen];
        var writer = new PacketWriter(packet);
        writer.SetByte(AiOpcode.AG_COMPRESSED_DATA);
        writer.SetShort(compLen);
        writer.SetShort(_compIndex);
        writer.SetDWord(crc);
        writer.SetShort(_compCount);
        writer.SetString(compressed.AsSpan(0, compLen));

        SendAiServer(1000, packet);

        _compCount = 0;
        _compIndex = 0;
    }

    /// <summary>
    /// EbenezerApp::DeleteAllNpcList — clears the NPC mirror once every AI link
    /// is gone. The first call only lowers PointCheckFlag (so in-flight reads
    /// stop); the sweep itself runs on the next tick.
    /// </summary>
    public void DeleteAllNpcList()
    {
        if (!ServerCheckFlag)
            return;

        if (PointCheckFlag)
        {
            PointCheckFlag = false;
            return;
        }

        foreach (GameZone zone in Zones)
        {
            for (int x = 0; x <= zone.XRegionMax; x++)
            {
                for (int z = 0; z <= zone.ZRegionMax; z++)
                    zone.Regions[x, z].Npcs.Clear();
            }
        }

        Npcs.Clear();
        ServerCheckFlag = false;
    }
}

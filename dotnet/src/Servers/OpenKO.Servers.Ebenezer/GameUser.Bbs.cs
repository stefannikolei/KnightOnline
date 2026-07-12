using System.Text;
using OpenKO.Core.Protocol;
using OpenKO.Data;
using OpenKO.Network;

namespace OpenKO.Servers.Ebenezer;

/// <summary>
/// The CUser bulletin-board slice (User.cpp): the WIZ_PARTY_BBS party finder
/// and the WIZ_MARKET_BBS buy/sell boards.
/// </summary>
public sealed partial class GameUser
{
    // e_PartyBbsOpcode / e_MarketBbsOpcode (shared/packets.h).
    public const byte PartyBbsRegister = 0x01;
    public const byte PartyBbsDelete = 0x02;
    public const byte PartyBbsNeeded = 0x03;

    public const byte MarketBbsRegister = 0x01;
    public const byte MarketBbsDelete = 0x02;
    public const byte MarketBbsReport = 0x03;
    public const byte MarketBbsOpen = 0x04;
    public const byte MarketBbsRemotePurchase = 0x05;
    public const byte MarketBbsMessage = 0x06;

    public const byte MarketBbsBuy = 0x01;
    public const byte MarketBbsSell = 0x02;

    private const int MaxBbsPage = 23;       // MAX_BBS_PAGE
    private const int MaxBbsMessage = 40;    // MAX_BBS_MESSAGE
    private const int MaxBbsTitle = 20;      // MAX_BBS_TITLE
    private const int BuyPostPrice = 500;    // BUY_POST_PRICE
    private const int SellPostPrice = 1000;  // SELL_POST_PRICE
    private const int RemotePurchasePrice = 5000;

    /// <summary>CUser::PartyBBS — WIZ_PARTY_BBS dispatch.</summary>
    public void PartyBbs(ReadOnlySpan<byte> body)
    {
        if (body.Length < 1)
            return;

        switch (body[0])
        {
            case PartyBbsRegister:
                PartyBbsRegisterPost();
                break;

            case PartyBbsDelete:
                PartyBbsDeletePost();
                break;

            case PartyBbsNeeded:
                PartyBbsList(body[1..], PartyBbsNeeded);
                break;
        }
    }

    /// <summary>CUser::PartyBBSRegister.</summary>
    public void PartyBbsRegisterPost()
    {
        if (UserData is not { } user)
            return;

        if (PartyIndex != -1 || NeedParty == 2)
        {
            var failBuffer = new byte[4];
            var failWriter = new PacketWriter(failBuffer);
            failWriter.SetByte((byte)GameOpcode.WIZ_PARTY_BBS);
            failWriter.SetByte(PartyBbsRegister);
            failWriter.SetByte(0);
            Send(failWriter.Written);
            return;
        }

        NeedParty = 2;

        // Broadcast the new need-party state through StateChange.
        StateChange([2, NeedParty]);

        // Find which page this poster lands on.
        int counter = 0;
        foreach (GameUser? other in world.Users)
        {
            if (other?.UserData is not { } otherData)
                continue;

            if (otherData.Nation != user.Nation)
                continue;

            if (other.NeedParty == 1)
                continue;

            if (!PartyBbsLevelMatch(otherData.Level, user.Level))
                continue;

            if (other.SocketId == SocketId)
                break;

            ++counter;
        }

        var page = new byte[2];
        var pageWriter = new PacketWriter(page);
        pageWriter.SetShort(counter / MaxBbsPage);
        PartyBbsList(page, PartyBbsRegister);
    }

    /// <summary>
    /// The register/list level window — the first clause keeps the upstream
    /// equality quirk (&lt;= AND &gt;= the SAME (int)(level*1.5)).
    /// </summary>
    private static bool PartyBbsLevelMatch(int otherLevel, int myLevel)
    {
        return (otherLevel <= (int)(myLevel * 1.5) && otherLevel >= (int)(myLevel * 1.5))
            || (otherLevel <= myLevel + 8 && otherLevel >= myLevel - 8);
    }

    /// <summary>CUser::PartyBBSDelete.</summary>
    public void PartyBbsDeletePost()
    {
        if (NeedParty == 1)
        {
            var failBuffer = new byte[4];
            var failWriter = new PacketWriter(failBuffer);
            failWriter.SetByte((byte)GameOpcode.WIZ_PARTY_BBS);
            failWriter.SetByte(PartyBbsDelete);
            failWriter.SetByte(0);
            Send(failWriter.Written);
            return;
        }

        NeedParty = 1;
        StateChange([2, NeedParty]);

        PartyBbsList([0, 0], PartyBbsDelete);
    }

    /// <summary>CUser::PartyBBSNeeded — one 23-row page of party seekers.</summary>
    public void PartyBbsList(ReadOnlySpan<byte> body, byte type)
    {
        if (UserData is not { } user)
            return;

        var reader = new PacketReader(body);
        short pageIndex = reader.GetShort();
        int startCounter = pageIndex * MaxBbsPage;

        if (startCounter < 0 || startCounter >= world.Users.Length)
        {
            var failBuffer = new byte[4];
            var failWriter = new PacketWriter(failBuffer);
            failWriter.SetByte((byte)GameOpcode.WIZ_PARTY_BBS);
            failWriter.SetByte(PartyBbsNeeded);
            failWriter.SetByte(0);
            Send(failWriter.Written);
            return;
        }

        var buffer = new byte[16 + MaxBbsPage * 32];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_PARTY_BBS);
        writer.SetByte(type);
        writer.SetByte(1);

        short bbsCounter = 0;
        byte validCounter = 0;

        for (int i = 0; i < world.Users.Length; i++)
        {
            GameUser? other = world.Users[i];
            if (other?.UserData is not { } otherData)
                continue;

            if (otherData.Nation != user.Nation)
                continue;

            if (other.NeedParty == 1)
                continue;

            if (!PartyBbsLevelMatch(otherData.Level, user.Level))
                continue;

            bbsCounter++;

            if (i < startCounter)
                continue;

            if (validCounter >= MaxBbsPage)
                continue;

            writer.SetString2(Encoding.Latin1.GetBytes(otherData.CharId));
            writer.SetByte(otherData.Level);
            writer.SetShort(otherData.Class);
            ++validCounter;
        }

        for (int j = validCounter; j < MaxBbsPage; j++)
        {
            writer.SetShort(0);
            writer.SetByte(0);
            writer.SetShort(0);
        }

        writer.SetShort(pageIndex);
        writer.SetShort(bbsCounter);
        Send(writer.Written);
    }

    /// <summary>CUser::MarketBBS — WIZ_MARKET_BBS dispatch.</summary>
    public void MarketBbs(ReadOnlySpan<byte> body)
    {
        if (body.Length < 1)
            return;

        world.MarketBbsBuyPostFilter();
        world.MarketBbsSellPostFilter();

        switch (body[0])
        {
            case MarketBbsRegister:
                MarketBbsRegisterPost(body[1..]);
                break;

            case MarketBbsDelete:
                MarketBbsDeletePost(body[1..]);
                break;

            case MarketBbsReport:
                MarketBbsReportPage(body[1..], MarketBbsReport);
                break;

            case MarketBbsOpen:
                MarketBbsReportPage(body[1..], MarketBbsOpen);
                break;

            case MarketBbsRemotePurchase:
                MarketBbsRemotePurchasePost(body[1..]);
                break;

            case MarketBbsMessage:
                MarketBbsMessagePost(body[1..]);
                break;
        }
    }

    private void SendMarketBbsFail(byte type, byte buySell, byte subResult)
    {
        var buffer = new byte[8];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_MARKET_BBS);
        writer.SetByte(type);
        writer.SetByte(buySell);
        writer.SetByte(0);
        writer.SetByte(subResult);
        Send(writer.Written);
    }

    /// <summary>CUser::MarketBBSRegister — costs 500 (buy) / 1000 (sell) gold.</summary>
    public void MarketBbsRegisterPost(ReadOnlySpan<byte> body)
    {
        if (UserData is not { } user)
            return;

        var reader = new PacketReader(body);
        byte buySell = reader.GetByte();

        if (buySell == MarketBbsBuy && user.Gold < BuyPostPrice)
        {
            SendMarketBbsFail(MarketBbsRegister, buySell, 2);
            return;
        }

        if (buySell == MarketBbsSell && user.Gold < SellPostPrice)
        {
            SendMarketBbsFail(MarketBbsRegister, buySell, 2);
            return;
        }

        if (buySell is not MarketBbsBuy and not MarketBbsSell)
        {
            SendMarketBbsFail(MarketBbsRegister, buySell, 1);
            return;
        }

        MarketBbsBoard board = buySell == MarketBbsBuy ? world.MarketBuy : world.MarketSell;

        int slot = -1;
        for (int i = 0; i < MarketBbsBoard.MaxPosts; i++)
        {
            if (board.PosterId[i] == -1)
            {
                board.PosterId[i] = SocketId;

                int titleLen = reader.GetShort();
                board.Title[i] = Encoding.Latin1.GetString(reader.GetString(titleLen));
                int messageLen = reader.GetShort();
                board.Message[i] = Encoding.Latin1.GetString(reader.GetString(messageLen));
                board.Price[i] = (int)reader.GetDWord();
                board.StartTime[i] = world.Clock();

                slot = i;
                break;
            }
        }

        if (slot == -1)
        {
            SendMarketBbsFail(MarketBbsRegister, buySell, 1);
            return;
        }

        int price = buySell == MarketBbsBuy ? BuyPostPrice : SellPostPrice;
        user.Gold -= price;

        var goldBuffer = new byte[12];
        var goldWriter = new PacketWriter(goldBuffer);
        goldWriter.SetByte((byte)GameOpcode.WIZ_GOLD_CHANGE);
        goldWriter.SetByte(2); // GOLD_CHANGE_LOSE
        goldWriter.SetDWord((uint)price);
        goldWriter.SetDWord((uint)user.Gold);
        Send(goldWriter.Written);

        var report = new byte[4];
        var reportWriter = new PacketWriter(report);
        reportWriter.SetByte(buySell);
        reportWriter.SetShort(slot / MaxBbsPage);
        MarketBbsReportPage(report, MarketBbsRegister);
    }

    /// <summary>CUser::MarketBBSDelete.</summary>
    public void MarketBbsDeletePost(ReadOnlySpan<byte> body)
    {
        if (UserData is not { } user)
            return;

        var reader = new PacketReader(body);
        byte buySell = reader.GetByte();
        short deleteId = reader.GetShort();

        if (deleteId < 0 || deleteId >= MarketBbsBoard.MaxPosts
            || buySell is not MarketBbsBuy and not MarketBbsSell)
        {
            SendMarketBbsFail(MarketBbsDelete, buySell, 1);
            return;
        }

        MarketBbsBoard board = buySell == MarketBbsBuy ? world.MarketBuy : world.MarketSell;
        if (board.PosterId[deleteId] != SocketId && user.Authority != GameConstants.AuthorityManager)
        {
            SendMarketBbsFail(MarketBbsDelete, buySell, 1);
            return;
        }

        board.Delete(deleteId);

        // C++ quirk kept as-is: the follow-up report request writes buySell as a
        // SHORT while the reader consumes a byte + short, shifting the page.
        var report = new byte[4];
        var reportWriter = new PacketWriter(report);
        reportWriter.SetShort(buySell);
        reportWriter.SetShort(0);
        MarketBbsReportPage(report, MarketBbsDelete);
    }

    /// <summary>CUser::MarketBBSReport — one 23-row page of posts.</summary>
    public void MarketBbsReportPage(ReadOnlySpan<byte> body, byte type)
    {
        var reader = new PacketReader(body);
        byte buySell = reader.GetByte();
        short pageIndex = reader.GetShort();

        int startCounter = pageIndex * MaxBbsPage;

        if (type == MarketBbsOpen)
        {
            startCounter = 0;
            pageIndex = 0;
        }

        if (startCounter < 0 || startCounter > MarketBbsBoard.MaxPosts)
        {
            SendMarketBbsFail(MarketBbsReport, buySell, 1);
            return;
        }

        var buffer = new byte[10240];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_MARKET_BBS);
        writer.SetByte(type);
        writer.SetByte(buySell);
        writer.SetByte(1);

        short bbsCounter = 0;
        short validCounter = 0;

        if (buySell is MarketBbsBuy or MarketBbsSell)
        {
            MarketBbsBoard board = buySell == MarketBbsBuy ? world.MarketBuy : world.MarketSell;

            for (int i = 0; i < MarketBbsBoard.MaxPosts; i++)
            {
                if (board.PosterId[i] == -1)
                    continue;

                GameUser? poster = board.PosterId[i] >= 0 && board.PosterId[i] < world.Users.Length
                    ? world.Users[board.PosterId[i]]
                    : null;
                if (poster?.UserData is not { } posterData)
                {
                    board.Delete(i);
                    continue;
                }

                ++bbsCounter;

                if (i < startCounter)
                    continue;

                if (validCounter >= MaxBbsPage)
                    continue;

                writer.SetShort(board.PosterId[i]);
                writer.SetString2(Encoding.Latin1.GetBytes(posterData.CharId));

                string title = board.Title[i];
                if (title.Length > MaxBbsTitle)
                    title = title[..MaxBbsTitle];
                writer.SetString2(Encoding.Latin1.GetBytes(title));

                string message = board.Message[i];
                if (message.Length > MaxBbsMessage)
                    message = message[..MaxBbsMessage];
                writer.SetString2(Encoding.Latin1.GetBytes(message));

                writer.SetDWord((uint)board.Price[i]);
                writer.SetShort(i);

                ++validCounter;
            }
        }

        if (validCounter == 0 && pageIndex > 0)
        {
            // C++ fail_return1: the retry request also writes buySell as a
            // SHORT (misaligned page read) — kept verbatim.
            var retry = new byte[4];
            var retryWriter = new PacketWriter(retry);
            retryWriter.SetShort(buySell);
            retryWriter.SetShort(pageIndex - 1);
            MarketBbsReportPage(retry, type);
            return;
        }

        for (int j = validCounter; j < MaxBbsPage; j++)
        {
            writer.SetShort(-1);
            writer.SetShort(0);
            writer.SetShort(0);
            writer.SetShort(0);
            writer.SetDWord(0);
            writer.SetShort(-1);
        }

        writer.SetShort(pageIndex);
        writer.SetShort(bbsCounter);
        Send(writer.Written);
    }

    /// <summary>CUser::MarketBBSRemotePurchase — 5000 gold remote-barter fee.</summary>
    public void MarketBbsRemotePurchasePost(ReadOnlySpan<byte> body)
    {
        if (UserData is not { } user)
            return;

        var reader = new PacketReader(body);
        byte buySell = reader.GetByte();
        short messageIndex = reader.GetShort();

        byte result = 0;
        byte subResult = 1;

        if (buySell is MarketBbsBuy or MarketBbsSell
            && messageIndex >= 0 && messageIndex < MarketBbsBoard.MaxPosts)
        {
            MarketBbsBoard board = buySell == MarketBbsBuy ? world.MarketBuy : world.MarketSell;

            if (board.PosterId[messageIndex] == -1)
            {
                subResult = 3;
            }
            else
            {
                GameUser? poster = world.Users[board.PosterId[messageIndex]];
                if (poster is null)
                {
                    subResult = 1;
                }
                else if (user.Gold >= RemotePurchasePrice)
                {
                    user.Gold -= RemotePurchasePrice;

                    var goldBuffer = new byte[12];
                    var goldWriter = new PacketWriter(goldBuffer);
                    goldWriter.SetByte((byte)GameOpcode.WIZ_GOLD_CHANGE);
                    goldWriter.SetByte(2); // GOLD_CHANGE_LOSE
                    goldWriter.SetDWord(RemotePurchasePrice);
                    goldWriter.SetDWord((uint)user.Gold);
                    Send(goldWriter.Written);

                    result = 1;
                }
                else
                {
                    subResult = 2;
                }
            }
        }

        var buffer = new byte[8];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_MARKET_BBS);
        writer.SetByte(MarketBbsRemotePurchase);
        writer.SetByte(buySell);
        writer.SetByte(result);
        if (result == 0)
            writer.SetByte(subResult);
        Send(writer.Written);
    }

    /// <summary>CUser::MarketBBSMessage — the full message text of one post.</summary>
    public void MarketBbsMessagePost(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        byte buySell = reader.GetByte();
        short messageIndex = reader.GetShort();

        if (buySell is not MarketBbsBuy and not MarketBbsSell
            || messageIndex < 0 || messageIndex >= MarketBbsBoard.MaxPosts)
        {
            SendMarketBbsMessageFail();
            return;
        }

        MarketBbsBoard board = buySell == MarketBbsBuy ? world.MarketBuy : world.MarketSell;
        if (board.PosterId[messageIndex] == -1)
        {
            SendMarketBbsMessageFail();
            return;
        }

        string message = board.Message[messageIndex];
        if (message.Length > MaxBbsMessage)
            message = message[..MaxBbsMessage];

        byte[] text = Encoding.Latin1.GetBytes(message);
        var buffer = new byte[8 + text.Length];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_MARKET_BBS);
        writer.SetByte(MarketBbsMessage);
        writer.SetByte(0); // result stays 0 in the C++ success path (quirk)
        writer.SetString2(text);
        Send(writer.Written);
    }

    private void SendMarketBbsMessageFail()
    {
        var buffer = new byte[8];
        var writer = new PacketWriter(buffer);
        writer.SetByte((byte)GameOpcode.WIZ_MARKET_BBS);
        writer.SetByte(MarketBbsMessage);
        writer.SetByte(0);
        writer.SetByte(1);
        Send(writer.Written);
    }

    /// <summary>CUser::MarketBBSUserDelete — drop this user's posts on logout.</summary>
    public void MarketBbsUserDelete()
    {
        for (int i = 0; i < MarketBbsBoard.MaxPosts; i++)
        {
            if (world.MarketBuy.PosterId[i] == SocketId)
                world.MarketBuy.Delete(i);

            if (world.MarketSell.PosterId[i] == SocketId)
                world.MarketSell.Delete(i);
        }
    }
}

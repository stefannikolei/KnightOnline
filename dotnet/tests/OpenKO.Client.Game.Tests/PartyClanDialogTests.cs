using System.Buffers.Binary;
using System.Text;
using OpenKO.Client.Assets;
using OpenKO.Client.Engine.Ui;
using OpenKO.Client.Game.Net;
using OpenKO.Client.Game.States;
using OpenKO.Client.Game.Ui;
using OpenKO.Client.Game.World;
using OpenKO.Core.Protocol;
using Xunit;

namespace OpenKO.Client.Game.Tests;

/// <summary>
/// Stage-9.7 pins: the party window, the clan browse/create/join window, the clan-name popup
/// and the character status sheet drive real WIZ_PARTY / WIZ_KNIGHTS_PROCESS / WIZ_POINT_CHANGE
/// packets and parse the party/clan broadcasts — headless over synthetic .uif trees.
/// </summary>
public class PartyClanDialogTests
{
    private sealed class FakeGameClient : IGameClient
    {
        public List<byte[]> Sent { get; } = [];

        public void Send(ReadOnlySpan<byte> payload) => Sent.Add(payload.ToArray());

        public void Connect(string host, int port) { }

        public bool CryptionEnabled => true;

        public void EnableCryption(ulong publicKey) { }

        public byte[] Last => Sent[^1];
    }

    // ---- synthetic-tree helpers (mirroring HudDialogTests) -------------------

    private static N3UiRect Rect(int l, int t, int r, int b) => new() { Left = l, Top = t, Right = r, Bottom = b };

    private static UiControl Group(string id, params UiControl[] children)
    {
        var g = new UiControl(new N3UiBase { Id = id, Region = Rect(0, 0, 400, 400) });
        foreach (UiControl c in children)
            g.AddChild(c);
        return g;
    }

    private static UiButton Button(string id) => new(new N3UiButton
    {
        Id = id,
        Style = UiStyle.BtnNormal,
        Region = Rect(0, 0, 50, 20),
        ClickRect = Rect(0, 0, 50, 20),
    });

    private static UiEditControl Edit(string id) => new(new N3UiEdit { Id = id, Region = Rect(0, 0, 100, 20) });

    private static UiStringControl Str(string id) => new(new N3UiString { Id = id, Region = Rect(0, 0, 100, 16) });

    private static UiControl Area(string id) => new(new N3UiBase { Id = id, Region = Rect(0, 0, 100, 16) });

    private static UiListControl List(string id) => new(new N3UiList { Id = id, Region = Rect(0, 0, 200, 160), FontHeight = 16 });

    private static void Click(UiControl c) => c.Parent!.ReceiveMessage(c, UiMsg.ButtonClick);

    // ---- packet builders (little-endian, matching the C++ MsgRecv layout) ---

    private sealed class Pkt
    {
        private readonly List<byte> _b = [];

        public Pkt Byte(int v) { _b.Add((byte)v); return this; }

        public Pkt Short(int v) { Span<byte> s = stackalloc byte[2]; BinaryPrimitives.WriteInt16LittleEndian(s, (short)v); _b.AddRange(s.ToArray()); return this; }

        public Pkt DWord(uint v) { Span<byte> s = stackalloc byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(s, v); _b.AddRange(s.ToArray()); return this; }

        public Pkt Str2(string v) { Short(v.Length); _b.AddRange(Encoding.Latin1.GetBytes(v)); return this; }

        public byte[] Done() => _b.ToArray();
    }

    // ======================================================================
    // Party window
    // ======================================================================

    private static (PartyOrForceDialog Dialog, UiControl Root) BuildParty(GameContext context)
    {
        var children = new List<UiControl>();
        for (int i = 0; i < PartyOrForceDialog.MaxMembers; i++)
        {
            children.Add(Str($"static_name_{i}"));
            children.Add(new UiControl(new N3UiProgress { Id = $"progress_hp_{i}", Region = Rect(0, 0, 100, 12) }));
            children.Add(Area($"Area_{i}"));
        }

        UiControl root = Group("PARTY", [.. children]);
        var dialog = new PartyOrForceDialog(context, root);
        return (dialog, root);
    }

    [Fact]
    public void Party_InsertBroadcastPopulatesSlotAndHpPercent()
    {
        var context = new GameContext(new FakeGameClient());
        (PartyOrForceDialog dialog, _) = BuildParty(context);

        // N3_SP_PARTY_OR_FORCE_INSERT: id, position, name, hpMax, hp, level, class, mpMax, mp, nation
        byte[] insert = new Pkt()
            .Byte((byte)GameOpcode.WIZ_PARTY).Byte(PartyProtocol.Insert)
            .Short(7).Byte(0).Str2("Buddy").Short(200).Short(150).Byte(42).Short(5).Short(80).Short(40).Byte(1)
            .Done();

        dialog.OnParty(PartyProtocol.Insert, insert);

        Assert.Equal(1, dialog.MemberCount);
        Assert.Equal("Buddy", dialog.NameAt(0));
        Assert.Equal(75, dialog.HpPercentAt(0)); // 150/200
        Assert.True(dialog.Root.Visible);        // window shows once a member exists
    }

    [Fact]
    public void Party_HpChangeThenRemoveUpdatesRoster()
    {
        var context = new GameContext(new FakeGameClient());
        (PartyOrForceDialog dialog, _) = BuildParty(context);

        byte[] insert = new Pkt()
            .Byte((byte)GameOpcode.WIZ_PARTY).Byte(PartyProtocol.Insert)
            .Short(7).Byte(0).Str2("Buddy").Short(200).Short(200).Byte(42).Short(5).Short(80).Short(80).Byte(1)
            .Done();
        dialog.OnParty(PartyProtocol.Insert, insert);

        byte[] hp = new Pkt()
            .Byte((byte)GameOpcode.WIZ_PARTY).Byte(PartyProtocol.HpChange)
            .Short(7).Short(200).Short(50).Short(80).Short(80).Done();
        dialog.OnParty(PartyProtocol.HpChange, hp);
        Assert.Equal(25, dialog.HpPercentAt(0)); // 50/200

        byte[] remove = new Pkt().Byte((byte)GameOpcode.WIZ_PARTY).Byte(PartyProtocol.Remove).Short(7).Done();
        dialog.OnParty(PartyProtocol.Remove, remove);
        Assert.Equal(0, dialog.MemberCount);
        Assert.False(dialog.Root.Visible);
    }

    [Fact]
    public void Party_LeaderKicksMemberAndDisbands()
    {
        var client = new FakeGameClient();
        var context = new GameContext(client);
        (PartyOrForceDialog dialog, _) = BuildParty(context);
        context.InGame.World.Local.SocketId = 100;
        dialog.Bind(context.InGame);

        // Leader (id 100, index 0) then a member (id 7).
        void Add(short id, string name) => dialog.OnParty(PartyProtocol.Insert, new Pkt()
            .Byte((byte)GameOpcode.WIZ_PARTY).Byte(PartyProtocol.Insert)
            .Short(id).Byte(0).Str2(name).Short(100).Short(100).Byte(1).Short(1).Short(50).Short(50).Byte(1).Done());
        Add(100, "Me");
        Add(7, "Buddy");

        // Kick the member (target = 7) → REMOVE 7.
        byte[] kick = dialog.BuildLeavePacket(7)!;
        Assert.Equal([(byte)GameOpcode.WIZ_PARTY, PartyProtocol.Remove, 7, 0], kick);

        // No member targeted → leader disbands (DESTROY).
        byte[] disband = dialog.BuildLeavePacket(-1)!;
        Assert.Equal([(byte)GameOpcode.WIZ_PARTY, PartyProtocol.Delete], disband);
    }

    // ======================================================================
    // Knights operation window
    // ======================================================================

    private static (KnightsOperationDialog Dialog, UiControl Root, UiListControl List, MessageBoxDialog Box)
        BuildKnightsOp(GameContext context)
    {
        UiControl boxRoot = Group("BOX", Button("Btn_Yes"), Button("Btn_No"), Str("Text_Message"), Str("Text_Title"));
        var box = new MessageBoxDialog(boxRoot);

        UiListControl list = List("List_Knights");
        UiControl root = Group("KNIGHTSOP",
            Button("btn_up"), Button("btn_down"), Button("btn_close"),
            Button("Btn_Join"), Button("Btn_Create"), Button("Btn_Destroy"), Button("Btn_Withdraw"),
            Edit("Edit_KnightsName"), list);
        var dialog = new KnightsOperationDialog(context, root, box);
        return (dialog, root, list, box);
    }

    private static byte[] ClanListPacket(params (short Id, string Name, string Chief, short Members, uint Point)[] rows)
    {
        var p = new Pkt().Byte((byte)GameOpcode.WIZ_KNIGHTS_PROCESS).Byte(KnightsProtocol.AllListReq)
            .Short(0).Short(rows.Length);
        foreach ((short id, string name, string chief, short members, uint point) in rows)
            p.Short(id).Str2(name).Short(members).Str2(chief).DWord(point);
        return p.Done();
    }

    [Fact]
    public void KnightsOp_CreateOpensPopup_JoinSendsSelectedClanId()
    {
        var client = new FakeGameClient();
        var context = new GameContext(client);
        (KnightsOperationDialog dialog, UiControl root, UiListControl list, _) = BuildKnightsOp(context);

        bool createRaised = false;
        dialog.CreateRequested += () => createRaised = true;
        Click(root.GetChildById<UiButton>("Btn_Create")!);
        Assert.True(createRaised);

        // AllListReq broadcast populates the list.
        dialog.OnKnights(KnightsProtocol.AllListReq, ClanListPacket(
            (10, "Templars", "Arthur", 5, 1000),
            (22, "Reapers", "Grim", 8, 4200)));
        Assert.Equal(2, list.Count);

        list.SetCurSel(1); // Reapers (id 22)
        byte[]? join = dialog.Join();
        Assert.Equal((byte)GameOpcode.WIZ_KNIGHTS_PROCESS, join![0]);
        Assert.Equal(KnightsProtocol.Join, join[1]);
        Assert.Equal((short)22, BinaryPrimitives.ReadInt16LittleEndian(join.AsSpan(2)));
        Assert.Equal(join, client.Last);
    }

    [Fact]
    public void KnightsOp_WithdrawAndDestroyConfirmThenSend()
    {
        var client = new FakeGameClient();
        var context = new GameContext(client);
        (KnightsOperationDialog dialog, UiControl root, _, MessageBoxDialog box) = BuildKnightsOp(context);

        Click(root.GetChildById<UiButton>("Btn_Withdraw")!);
        Assert.True(box.IsOpen);
        Click(box.Root.GetChildById<UiButton>("Btn_Yes")!); // confirm
        Assert.Equal([(byte)GameOpcode.WIZ_KNIGHTS_PROCESS, KnightsProtocol.Withdraw], client.Last);

        Click(root.GetChildById<UiButton>("Btn_Destroy")!);
        Assert.True(box.IsOpen);
        Click(box.Root.GetChildById<UiButton>("Btn_Yes")!);
        Assert.Equal([(byte)GameOpcode.WIZ_KNIGHTS_PROCESS, KnightsProtocol.Destroy], client.Last);
    }

    [Fact]
    public void KnightsOp_PageButtonsRequestList()
    {
        var client = new FakeGameClient();
        var context = new GameContext(client);
        (KnightsOperationDialog dialog, UiControl root, _, _) = BuildKnightsOp(context);

        Click(root.GetChildById<UiButton>("btn_down")!); // page 0 -> 1
        Assert.Equal((byte)GameOpcode.WIZ_KNIGHTS_PROCESS, client.Last[0]);
        Assert.Equal(KnightsProtocol.AllListReq, client.Last[1]);
        Assert.Equal((short)1, BinaryPrimitives.ReadInt16LittleEndian(client.Last.AsSpan(2)));
    }

    // ======================================================================
    // Create-clan popup
    // ======================================================================

    [Fact]
    public void CreateClan_OkConfirmsThenSendsBuildCreate()
    {
        var client = new FakeGameClient();
        var context = new GameContext(client);
        UiControl root = Group("CREATECLAN", Edit("Edit_Clan"), Str("Text_Message"), Button("btn_yes"), Button("btn_no"));
        var dialog = new CreateClanDialog(context, root);

        dialog.Open();
        root.GetChildById<UiEditControl>("Edit_Clan")!.Text = "Templars";

        string? confirmed = null;
        dialog.ConfirmRequested += n => confirmed = n;
        Click(root.GetChildById<UiButton>("btn_yes")!);
        Assert.Equal("Templars", confirmed); // OK asks for confirmation first
        Assert.Empty(client.Sent);

        byte[]? sent = dialog.Send();
        var r = new OpenKO.Network.PacketReader(sent!);
        Assert.Equal((byte)GameOpcode.WIZ_KNIGHTS_PROCESS, r.GetByte());
        Assert.Equal(KnightsProtocol.Create, r.GetByte());
        Assert.Equal("Templars", Encoding.Latin1.GetString(r.GetVarString(2)));
        Assert.False(root.Visible); // closed on send
    }

    [Fact]
    public void CreateClan_EmptyNameDoesNotConfirm()
    {
        var context = new GameContext(new FakeGameClient());
        UiControl root = Group("CREATECLAN", Edit("Edit_Clan"), Str("Text_Message"), Button("btn_yes"), Button("btn_no"));
        var dialog = new CreateClanDialog(context, root);
        dialog.Open();

        bool raised = false;
        dialog.ConfirmRequested += _ => raised = true;
        Click(root.GetChildById<UiButton>("btn_yes")!);
        Assert.False(raised);
    }

    // ======================================================================
    // Character status sheet (Various)
    // ======================================================================

    private static (VariousDialog Dialog, UiControl Root) BuildVarious(GameContext context)
    {
        UiControl root = Group("VARIOUS",
            Str("Text_ID"), Str("Text_Class"), Str("Text_Race"), Str("Text_Nation"),
            Str("Text_Level"), Str("Text_Exp"), Str("Text_HP"), Str("Text_MP"),
            Str("Text_AP"), Str("Text_GP"), Str("Text_Weight"), Str("Text_BonusPoint"), Str("Text_RealmPoint"),
            Str("Text_Strength"), Str("Text_Stamina"), Str("Text_Dexterity"), Str("Text_Intelligence"), Str("Text_MagicAttack"),
            Str("Text_RegistFire"), Str("Text_RegistIce"), Str("Text_RegistLightR"),
            Str("Text_RegistMagic"), Str("Text_RegistCurse"), Str("Text_RegistPoison"),
            Button("Btn_Strength"), Button("Btn_Stamina"), Button("Btn_Dexterity"),
            Button("Btn_Intelligence"), Button("Btn_MagicAttack"),
            Str("Text_ClansName"), Str("Text_clan_MemberCount"),
            List("List_clan_ChrID"), List("List_clan_Grade"), List("List_clan_Level"), List("List_clan_Job"),
            Button("btn_clan_admit"), Button("btn_clan_Appoint"), Button("btn_clan_Remove"));
        var dialog = new VariousDialog(context, root);
        return (dialog, root);
    }

    [Fact]
    public void Various_FillStateBindsTextsFromLocalPlayer()
    {
        var context = new GameContext(new FakeGameClient());
        (VariousDialog dialog, UiControl root) = BuildVarious(context);

        var p = new LocalPlayer
        {
            Name = "Hero", Class = 105, Race = 1, Nation = 2, Level = 50,
            Exp = 1234, MaxExp = 5000, Hp = 800, MaxHp = 1000, Mp = 200, MaxMp = 400,
            TotalHit = 350, TotalAc = 220, CurWeight = 1500, MaxWeight = 3000,
            Str = 90, ItemStr = 12, Sta = 80, Dex = 70, Intel = 60, Cha = 50,
            FireResist = 15, ColdResist = 20, LightningResist = 25, MagicResist = 30,
            DiseaseResist = 35, PoisonResist = 40, Points = 3, Loyalty = 999, LoyaltyMonthly = 111,
        };

        dialog.FillState(p);

        Assert.Equal("Hero", root.GetChildById<UiStringControl>("Text_ID")!.Text);
        Assert.Equal("50", root.GetChildById<UiStringControl>("Text_Level")!.Text);
        Assert.Equal("1234 / 5000", root.GetChildById<UiStringControl>("Text_Exp")!.Text);
        Assert.Equal("800 / 1000", root.GetChildById<UiStringControl>("Text_HP")!.Text);
        Assert.Equal("200 / 400", root.GetChildById<UiStringControl>("Text_MP")!.Text);
        Assert.Equal("350", root.GetChildById<UiStringControl>("Text_AP")!.Text);
        Assert.Equal("220", root.GetChildById<UiStringControl>("Text_GP")!.Text);
        Assert.Equal("150.0/300.0", root.GetChildById<UiStringControl>("Text_Weight")!.Text);
        Assert.Equal("90(+12)", root.GetChildById<UiStringControl>("Text_Strength")!.Text); // base(+item)
        Assert.Equal("80", root.GetChildById<UiStringControl>("Text_Stamina")!.Text);        // no item bonus
        Assert.Equal("15", root.GetChildById<UiStringControl>("Text_RegistFire")!.Text);
        Assert.Equal("40", root.GetChildById<UiStringControl>("Text_RegistPoison")!.Text);
        Assert.Equal("3", root.GetChildById<UiStringControl>("Text_BonusPoint")!.Text);
        Assert.Equal("999 / 111", root.GetChildById<UiStringControl>("Text_RealmPoint")!.Text);

        // Buttons visible because a bonus point remains.
        Assert.True(root.GetChildById<UiButton>("Btn_Strength")!.Visible);
    }

    [Fact]
    public void Various_StatUpSendsPointChange_OnlyWhenPointsRemain()
    {
        var client = new FakeGameClient();
        var context = new GameContext(client);
        (VariousDialog dialog, UiControl root) = BuildVarious(context);

        // No points → stat button is hidden and clicking sends nothing.
        dialog.FillState(new LocalPlayer { Points = 0, Str = 50 });
        Assert.False(root.GetChildById<UiButton>("Btn_Strength")!.Visible);
        Click(root.GetChildById<UiButton>("Btn_Strength")!);
        Assert.Empty(client.Sent);

        // With a point, Btn_Dexterity spends type 3, delta +1.
        dialog.FillState(new LocalPlayer { Points = 2, Dex = 40 });
        Click(root.GetChildById<UiButton>("Btn_Dexterity")!);
        Assert.Equal(
            [(byte)GameOpcode.WIZ_POINT_CHANGE, StatPointProtocol.Dexterity, 1, 0],
            client.Last);
    }

    [Fact]
    public void Various_MemberBroadcastFillsClanLists_AndOfficerButtons()
    {
        var client = new FakeGameClient();
        var context = new GameContext(client);
        (VariousDialog dialog, UiControl root) = BuildVarious(context);

        // MemberInfoAll: common(success), size, online, total, count, rows.
        byte[] members = new Pkt()
            .Byte((byte)GameOpcode.WIZ_KNIGHTS_PROCESS).Byte(KnightsProtocol.MemberReq)
            .Byte(1).Short(0).Short(2).Short(3).Short(2)
            .Str2("Arthur").Byte(VariousDialog.DutyChief).Byte(60).Short(105).Byte(1)  // connected
            .Str2("Lancelot").Byte(5).Byte(55).Short(101).Byte(0)                       // offline
            .Done();

        dialog.OnKnights(KnightsProtocol.MemberReq, members);

        UiListControl ids = root.GetChildById<UiListControl>("List_clan_ChrID")!;
        UiListControl grades = root.GetChildById<UiListControl>("List_clan_Grade")!;
        Assert.Equal(2, ids.Count);
        Assert.True(ids.GetString(0, out string first) && first == "Arthur");
        Assert.True(grades.GetString(0, out string g0) && g0 == "Chief");
        Assert.True(grades.GetString(1, out string g1) && g1 == "...."); // offline row
        Assert.Equal("2 / 3", root.GetChildById<UiStringControl>("Text_clan_MemberCount")!.Text);

        // Chief sees all management buttons; expel sends the selected member by name.
        dialog.SetClanDuty(VariousDialog.DutyChief);
        Assert.True(root.GetChildById<UiButton>("btn_clan_Remove")!.Visible);
        ids.SetCurSel(0);
        Click(root.GetChildById<UiButton>("btn_clan_Remove")!);
        var r = new OpenKO.Network.PacketReader(client.Last);
        Assert.Equal((byte)GameOpcode.WIZ_KNIGHTS_PROCESS, r.GetByte());
        Assert.Equal(KnightsProtocol.Remove, r.GetByte());
        Assert.Equal("Arthur", Encoding.Latin1.GetString(r.GetVarString(2)));
    }

    [Fact]
    public void Various_AdmitInvitesCurrentTarget()
    {
        var client = new FakeGameClient();
        var context = new GameContext(client);
        (VariousDialog dialog, UiControl root) = BuildVarious(context);

        dialog.SetClanDuty(VariousDialog.DutyChief);
        dialog.TargetId = 321;
        Click(root.GetChildById<UiButton>("btn_clan_admit")!);
        Assert.Equal((byte)GameOpcode.WIZ_KNIGHTS_PROCESS, client.Last[0]);
        Assert.Equal(KnightsProtocol.Join, client.Last[1]);
        Assert.Equal((short)321, BinaryPrimitives.ReadInt16LittleEndian(client.Last.AsSpan(2)));
    }

    // ======================================================================
    // Protocol builder / parser byte layouts
    // ======================================================================

    [Fact]
    public void Knights_NewBuilderByteLayouts()
    {
        Assert.Equal([(byte)GameOpcode.WIZ_KNIGHTS_PROCESS, KnightsProtocol.Destroy], KnightsProtocol.BuildDestroy());
        Assert.Equal([(byte)GameOpcode.WIZ_KNIGHTS_PROCESS, KnightsProtocol.MemberReq], KnightsProtocol.BuildMemberInfoAll());

        var expel = new OpenKO.Network.PacketReader(KnightsProtocol.BuildExpel("Bob"));
        expel.GetByte();
        Assert.Equal(KnightsProtocol.Remove, expel.GetByte());
        Assert.Equal("Bob", Encoding.Latin1.GetString(expel.GetVarString(2)));

        var appoint = new OpenKO.Network.PacketReader(KnightsProtocol.BuildAppointViceChief("Kay"));
        appoint.GetByte();
        Assert.Equal(KnightsProtocol.ViceChief, appoint.GetByte());
        Assert.Equal("Kay", Encoding.Latin1.GetString(appoint.GetVarString(2)));
    }

    [Fact]
    public void StatPoint_BuildLayout()
    {
        Assert.Equal([(byte)GameOpcode.WIZ_POINT_CHANGE, 0x01, 1, 0], StatPointProtocol.Build(0x01, 1));
    }

    [Fact]
    public void Knights_ParseClanListMatchesCppLayout()
    {
        byte[] payload = ClanListPacket((10, "Templars", "Arthur", 5, 1000), (22, "Reapers", "Grim", 8, 4200));
        KnightsProtocol.ClanList list = KnightsProtocol.ParseClanList(payload);

        Assert.Equal(2, list.Rows.Count);
        Assert.Equal((short)10, list.Rows[0].Id);
        Assert.Equal("Templars", list.Rows[0].Name);
        Assert.Equal("Arthur", list.Rows[0].ChiefName);
        Assert.Equal(5, list.Rows[0].MemberCount);
        Assert.Equal(1000u, list.Rows[0].Point);
        Assert.Equal(4200u, list.Rows[1].Point);
    }

    [Fact]
    public void Knights_ParseMemberListMatchesCppLayout()
    {
        byte[] payload = new Pkt()
            .Byte((byte)GameOpcode.WIZ_KNIGHTS_PROCESS).Byte(KnightsProtocol.MemberReq)
            .Byte(1).Short(0).Short(4).Short(9).Short(1)
            .Str2("Merlin").Byte(2).Byte(70).Short(107).Byte(1)
            .Done();

        KnightsProtocol.ClanMemberList members = KnightsProtocol.ParseMemberList(payload);
        Assert.Equal((short)4, members.Online);
        Assert.Equal((short)9, members.Total);
        KnightsProtocol.ClanMemberRow row = Assert.Single(members.Members);
        Assert.Equal("Merlin", row.Name);
        Assert.Equal(2, row.Duty);
        Assert.Equal(70, row.Level);
        Assert.Equal((short)107, row.Class);
        Assert.True(row.Connected);
    }

    [Fact]
    public void Party_ParseInsertRejectsNegativeIdErrorCode()
    {
        byte[] err = new Pkt().Byte((byte)GameOpcode.WIZ_PARTY).Byte(PartyProtocol.Insert).Short(-1).Done();
        Assert.Null(PartyProtocol.ParseInsert(err));
    }
}

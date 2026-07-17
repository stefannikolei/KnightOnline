using OpenKO.Client.Engine.Ui;
using OpenKO.Client.Game.Net;
using OpenKO.Client.Game.States;

namespace OpenKO.Client.Game.Ui;

/// <summary>
/// Controller for the party / force member window — port of <c>CUIPartyOrForce</c>
/// (Client/WarFare/UIPartyOrForce.cpp). Displays up to <see cref="MaxMembers"/> member
/// slots (name label <c>static_name_{i}</c> + HP gauge <c>progress_hp_{i}</c>), fed from
/// the WIZ_PARTY broadcasts routed through <see cref="InGameState.PartyReceived"/>.
///
/// Invite/create is driven from the command bar's target action (not this window); this
/// controller only displays the roster and offers a leave/disband action
/// (<see cref="Leave"/>, CGameProcMain::MsgSend_PartyOrForceLeave). Clicking a member slot
/// selects that member as the current target (<see cref="MemberSelected"/>). There is no
/// runtime progress widget yet, so the HP percent is recorded per slot like the state bar.
///
/// The mp/status blink and force-vs-party gauge variants are cosmetic and deferred; the HP
/// gauge and name roster are the load-bearing behaviour.
/// </summary>
public sealed class PartyOrForceDialog
{
    /// <summary>MAX_PARTY_OR_FORCE.</summary>
    public const int MaxMembers = 8;

    private sealed class Member
    {
        public short Id;
        public string Name = string.Empty;
        public byte Level;
        public short Class;
        public short Hp;
        public short MaxHp;
        public short Mp;
        public short MaxMp;
    }

    private readonly GameContext _context;
    private readonly UiControl _root;
    private readonly UiStringControl?[] _names = new UiStringControl?[MaxMembers];
    private readonly UiControl?[] _hpBars = new UiControl?[MaxMembers];
    private readonly UiControl?[] _areas = new UiControl?[MaxMembers];
    private readonly Dictionary<UiControl, int> _hpPercent = new();
    private readonly List<Member> _members = [];

    private short _localId;

    public PartyOrForceDialog(GameContext context, UiControl root)
    {
        _context = context;
        _root = root;

        for (int i = 0; i < MaxMembers; i++)
        {
            _names[i] = root.GetChildById<UiStringControl>($"static_name_{i}");
            _hpBars[i] = root.GetChildById($"progress_hp_{i}");
            _areas[i] = root.GetChildById($"Area_{i}");
            _names[i]?.SetVisible(false);
        }

        root.Message += OnMessage;
        root.SetVisible(false); // hidden until a party forms
    }

    public UiControl Root => _root;

    /// <summary>Current member count (CUIPartyOrForce::MemberCount).</summary>
    public int MemberCount => _members.Count;

    /// <summary>Raised when a member slot is clicked, with that member's socket id (target select).</summary>
    public event Action<short>? MemberSelected;

    /// <summary>Member ids in roster order (index 0 = leader).</summary>
    public IReadOnlyList<short> MemberIds => _members.Select(m => m.Id).ToArray();

    /// <summary>The recorded HP fill percentage for a member slot (0..100), or 0 when empty.</summary>
    public int HpPercentAt(int slot) =>
        slot >= 0 && slot < MaxMembers && _hpBars[slot] is { } bar && _hpPercent.TryGetValue(bar, out int v) ? v : 0;

    /// <summary>The member name shown in a slot, or empty.</summary>
    public string NameAt(int slot) =>
        slot >= 0 && slot < _members.Count ? _members[slot].Name : string.Empty;

    /// <summary>Wire the party broadcasts and remember the local socket id (self-remove disbands).</summary>
    public void Bind(InGameState inGame)
    {
        _localId = inGame.World.Local.SocketId;
        inGame.PartyReceived += OnParty;
    }

    /// <summary>CGameProcMain::MsgRecv_PartyOrForce — route a party broadcast into the roster.</summary>
    public void OnParty(byte sub, byte[] payload)
    {
        switch (sub)
        {
            case PartyProtocol.Insert:
                if (PartyProtocol.ParseInsert(payload) is { } m)
                    AddOrUpdate(m);
                break;

            case PartyProtocol.Remove:
            {
                short id = PartyProtocol.ParseId(payload);
                if (id == _localId)
                    _members.Clear();
                else
                    _members.RemoveAll(x => x.Id == id);
                break;
            }

            case PartyProtocol.Delete: // N3_SP_PARTY_OR_FORCE_DESTROY
                _members.Clear();
                break;

            case PartyProtocol.HpChange:
            {
                PartyProtocol.PartyHpUpdate u = PartyProtocol.ParseHpChange(payload);
                if (Find(u.Id) is { } hm)
                {
                    hm.Hp = u.Hp;
                    hm.MaxHp = u.MaxHp;
                    hm.Mp = u.Mp;
                    hm.MaxMp = u.MaxMp;
                }

                break;
            }

            case PartyProtocol.LevelChange:
            {
                (short id, byte level) = PartyProtocol.ParseLevelChange(payload);
                if (Find(id) is { } lm)
                    lm.Level = level;
                break;
            }

            case PartyProtocol.ClassChange:
            {
                (short id, short cls) = PartyProtocol.ParseClassChange(payload);
                if (Find(id) is { } cm)
                    cm.Class = cls;
                break;
            }

            default:
                return; // permit/create handled elsewhere (message box / cmd bar)
        }

        Refresh();
    }

    /// <summary>
    /// CGameProcMain::MsgSend_PartyOrForceLeave. If I lead and the target is a non-leader member,
    /// kick that member; if I lead otherwise, disband; if I am a non-leader member, leave myself.
    /// Returns the packet sent (null when there is no party).
    /// </summary>
    public byte[]? Leave(short targetId = -1)
    {
        byte[]? packet = BuildLeavePacket(targetId);
        if (packet != null)
            _context.Client.Send(packet);
        return packet;
    }

    /// <summary>The leave/kick/disband packet for the current roster, or null when no party exists.</summary>
    public byte[]? BuildLeavePacket(short targetId)
    {
        if (_members.Count == 0)
            return null;

        bool iAmLeader = _members[0].Id == _localId;
        if (iAmLeader)
        {
            int targetIndex = _members.FindIndex(m => m.Id == targetId);
            return targetIndex > 0
                ? PartyProtocol.BuildRemove(targetId) // kick a member
                : PartyProtocol.BuildLeave();          // disband
        }

        if (_members.Any(m => m.Id == _localId))
            return PartyProtocol.BuildRemove(_localId); // leave as member

        return PartyProtocol.BuildLeave();
    }

    private void AddOrUpdate(PartyProtocol.PartyMemberInfo info)
    {
        Member m = Find(info.Id) ?? Append(info.Id);
        m.Name = info.Name;
        m.Level = info.Level;
        m.Class = info.Class;
        m.Hp = info.Hp;
        m.MaxHp = info.MaxHp;
        m.Mp = info.Mp;
        m.MaxMp = info.MaxMp;
    }

    private Member Append(short id)
    {
        var m = new Member { Id = id };
        _members.Add(m);
        return m;
    }

    private Member? Find(short id) => _members.FirstOrDefault(m => m.Id == id);

    /// <summary>CUIPartyOrForce::MemberInfoReInit — repaint each slot; hide the window when empty.</summary>
    private void Refresh()
    {
        for (int i = 0; i < MaxMembers; i++)
        {
            if (i < _members.Count)
            {
                Member m = _members[i];
                if (_names[i] is { } name)
                {
                    name.Text = m.Name;
                    name.SetVisible(true);
                }

                if (_hpBars[i] is { } bar && m.MaxHp > 0)
                    _hpPercent[bar] = Math.Clamp(m.Hp * 100 / m.MaxHp, 0, 100);
            }
            else
            {
                _names[i]?.SetVisible(false);
            }
        }

        _root.SetVisible(_members.Count > 0);
    }

    private void OnMessage(UiControl sender, uint msg)
    {
        if (msg != UiMsg.ButtonClick)
            return;

        for (int i = 0; i < MaxMembers && i < _members.Count; i++)
        {
            if (ReferenceEquals(sender, _areas[i]))
            {
                MemberSelected?.Invoke(_members[i].Id);
                return;
            }
        }
    }
}

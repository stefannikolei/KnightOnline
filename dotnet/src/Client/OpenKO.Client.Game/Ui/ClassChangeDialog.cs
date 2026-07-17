using OpenKO.Client.Engine.Ui;
using OpenKO.Client.Game.Net;
using OpenKO.Client.Game.States;
using OpenKO.Client.Game.World;

namespace OpenKO.Client.Game.Ui;

/// <summary>
/// Controller for the class-change (promotion) dialog — port of <c>CUIClassChange</c>
/// (Client/WarFare/UIClassChange.cpp). Driven entirely by the server's WIZ_CLASS_CHANGE reply:
/// <see cref="Open"/> is wired to <see cref="InGameState.ClassChangeResult"/> and shows the
/// appropriate buttons/message for the result sub-opcode. On <c>SUCCESS</c> it previews the
/// promoted class and offers <c>Btn_Class</c>; clicking it optimistically promotes the local
/// player, sends the WIZ_CLASS_CHANGE request, rebuilds the skill tree and raises
/// <see cref="ClassChanged"/>. Pure/headless.
/// </summary>
public sealed class ClassChangeDialog
{
    private readonly GameContext _context;
    private readonly UiControl _root;

    private readonly UiButton? _btnOk;
    private readonly UiButton? _btnCancel;
    private readonly UiButton? _btnClass;
    private readonly UiStringControl? _textWarning; // Text_Waring
    private readonly UiStringControl? _textInfo;     // Text_info
    private readonly UiStringControl? _textMessage;  // Text_Message

    private short _prevClass;

    /// <summary>
    /// Raised after a successful <c>Btn_Class</c> promotion (the local class is already updated
    /// and the request sent). SEAM: the hotkey bar slice subscribes this to flush hotkeys
    /// (CUIHotKeyDlg::ClassChangeHotkeyFlush); it is a no-op until then.
    /// </summary>
    public event Action? ClassChanged;

    public ClassChangeDialog(GameContext context, UiControl root)
    {
        _context = context;
        _root = root;

        _btnOk = root.GetChildById<UiButton>("Btn_Ok");
        _btnCancel = root.GetChildById<UiButton>("Btn_Cancel");
        _btnClass = root.GetChildById<UiButton>("Btn_Class");
        _textWarning = root.GetChildById<UiStringControl>("Text_Waring");
        _textInfo = root.GetChildById<UiStringControl>("Text_info");
        _textMessage = root.GetChildById<UiStringControl>("Text_Message");

        root.Message += OnMessage;
        root.SetVisible(false);
    }

    /// <summary>The runtime dialog root (registered with the UI manager).</summary>
    public UiControl Root => _root;

    /// <summary>The skill tree to rebuild on promotion / restore (set by the executable glue).</summary>
    public SkillTreeDialog? SkillTree { get; set; }

    public bool IsOpen => _root.Visible;

    private LocalPlayer Local => _context.InGame.World.Local;

    /// <summary>
    /// CUIClassChange::Open — show the dialog for a WIZ_CLASS_CHANGE result sub-opcode. On
    /// <c>SUCCESS</c> the promo class name previews in <c>Text_info</c> and <c>Btn_Class</c> /
    /// <c>Btn_Cancel</c> appear; <c>NOT_YET</c>/<c>ALREADY</c>/<c>ITEM_IN_SLOT</c> show a message
    /// with <c>Btn_Ok</c>; <c>FAILURE</c> restores the previous class (the C++ RestorePrevClass).
    /// </summary>
    public void Open(byte resultCode)
    {
        // FAILURE never opens the dialog in the C++ — it rolls the optimistic promotion back.
        if (resultCode == ClassChangeProtocol.ResultFailure)
        {
            RestorePrevClass();
            return;
        }

        _root.SetVisible(true);

        _btnOk?.SetVisible(false);
        _btnCancel?.SetVisible(false);
        _btnClass?.SetVisible(false);
        _textWarning?.SetVisible(false);
        _textInfo?.SetVisible(false);
        _textMessage?.SetVisible(true);

        switch (resultCode)
        {
            case ClassChangeProtocol.ResultSuccess:
                SetText(_textMessage, "You are ready to change your class.");
                _btnClass?.SetVisible(true);
                _btnCancel?.SetVisible(true);
                _textInfo?.SetVisible(true);

                _prevClass = Local.Class;
                SetText(_textInfo, ClassName(ClassChangeProtocol.Promote(Local.Class)));
                break;

            case ClassChangeProtocol.ResultNotYet:
                SetText(_textMessage, "You cannot change your class yet.");
                _btnOk?.SetVisible(true);
                break;

            case ClassChangeProtocol.ResultAlready:
                SetText(_textMessage, "You have already changed your class.");
                _btnOk?.SetVisible(true);
                break;

            case ClassChangeProtocol.ResultItemInSlot:
                SetText(_textMessage, "You must empty your equipment slots first.");
                _btnOk?.SetVisible(true);
                break;
        }
    }

    /// <summary>CUIClassChange::RestorePrevClass — a server reject rolls back the optimistic promotion.</summary>
    public void RestorePrevClass()
    {
        if (_prevClass != 0)
        {
            Local.Class = _prevClass;
            SkillTree?.Rebuild();
        }

        Close();
    }

    public void Close() => _root.SetVisible(false);

    private void OnMessage(UiControl sender, uint msg)
    {
        if (msg != UiMsg.ButtonClick || !_root.Visible)
            return;

        if (ReferenceEquals(sender, _btnOk) || ReferenceEquals(sender, _btnCancel))
        {
            Close();
            return;
        }

        if (ReferenceEquals(sender, _btnClass))
            DoClassChange();
    }

    private void DoClassChange()
    {
        LocalPlayer local = Local;
        _prevClass = local.Class;
        short promo = ClassChangeProtocol.Promote(local.Class);

        // Optimistic promotion, then request the change from the server.
        local.Class = promo;
        _context.InGame.SendClassChangeRequest(promo);

        // Rebuild the skill tree so the newly available specialization tabs light up.
        SkillTree?.Rebuild();

        // SEAM: the hotkey slice flushes hotkeys off this event.
        ClassChanged?.Invoke();

        Close();
    }

    private static void SetText(UiStringControl? s, string value)
    {
        if (s != null)
            s.Text = value;
    }

    /// <summary>
    /// Static class-name map (the C++ CGameBase::GetTextByClass, without the text-resource
    /// table). Covers every base + promotion class; unknown ids fall back to the numeric id.
    /// </summary>
    public static string ClassName(short cls) => cls switch
    {
        101 or 201 => "Warrior",
        102 or 202 => "Rogue",
        103 or 203 => "Wizard",
        104 or 204 => "Priest",
        105 => "Berserker",
        106 => "Guardian",
        107 => "Hunter",
        108 => "Penetrator",
        109 => "Sorcerer",
        110 => "Necromancer",
        111 => "Shaman",
        112 => "Dark Priest",
        205 => "Blade",
        206 => "Protector",
        207 => "Ranger",
        208 => "Assassin",
        209 => "Mage",
        210 => "Enchanter",
        211 => "Cleric",
        212 => "Druid",
        _ => cls.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };
}

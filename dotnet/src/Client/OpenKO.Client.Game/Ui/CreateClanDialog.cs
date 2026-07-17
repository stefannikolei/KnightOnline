using OpenKO.Client.Engine.Ui;
using OpenKO.Client.Game.Net;
using OpenKO.Client.Game.States;

namespace OpenKO.Client.Game.Ui;

/// <summary>
/// Controller for the clan-name entry popup — port of <c>CUICreateClanName</c>
/// (Client/WarFare/UICreateClanName.cpp). <c>Edit_Clan</c> holds the name, <c>Text_Message</c>
/// the prompt; <c>btn_yes</c> founds the clan, <c>btn_no</c> cancels.
///
/// The original chains a cost-confirmation message box before sending
/// (<c>MakeClan</c> → MB_YESNO → <c>MsgSend_MakeClan</c>); that confirmation is owned by the
/// caller here — <see cref="ConfirmRequested"/> fires with the entered name and the caller
/// (executable) posts the message box, then calls <see cref="Send"/> on accept. A non-empty
/// name is clamped to 20 chars like the C++.
/// </summary>
public sealed class CreateClanDialog
{
    private readonly GameContext _context;
    private readonly UiControl _root;
    private readonly UiEditControl? _editClan;
    private readonly UiStringControl? _message;

    public CreateClanDialog(GameContext context, UiControl root)
    {
        _context = context;
        _root = root;
        _editClan = root.GetChildById<UiEditControl>("Edit_Clan");
        _message = root.GetChildById<UiStringControl>("Text_Message");
        root.Message += OnMessage;
        root.SetVisible(false);
    }

    public UiControl Root => _root;

    /// <summary>The entered clan name, trimmed to the wire limit (20 chars).</summary>
    public string ClanName
    {
        get
        {
            string name = _editClan?.Text ?? string.Empty;
            return name.Length > 20 ? name[..20] : name;
        }
    }

    /// <summary>
    /// Raised on OK with the entered name so the caller can post the cost-confirmation box.
    /// The caller calls <see cref="Send"/> when the user accepts.
    /// </summary>
    public event Action<string>? ConfirmRequested;

    /// <summary>CUICreateClanName::Open — set the prompt, clear the edit and show.</summary>
    public void Open(string prompt = "")
    {
        if (_message != null && prompt.Length > 0)
            _message.Text = prompt;
        if (_editClan != null)
            _editClan.Text = string.Empty;
        _root.SetVisible(true);
    }

    public void Close() => _root.SetVisible(false);

    /// <summary>CUICreateClanName::MsgSend_MakeClan — WIZ_KNIGHTS_PROCESS create with the name.</summary>
    public byte[]? Send()
    {
        string name = ClanName;
        if (name.Length == 0)
            return null;
        byte[] packet = KnightsProtocol.BuildCreate(name);
        _context.Client.Send(packet);
        Close();
        return packet;
    }

    private void OnMessage(UiControl sender, uint msg)
    {
        if (msg != UiMsg.ButtonClick || !_root.Visible)
            return;

        string id = sender.Id.ToLowerInvariant();
        if (id == "btn_yes")
        {
            string name = ClanName;
            if (name.Length == 0)
                return; // CUICreateClanName::MakeClan bails on empty
            ConfirmRequested?.Invoke(name);
        }
        else if (id == "btn_no")
        {
            Close();
        }
    }
}

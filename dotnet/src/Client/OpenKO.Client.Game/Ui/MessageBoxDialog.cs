using OpenKO.Client.Engine.Ui;

namespace OpenKO.Client.Game.Ui;

/// <summary>Which buttons a message box shows (MB_OK / MB_YESNO).</summary>
public enum MessageBoxStyle
{
    Ok,
    YesNo,
}

/// <summary>The button the user pressed.</summary>
public enum MessageBoxResult
{
    Ok,
    Yes,
    No,
    Cancel,
}

/// <summary>
/// Controller for the shared message box — port of <c>CUIMessageBox</c>
/// (Btn_OK/Btn_Yes/Btn_No/Btn_Cancel + Text_Title/Text_Message). One layout
/// instance is shown/hidden and re-texted per post, like the original manager.
/// </summary>
public sealed class MessageBoxDialog
{
    private readonly UiControl? _btnOk;
    private readonly UiControl? _btnYes;
    private readonly UiControl? _btnNo;
    private readonly UiControl? _btnCancel;
    private readonly UiStringControl? _message;
    private readonly UiStringControl? _title;

    private Action<MessageBoxResult>? _onResult;

    public UiControl Root { get; }

    public MessageBoxDialog(UiControl root)
    {
        Root = root;
        _btnOk = root.GetChildById("Btn_OK");
        _btnYes = root.GetChildById("Btn_Yes");
        _btnNo = root.GetChildById("Btn_No");
        _btnCancel = root.GetChildById("Btn_Cancel");
        _message = root.GetChildById<UiStringControl>("Text_Message");
        _title = root.GetChildById<UiStringControl>("Text_Title");
        root.Message += OnMessage;
        root.SetVisible(false);
    }

    public bool IsOpen => Root.Visible;

    /// <summary>CGameProcedure::MessageBoxPost.</summary>
    public void Show(string message, string title = "", MessageBoxStyle style = MessageBoxStyle.Ok,
        Action<MessageBoxResult>? onResult = null)
    {
        if (_message != null)
            _message.Text = message;
        if (_title != null)
            _title.Text = title;
        _onResult = onResult;

        bool yesNo = style == MessageBoxStyle.YesNo;
        _btnOk?.SetVisible(!yesNo);
        _btnYes?.SetVisible(yesNo);
        _btnNo?.SetVisible(yesNo);
        _btnCancel?.SetVisible(false);

        Root.SetVisible(true);
    }

    public void Close() => Root.SetVisible(false);

    private void OnMessage(UiControl sender, uint msg)
    {
        if (msg != UiMsg.ButtonClick || !Root.Visible)
            return;

        MessageBoxResult? result =
            ReferenceEquals(sender, _btnOk) ? MessageBoxResult.Ok
            : ReferenceEquals(sender, _btnYes) ? MessageBoxResult.Yes
            : ReferenceEquals(sender, _btnNo) ? MessageBoxResult.No
            : ReferenceEquals(sender, _btnCancel) ? MessageBoxResult.Cancel
            : null;

        if (result is { } r)
        {
            Close();
            Action<MessageBoxResult>? cb = _onResult;
            _onResult = null;
            cb?.Invoke(r);
        }
    }
}

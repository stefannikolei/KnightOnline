using OpenKO.Game.Rendering;
using OpenKO.N3;
using OpenKO.Numerics;

namespace OpenKO.Game.Procedures;

/// <summary>Basic server-selection state between login and character-select.</summary>
public sealed class ServerSelectProcedure : GameProcedure
{
    public int HighlightedServerIndex { get; private set; }
    private N3UIBase _root = new();
    private readonly List<Rect> _serverRows = new();

    public override void Init()
    {
        if (Context.SelectedServerIndex < 0 && Context.Servers.Count > 0)
            Context.SelectedServerIndex = 0;

        HighlightedServerIndex = Context.SelectedServerIndex >= 0 ? Context.SelectedServerIndex : 0;
        BuildLayout(Context.UiRenderer?.ScreenWidth ?? 1024, Context.UiRenderer?.ScreenHeight ?? 768);
    }

    public override void Render()
    {
        IUiRenderer? r = Context.UiRenderer;
        if (r == null)
            return;

        r.Begin();
        r.DrawQuad(_root.Region, new UiColor(10, 14, 28));
        if (_serverRows.Count == 0)
        {
            r.DrawQuad(new Rect(312, 260, 712, 508), new UiColor(46, 52, 70));
        }
        else
        {
            for (int i = 0; i < _serverRows.Count; i++)
            {
                UiColor color = i == HighlightedServerIndex
                    ? new UiColor(196, 152, 64)
                    : new UiColor(46, 52, 70);
                r.DrawQuad(_serverRows[i], color);
            }
        }

        r.End();
    }

    public void MoveSelection(int delta)
    {
        if (Context.Servers.Count == 0)
            return;

        int count = Context.Servers.Count;
        HighlightedServerIndex = ((HighlightedServerIndex + delta) % count + count) % count;
    }

    public bool TrySelectCurrentServer() => TrySelectServer(HighlightedServerIndex);

    public bool TrySelectServer(int index)
    {
        if (index < 0 || index >= Context.Servers.Count)
            return false;

        HighlightedServerIndex = index;
        Context.SelectedServerIndex = index;
        Context.ServerName = Context.Servers[index].Name;
        Context.Procedures.SetActive(new CharacterSelectProcedure());
        return true;
    }

    private void BuildLayout(int screenWidth, int screenHeight)
    {
        _root = new N3UIBase { Id = "server_select", Region = new Rect(0, 0, screenWidth, screenHeight) };
        _serverRows.Clear();
        int panelWidth = 420;
        int rowHeight = 52;
        int gap = 10;
        int rows = Math.Max(1, Context.Servers.Count);
        int panelHeight = (rows * rowHeight) + ((rows - 1) * gap) + 40;
        int left = (screenWidth - panelWidth) / 2;
        int top = (screenHeight - panelHeight) / 2;
        int rowTop = top + 20;

        for (int i = 0; i < Context.Servers.Count; i++)
        {
            _serverRows.Add(new Rect(
                left + 16,
                rowTop + i * (rowHeight + gap),
                left + panelWidth - 16,
                rowTop + i * (rowHeight + gap) + rowHeight));
        }
    }
}

using OpenKO.Common;
using OpenKO.Game.Rendering;
using OpenKO.Net;
using OpenKO.N3;
using OpenKO.Numerics;

namespace OpenKO.Game.Procedures;

/// <summary>Basic character-selection state after server selection.</summary>
public sealed class CharacterSelectProcedure : GameProcedure
{
    private const byte InitialZoneJoinFlag = 0x01;

    public int HighlightedCharacterIndex { get; private set; }
    private N3UIBase _root = new();
    private readonly List<Rect> _characterRows = new();

    public override void Init()
    {
        HighlightedCharacterIndex = Context.CharacterSelectIndex;
        BuildLayout(Context.UiRenderer?.ScreenWidth ?? 1024, Context.UiRenderer?.ScreenHeight ?? 768);

        if (Context.MainSocket is { IsConnected: true } socket)
            socket.Send(GameProtocol.BuildAllCharacterInfoRequest());
    }

    public override bool ProcessPacket(Packet packet)
    {
        if ((GameOpcode)packet.Opcode != GameOpcode.AllCharInfoReq)
            return false;

        CharacterListResult result = GameProtocol.ParseCharacterList(packet);
        if (!result.Success)
            return true;

        Context.Characters.Clear();
        Context.Characters.AddRange(result.Characters);
        if (HighlightedCharacterIndex >= Context.Characters.Count)
            HighlightedCharacterIndex = Context.Characters.Count - 1;
        if (HighlightedCharacterIndex < 0 && Context.Characters.Count > 0)
            HighlightedCharacterIndex = 0;
        Context.CharacterSelectIndex = HighlightedCharacterIndex;
        return true;
    }

    public override void Render()
    {
        IUiRenderer? r = Context.UiRenderer;
        if (r == null)
            return;

        r.Begin();
        r.DrawQuad(_root.Region, new UiColor(10, 14, 28));
        for (int i = 0; i < _characterRows.Count; i++)
        {
            bool occupied = i < Context.Characters.Count && !string.IsNullOrEmpty(Context.Characters[i].Name);
            UiColor color = i == HighlightedCharacterIndex
                ? new UiColor(196, 152, 64)
                : occupied ? new UiColor(46, 52, 70) : new UiColor(32, 36, 48);
            r.DrawQuad(_characterRows[i], color);
        }
        r.End();
    }

    public void MoveSelection(int delta)
    {
        int count = _characterRows.Count;
        if (count == 0)
            return;

        HighlightedCharacterIndex = ((HighlightedCharacterIndex + delta) % count + count) % count;
        Context.CharacterSelectIndex = HighlightedCharacterIndex;
    }

    public bool TrySelectCurrentCharacter() => TrySelectCharacter(HighlightedCharacterIndex);

    public bool TrySelectCharacter(int index)
    {
        if (index < 0 || index >= Context.Characters.Count)
            return false;

        CharacterSlotInfo info = Context.Characters[index];
        if (string.IsNullOrEmpty(info.Name))
            return false;

        Context.CharacterSelectIndex = index;
        if (Context.MainSocket is not { IsConnected: true } socket)
            return false;

        socket.Send(GameProtocol.BuildCharacterSelect(Context.Account, info.Name, zoneInit: InitialZoneJoinFlag, zoneCurrent: info.Zone));
        return true;
    }

    private void BuildLayout(int screenWidth, int screenHeight)
    {
        _root = new N3UIBase { Id = "character_select", Region = new Rect(0, 0, screenWidth, screenHeight) };
        _characterRows.Clear();

        int panelWidth = 520;
        int rowHeight = 70;
        int gap = 14;
        int rows = GameProtocol.MaxCharacterSlots;
        int panelHeight = (rows * rowHeight) + ((rows - 1) * gap) + 40;
        int left = (screenWidth - panelWidth) / 2;
        int top = (screenHeight - panelHeight) / 2;
        int rowTop = top + 20;

        for (int i = 0; i < rows; i++)
        {
            _characterRows.Add(new Rect(
                left + 16,
                rowTop + i * (rowHeight + gap),
                left + panelWidth - 16,
                rowTop + i * (rowHeight + gap) + rowHeight));
        }
    }
}

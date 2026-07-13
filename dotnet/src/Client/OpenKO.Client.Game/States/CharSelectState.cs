using OpenKO.Client.Game.Net;
using OpenKO.Core.Protocol;

namespace OpenKO.Client.Game.States;

/// <summary>
/// Port of CGameProcCharacterSelect: request the account's characters, then on a
/// pick either create (empty slot → char create) or select (WIZ_SEL_CHAR); the
/// shared handler advances to the game on the reply.
/// </summary>
public sealed class CharSelectState(GameContext context) : GameState
{
    /// <summary>zoneInit byte for the first connect (WIZ_SEL_CHAR).</summary>
    public const byte FirstConnect = 0x01;

    public override string Name => "CharSelect";

    /// <summary>MsgSend_RequestAllCharacterInfo.</summary>
    public override void Init() => context.Client.Send(GameProtocol.BuildAllCharInfoRequest());

    /// <summary>CharacterSelectOrCreate: empty slot creates, occupied slot selects.</summary>
    public void SelectCharacter(int index)
    {
        if (index < 0 || index >= context.Characters.Count)
            return;

        context.SelectedCharIndex = index;
        CharacterSlot slot = context.Characters[index];
        if (slot.IsEmpty)
        {
            context.CharCreate.SlotIndex = index;
            context.Machine.SetActive(context.CharCreate);
            return;
        }

        context.Client.Send(GameProtocol.BuildSelectCharacter(
            context.Account, slot.CharId, FirstConnect, slot.Zone));
    }

    public override bool ProcessPacket(ReadOnlySpan<byte> payload)
    {
        if (context.ProcessSharedPacket(payload))
            return true;

        switch ((GameOpcode)payload[0])
        {
            case GameOpcode.WIZ_ALLCHAR_INFO_REQ:
            {
                AllCharInfoResult result = GameProtocol.ParseAllCharInfo(payload);
                context.Characters = result.Slots;
                context.CharactersReceived?.Invoke(result.Slots);
                return true;
            }

            case GameOpcode.WIZ_DEL_CHAR:
                // Re-request the slots after a delete (the C++ refreshes the list).
                context.Client.Send(GameProtocol.BuildAllCharInfoRequest());
                return true;
        }

        return false;
    }
}

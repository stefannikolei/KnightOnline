using OpenKO.Client.Game.Net;
using OpenKO.Core.Protocol;

namespace OpenKO.Client.Game.States;

/// <summary>Port of CGameProcCharacterCreate: create a character, back to select on success.</summary>
public sealed class CharCreateState(GameContext context) : GameState
{
    public override string Name => "CharCreate";

    /// <summary>The slot the new character fills (set by the char-select screen).</summary>
    public int SlotIndex { get; set; }

    /// <summary>Raised with the WIZ_NEW_CHAR result byte (0 = success).</summary>
    public Action<byte>? CreateResult { get; set; }

    /// <summary>MsgSendCharacterCreate.</summary>
    public void CreateCharacter(
        string charId, byte race, short charClass, byte face, byte hair,
        byte str, byte sta, byte dex, byte intel, byte cha)
    {
        context.Client.Send(GameProtocol.BuildNewCharacter(
            (byte)SlotIndex, charId, race, charClass, face, hair, str, sta, dex, intel, cha));
    }

    public override bool ProcessPacket(ReadOnlySpan<byte> payload)
    {
        if (context.ProcessSharedPacket(payload))
            return true;

        if ((GameOpcode)payload[0] == GameOpcode.WIZ_NEW_CHAR)
        {
            byte result = payload[1];
            CreateResult?.Invoke(result);
            if (result == 0) // success → return to the select screen
                context.Machine.SetActive(context.CharSelect);
            return true;
        }

        return false;
    }
}

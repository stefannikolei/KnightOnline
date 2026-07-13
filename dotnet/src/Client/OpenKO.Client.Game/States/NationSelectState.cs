using OpenKO.Client.Game.Net;
using OpenKO.Core.Protocol;

namespace OpenKO.Client.Game.States;

/// <summary>Port of CGameProcNationSelect: pick Karus/El Morad, then char select.</summary>
public sealed class NationSelectState(GameContext context) : GameState
{
    public const byte Karus = 1;
    public const byte ElMorad = 2;

    public override string Name => "NationSelect";

    /// <summary>MsgSendNationSelect.</summary>
    public void SelectNation(byte nation) => context.Client.Send(GameProtocol.BuildSelectNation(nation));

    public override bool ProcessPacket(ReadOnlySpan<byte> payload)
    {
        if (context.ProcessSharedPacket(payload))
            return true;

        if ((GameOpcode)payload[0] == GameOpcode.WIZ_SEL_NATION)
        {
            byte nation = GameProtocol.ParseSelectNation(payload);
            if (nation is Karus or ElMorad)
            {
                context.Nation = nation;
                context.NationResolved?.Invoke(nation);
                context.Machine.SetActive(context.CharSelect);
            }

            return true;
        }

        return false;
    }
}

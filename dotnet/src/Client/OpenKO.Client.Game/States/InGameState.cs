using OpenKO.Client.Game.Net;
using OpenKO.Client.Game.World;
using OpenKO.Core.Protocol;

namespace OpenKO.Client.Game.States;

/// <summary>
/// Port of CGameProcMain's in-world core: the WIZ_GAMESTART handshake and the
/// region entity stream (WIZ_MYINFO, WIZ_MOVE, WIZ_USER_INOUT, WIZ_CHAT) into a
/// client-side world roster. The zone load (terrain/sky/water from stage 6),
/// the full MyInfo stat block, NPCs and combat/magic land in later slices.
/// </summary>
public sealed class InGameState(GameContext context) : GameState
{
    public override string Name => "InGame";

    /// <summary>True once the state has been entered (the char-select spawn is set).</summary>
    public bool Entered { get; private set; }

    /// <summary>The client-side world roster (local + visible remote players).</summary>
    public WorldEntities World { get; } = new();

    /// <summary>The local player's inventory (filled from MyInfo in a later slice).</summary>
    public Inventory Inventory { get; } = new();

    /// <summary>The spawn zone/position carried over from char select.</summary>
    public SelectCharResult Spawn => context.Spawn;

    // In-world observation hooks.
    public Action<ChatMessage>? ChatReceived { get; set; }

    public Action<RemotePlayer>? PlayerEntered { get; set; }

    public Action<short>? PlayerLeft { get; set; }

    public Action<bool>? ItemMoveResult { get; set; }

    public Action<MagicPacket>? MagicReceived { get; set; }

    /// <summary>Group packets surfaced as (sub-command, full payload) for the dialogs.</summary>
    public Action<byte, byte[]>? PartyReceived { get; set; }

    public Action<byte, byte[]>? ExchangeReceived { get; set; }

    public Action<byte, byte[]>? WarehouseReceived { get; set; }

    public Action<byte, byte[]>? KnightsReceived { get; set; }

    public override void Init()
    {
        Entered = true;

        // Place the local player from the char-select spawn (until MyInfo lands).
        World.Local.X = Spawn.X * 0.1f;
        World.Local.Z = Spawn.Z * 0.1f;
        World.Local.Y = Spawn.Y * 0.1f;

        // MsgSend_GameStart phase 1 (loading). The zone is assumed loaded here.
        context.Client.Send(GameProtocol.BuildGameStartRequest());
    }

    public override void Release()
    {
        Entered = false;
        World.Clear();
    }

    /// <summary>Sends a chat line (CUser::Chat request).</summary>
    public void SendChat(byte type, string text) => context.Client.Send(WorldProtocol.BuildChat(type, text));

    /// <summary>Requests an item move; the local inventory updates optimistically.</summary>
    public void SendItemMove(ItemMoveDirection dir, int itemId, byte srcPos, byte destPos)
    {
        Inventory.MoveItem(srcPos, destPos);
        context.Client.Send(ItemProtocol.BuildItemMove(dir, itemId, srcPos, destPos));
    }

    /// <summary>Sends a magic-process step (CMagicProcess flow).</summary>
    public void SendMagic(MagicPacket packet) => context.Client.Send(MagicProtocol.Build(packet));

    /// <summary>Sends a pre-built group packet (party/exchange/warehouse/knights).</summary>
    public void SendRaw(ReadOnlySpan<byte> payload) => context.Client.Send(payload);

    /// <summary>CUser::MoveProcess request — update the local position and tell the server.</summary>
    public void SendMove(float x, float y, float z, short speed)
    {
        World.Local.X = x;
        World.Local.Y = y;
        World.Local.Z = z;
        context.Client.Send(WorldProtocol.BuildMove(x, y, z, speed, echo: 0));
    }

    public override bool ProcessPacket(ReadOnlySpan<byte> payload)
    {
        if (context.ProcessSharedPacket(payload))
            return true;

        switch ((GameOpcode)payload[0])
        {
            case GameOpcode.WIZ_GAMESTART:
                // The server acknowledges phase 1; reply phase 2 (finished loading).
                context.Client.Send(GameProtocol.BuildGameStartAck());
                return true;

            case GameOpcode.WIZ_MYINFO:
                WorldProtocol.ParseMyInfoInto(payload, World.Local);
                return true;

            case GameOpcode.WIZ_MOVE:
            {
                MoveUpdate move = WorldProtocol.ParseMove(payload);
                World.Move(move.Id, move.X, move.Y, move.Z);
                return true;
            }

            case GameOpcode.WIZ_USER_INOUT:
            {
                byte type = WorldProtocol.ParseInOutType(payload);
                if (type == 1) // USER_IN
                {
                    RemotePlayer player = WorldProtocol.ParseUserIn(payload);
                    World.AddOrUpdate(player);
                    PlayerEntered?.Invoke(player);
                }
                else // USER_OUT
                {
                    short id = WorldProtocol.ParseInOutId(payload);
                    if (World.Remove(id))
                        PlayerLeft?.Invoke(id);
                }

                return true;
            }

            case GameOpcode.WIZ_CHAT:
            {
                ChatMessage chat = WorldProtocol.ParseChat(payload);
                ChatReceived?.Invoke(chat);
                return true;
            }

            case GameOpcode.WIZ_ITEM_MOVE:
                ItemMoveResult?.Invoke(ItemProtocol.ParseItemMoveSucceeded(payload));
                return true;

            case GameOpcode.WIZ_MAGIC_PROCESS:
                MagicReceived?.Invoke(MagicProtocol.Parse(payload));
                return true;

            case GameOpcode.WIZ_PARTY:
                PartyReceived?.Invoke(PartyProtocol.Subcommand(payload), payload.ToArray());
                return true;

            case GameOpcode.WIZ_EXCHANGE:
                ExchangeReceived?.Invoke(ExchangeProtocol.Subcommand(payload), payload.ToArray());
                return true;

            case GameOpcode.WIZ_WAREHOUSE:
                WarehouseReceived?.Invoke(WarehouseProtocol.Subcommand(payload), payload.ToArray());
                return true;

            case GameOpcode.WIZ_KNIGHTS_PROCESS:
                KnightsReceived?.Invoke(KnightsProtocol.Subcommand(payload), payload.ToArray());
                return true;
        }

        return false;
    }
}

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

    /// <summary>The cast gate + cooldown tracker shared by the hotkey bar (CMagicSkillMng).</summary>
    public MagicCastManager Magic { get; } = new();

    /// <summary>The spawn zone/position carried over from char select.</summary>
    public SelectCharResult Spawn => context.Spawn;

    // In-world observation hooks.
    public Action<ChatMessage>? ChatReceived { get; set; }

    /// <summary>Raised when the full WIZ_MYINFO block populates the local player + inventory.</summary>
    public Action<LocalPlayer>? MyInfoReceived { get; set; }

    public Action<RemotePlayer>? PlayerEntered { get; set; }

    public Action<short>? PlayerLeft { get; set; }

    public Action<NpcEntity>? NpcEntered { get; set; }

    public Action<short>? NpcLeft { get; set; }

    /// <summary>Raised on WIZ_HP_CHANGE with (maxHp, hp) for the local player.</summary>
    public Action<short, short>? HpChanged { get; set; }

    /// <summary>Raised on WIZ_DEAD with the socket/NPC id that died.</summary>
    public Action<short>? EntityDied { get; set; }

    /// <summary>Raised on a WIZ_ATTACK broadcast (attacker/target/result).</summary>
    public Action<AttackEvent>? AttackObserved { get; set; }

    /// <summary>Raised on WIZ_TARGET_HP with the target's health and the damage dealt.</summary>
    public Action<TargetHpUpdate>? TargetHpReceived { get; set; }

    /// <summary>
    /// Raised on the WIZ_ITEM_MOVE reply (parsed stat blob). The local stat block is applied
    /// to <see cref="WorldEntities.Local"/> before the event fires, so subscribers (inventory
    /// dialog commit/rollback, state bar refresh) see the updated maxima.
    /// </summary>
    public Action<Net.ItemMoveResult>? ItemMoveResult { get; set; }

    /// <summary>
    /// Raised on the WIZ_BUNDLE_OPEN_REQ reply (a corpse/box loot list). The
    /// <see cref="LootBundle.BundleId"/> is the id carried over from the open request
    /// (<see cref="SendBundleOpen"/>), since it is not echoed on the wire.
    /// </summary>
    public Action<Net.LootBundle>? LootListReceived { get; set; }

    /// <summary>Raised on the WIZ_ITEM_GET reply (a dropped-item pickup result).</summary>
    public Action<Net.ItemGetResult>? ItemGetReceived { get; set; }

    public Action<MagicPacket>? MagicReceived { get; set; }

    /// <summary>
    /// Raised on a WIZ_CLASS_CHANGE reply with the result sub-opcode (0x00 failure ..
    /// 0x04 item-in-slot). The class-change dialog subscribes it (→ Open).
    /// </summary>
    public Action<byte>? ClassChangeResult { get; set; }

    /// <summary>Group packets surfaced as (sub-command, full payload) for the dialogs.</summary>
    public Action<byte, byte[]>? PartyReceived { get; set; }

    public Action<byte, byte[]>? ExchangeReceived { get; set; }

    public Action<byte, byte[]>? WarehouseReceived { get; set; }

    public Action<byte, byte[]>? KnightsReceived { get; set; }

    /// <summary>Raised on a WIZ_WARP_LIST reply (the NPC/object teleport menu).</summary>
    public Action<Net.WarpListReply>? WarpListReceived { get; set; }

    /// <summary>Raised on a WIZ_ITEM_REPAIR reply (NPC blacksmith repair result).</summary>
    public Action<Net.RepairResult>? ItemRepairReceived { get; set; }

    /// <summary>Group WIZ_ITEM_UPGRADE packets surfaced as (sub-opcode, full payload).</summary>
    public Action<byte, byte[]>? ItemUpgradeReceived { get; set; }

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

    /// <summary>
    /// CUIHotKeyDlg::DoOperate → CMagicSkillMng::MsgSend_MagicProcess: run the cast gate for a
    /// skill at <paramref name="targetId"/> and, on success, send the built WIZ_MAGIC_PROCESS
    /// packet. The caster id/position come from the local player; <paramref name="nowSeconds"/> is
    /// the injectable game clock the cooldown gate reads. Returns the cast outcome.
    /// </summary>
    public CastResult CastSkill(Assets.Player.SkillRow skill, short targetId, double nowSeconds)
    {
        LocalPlayer me = World.Local;
        (short, short, short) pos = ((short)me.X, (short)me.Y, (short)me.Z);
        CastResult result = Magic.TryCast(skill, me.SocketId, targetId, pos, me, Inventory, nowSeconds);
        if (result.Success)
            SendMagic(result.Packet);
        return result;
    }

    /// <summary>Spends a skill point into a tab (CUISkillTreeDlg::PointPushUpButton).</summary>
    public void SendSkillPoint(byte tab) => context.Client.Send(SkillPointProtocol.Build(tab));

    /// <summary>
    /// Spends a stat bonus point (CUIState::MsgSendAblityPointChange): type 1=Str, 2=Sta,
    /// 3=Dex, 4=Int, 5=MagicAttack; delta is +1 per press.
    /// </summary>
    public void SendStatPoint(byte type, short delta) => context.Client.Send(StatPointProtocol.Build(type, delta));

    /// <summary>Sends a pre-built WIZ_PARTY packet (party window / cmd-bar party actions).</summary>
    public void SendParty(ReadOnlySpan<byte> payload) => context.Client.Send(payload);

    /// <summary>Sends a pre-built WIZ_KNIGHTS_PROCESS packet (clan dialogs).</summary>
    public void SendKnights(ReadOnlySpan<byte> payload) => context.Client.Send(payload);

    /// <summary>Requests a class change/promotion (CUIClassChange Btn_Class).</summary>
    public void SendClassChangeRequest(short newClass) =>
        context.Client.Send(ClassChangeProtocol.BuildRequest(newClass));

    /// <summary>Sends a general attack at a target (CGameProcMain::MsgSend_Attack).</summary>
    public void SendAttack(short targetId, float interval, float distance) =>
        context.Client.Send(CombatProtocol.BuildAttack(targetId, interval, distance));

    /// <summary>CUIDead::MsgSend_Revival — type 1 = return to town, 2 = life-stone revive.</summary>
    public void SendRevival(byte type) => context.Client.Send(GameProtocol.BuildRevival(type));

    /// <summary>CGameProcMain::MsgSend_RequestTargetHP — ask the server for a target's HP.</summary>
    public void SendTargetHpRequest(short targetId) =>
        context.Client.Send(GameProtocol.BuildTargetHpRequest(targetId));

    /// <summary>Sends a pre-built group packet (party/exchange/warehouse/knights).</summary>
    public void SendRaw(ReadOnlySpan<byte> payload) => context.Client.Send(payload);

    /// <summary>CUIInn::MsgSend_OpenWareHouse — ask the server to open the warehouse.</summary>
    public void SendWarehouseOpen() => context.Client.Send(WarehouseProtocol.BuildOpen());

    /// <summary>CGameProcMain::MsgSend_Warp — confirm the selected teleport destination.</summary>
    public void SendWarp(int warpId) => context.Client.Send(WarpProtocol.BuildWarp(warpId));

    /// <summary>CItemRepairMgr::Tick — request the NPC repair of an item at a slot.</summary>
    public void SendRepair(byte arm, byte order, uint itemId) =>
        context.Client.Send(RepairProtocol.BuildRepair(arm, order, itemId));

    /// <summary>
    /// CGameProcMain::MsgSend_RequestItemBundleOpen — ask the server to open a corpse/box's
    /// loot bundle. Remembers the bundle id so the reply (which does not echo it) can be
    /// tagged for the dropped-item dialog.
    /// </summary>
    public void SendBundleOpen(uint bundleId)
    {
        PendingBundleId = bundleId;
        context.Client.Send(ItemProtocol.BuildBundleOpenRequest(bundleId));
    }

    /// <summary>The bundle id of the most recent <see cref="SendBundleOpen"/> request.</summary>
    public uint PendingBundleId { get; set; }

    /// <summary>
    /// CGameProcMain::MsgSend_Move request — update the local position and tell the
    /// server. moveFlag is 0x01 (moving) | 0x02 (continuous), 0 on stop.
    /// </summary>
    public void SendMove(float x, float y, float z, float speed, byte moveFlag)
    {
        World.Local.X = x;
        World.Local.Y = y;
        World.Local.Z = z;
        context.Client.Send(WorldProtocol.BuildMove(x, y, z, speed, moveFlag));
    }

    /// <summary>CGameProcMain::MsgSend_Rotation request — tell the server the new facing.</summary>
    public void SendRotation(float yaw) => context.Client.Send(WorldProtocol.BuildRotate(yaw));

    /// <summary>
    /// Apply the WIZ_ITEM_MOVE 0x01 stat blob to the local player (the equip change recomputed
    /// attack/guard/weight/HP/MP maxima, item-stat deltas and resistances), clamping HP/MP to
    /// the new maxima like CGameProcMain::MsgRecv_ItemMove.
    /// </summary>
    private void ApplyItemMoveStats(Net.ItemMoveResult res)
    {
        LocalPlayer l = World.Local;
        l.TotalHit = res.Attack;
        l.TotalAc = res.Guard;
        l.MaxWeight = res.WeightMax;
        l.MaxHp = res.HpMax;
        l.MaxMp = res.MspMax;
        if (l.Hp > l.MaxHp)
            l.Hp = l.MaxHp;
        if (l.Mp > l.MaxMp)
            l.Mp = l.MaxMp;

        l.ItemStr = (byte)res.StrDelta;
        l.ItemSta = (byte)res.StaDelta;
        l.ItemDex = (byte)res.DexDelta;
        l.ItemIntel = (byte)res.IntDelta;
        l.ItemCha = (byte)res.MagicAttackDelta;

        l.FireResist = (byte)res.ResistFire;
        l.ColdResist = (byte)res.ResistCold;
        l.LightningResist = (byte)res.ResistLight;
        l.MagicResist = (byte)res.ResistMagic;
        l.DiseaseResist = (byte)res.ResistCurse;
        l.PoisonResist = (byte)res.ResistPoison;
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
                WorldProtocol.ParseMyInfoInto(payload, World.Local, Inventory);
                MyInfoReceived?.Invoke(World.Local);
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

            case GameOpcode.WIZ_NPC_INOUT:
            {
                byte type = NpcProtocol.ParseInOutType(payload);
                if (type == 1) // NPC_IN
                {
                    NpcEntity npc = NpcProtocol.ParseNpcIn(payload);
                    World.AddOrUpdateNpc(npc);
                    NpcEntered?.Invoke(npc);
                }
                else // NPC_OUT
                {
                    short id = NpcProtocol.ParseInOutId(payload);
                    if (World.RemoveNpc(id))
                        NpcLeft?.Invoke(id);
                }

                return true;
            }

            case GameOpcode.WIZ_NPC_MOVE:
            {
                NpcMoveUpdate move = NpcProtocol.ParseNpcMove(payload);
                World.MoveNpc(move.Id, move.X, move.Y, move.Z);
                return true;
            }

            case GameOpcode.WIZ_ROTATE:
            {
                (short id, short dir) = WorldProtocol.ParseRotate(payload);
                World.Rotate(id, dir);
                return true;
            }

            case GameOpcode.WIZ_HP_CHANGE:
            {
                (short maxHp, short hp) = WorldProtocol.ParseHpChange(payload);
                World.Local.MaxHp = maxHp;
                World.Local.Hp = hp;
                HpChanged?.Invoke(maxHp, hp);
                return true;
            }

            case GameOpcode.WIZ_DEAD:
            {
                short id = WorldProtocol.ParseDeadId(payload);
                if (World.MarkDead(id))
                    EntityDied?.Invoke(id);
                return true;
            }

            case GameOpcode.WIZ_ATTACK:
            {
                AttackEvent atk = CombatProtocol.ParseAttack(payload);
                if (atk.Result == CombatProtocol.ResultDeath)
                    World.MarkDead(atk.TargetId);
                AttackObserved?.Invoke(atk);
                return true;
            }

            case GameOpcode.WIZ_TARGET_HP:
                TargetHpReceived?.Invoke(CombatProtocol.ParseTargetHp(payload));
                return true;

            case GameOpcode.WIZ_CHAT:
            {
                ChatMessage chat = WorldProtocol.ParseChat(payload);
                ChatReceived?.Invoke(chat);
                return true;
            }

            case GameOpcode.WIZ_ITEM_MOVE:
            {
                Net.ItemMoveResult res = ItemProtocol.ParseItemMoveResult(payload);
                if (res.Success)
                    ApplyItemMoveStats(res);
                ItemMoveResult?.Invoke(res);
                return true;
            }

            case GameOpcode.WIZ_BUNDLE_OPEN_REQ:
            {
                IReadOnlyList<Net.LootItem> items = ItemProtocol.ParseBundleOpen(payload);
                LootListReceived?.Invoke(new Net.LootBundle(PendingBundleId, items));
                return true;
            }

            case GameOpcode.WIZ_ITEM_GET:
                ItemGetReceived?.Invoke(ItemProtocol.ParseItemGetResult(payload));
                return true;

            case GameOpcode.WIZ_MAGIC_PROCESS:
                MagicReceived?.Invoke(MagicProtocol.Parse(payload));
                return true;

            case GameOpcode.WIZ_CLASS_CHANGE:
                ClassChangeResult?.Invoke(ClassChangeProtocol.ParseResult(payload));
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

            case GameOpcode.WIZ_WARP_LIST:
                WarpListReceived?.Invoke(WarpProtocol.ParseList(payload));
                return true;

            case GameOpcode.WIZ_ITEM_REPAIR:
                ItemRepairReceived?.Invoke(RepairProtocol.ParseResult(payload));
                return true;

            case GameOpcode.WIZ_ITEM_UPGRADE:
                ItemUpgradeReceived?.Invoke(UpgradeProtocol.Subcommand(payload), payload.ToArray());
                return true;
        }

        return false;
    }
}

using System.Numerics;
using OpenKO.Core.Protocol;
using OpenKO.Network;

namespace OpenKO.Servers.AIServer.Ai;

/// <summary>
/// Stage 3.7 part 2a: the CNpc damage/exp layer from Npc.cpp — hit-rate table,
/// damage formulas, SetDamage bookkeeping, target switching and the exp
/// distribution. Enemy acquisition (FindEnemy family) and the attack executors
/// remain stubbed in Npc.State.cs.
/// </summary>
public partial class Npc
{
    // Hit results (Define.h): GREAT_SUCCESS/SUCCESS/NORMAL/FAIL.
    public const byte HitGreatSuccess = 0x01;
    public const byte HitSuccess = 0x02;
    public const byte HitNormal = 0x03;
    public const byte HitFail = 0x04;

    // Further NPC types (shared/globals.h); the rest live in Npc.State.cs.
    private const byte NpcTypeBoss = 3; // NPC_BOSS_MONSTER

    /// <summary>Battle-zone bookkeeping shared with the app (replaces AIServerApp fields).</summary>
    public sealed class BattleZoneState
    {
        public int NpcsKilledByKarus;
        public int NpcsKilledByElmorad;

        /// <summary>Room counts from the zone's RoomEvent data (0 until RoomEvent is ported).</summary>
        public short KarusRooms;
        public short ElmoradRooms;
    }

    /// <summary>Set by the host for battle zones; null elsewhere.</summary>
    public BattleZoneState? Battle;

    private bool IsGateLikeNpc =>
        NpcType is NpcTypeDoor or NpcTypeArtifact or NpcTypePhoenixGate
            or NpcTypeGateLever or NpcTypeDomesticAnimal or NpcTypeSpecialGate
            or NpcTypeDestroyArtifact;

    /// <summary>CNpc::GetDefense.</summary>
    public int GetDefense() => Defense;

    /// <summary>CNpc::GetHitRate — the rate → outcome probability table.</summary>
    public byte GetHitRate(float rate)
    {
        byte result = HitFail;
        int random = MyRand(1, 10000);

        if (rate >= 5.0f)
        {
            if (random <= 3500) result = HitGreatSuccess;
            else if (random <= 7500) result = HitSuccess;
            else if (random <= 9800) result = HitNormal;
        }
        else if (rate >= 3.0f)
        {
            if (random <= 2500) result = HitGreatSuccess;
            else if (random <= 6000) result = HitSuccess;
            else if (random <= 9600) result = HitNormal;
        }
        else if (rate >= 2.0f)
        {
            if (random <= 2000) result = HitGreatSuccess;
            else if (random <= 5000) result = HitSuccess;
            else if (random <= 9400) result = HitNormal;
        }
        else if (rate >= 1.25f)
        {
            if (random <= 1500) result = HitGreatSuccess;
            else if (random <= 4000) result = HitSuccess;
            else if (random <= 9200) result = HitNormal;
        }
        else if (rate >= 0.8f)
        {
            if (random <= 1000) result = HitGreatSuccess;
            else if (random <= 3000) result = HitSuccess;
            else if (random <= 9000) result = HitNormal;
        }
        else if (rate >= 0.5f)
        {
            if (random <= 800) result = HitGreatSuccess;
            else if (random <= 2500) result = HitSuccess;
            else if (random <= 8000) result = HitNormal;
        }
        else if (rate >= 0.33f)
        {
            if (random <= 600) result = HitGreatSuccess;
            else if (random <= 2000) result = HitSuccess;
            else if (random <= 7000) result = HitNormal;
        }
        else if (rate >= 0.2f)
        {
            if (random <= 400) result = HitGreatSuccess;
            else if (random <= 1500) result = HitSuccess;
            else if (random <= 6000) result = HitNormal;
        }
        else
        {
            if (random <= 200) result = HitGreatSuccess;
            else if (random <= 1000) result = HitSuccess;
            else if (random <= 5000) result = HitNormal;
        }

        return result;
    }

    /// <summary>CNpc::GetNFinalDamage — NPC-vs-NPC damage.</summary>
    public int GetNFinalDamage(Npc? npc)
    {
        short damage = 0;

        if (npc is null)
            return damage;

        float attack = HitRate;
        float avoid = npc.EvadeRate;
        short hit = Damage;
        short ac = npc.Defense;

        byte result = GetHitRate(attack / avoid);

        switch (result)
        {
            case HitGreatSuccess:
                damage = (short)(0.6 * hit);
                if (damage <= 0)
                {
                    damage = 0;
                    break;
                }

                damage = (short)MyRand(0, damage);
                damage += (short)(0.7 * hit);
                break;

            case HitSuccess:
            case HitNormal:
                if (hit - ac > 0)
                {
                    damage = (short)(0.6 * (hit - ac));
                    if (damage <= 0)
                    {
                        damage = 0;
                        break;
                    }

                    damage = (short)MyRand(0, damage);
                    damage += (short)(0.7 * (hit - ac));
                }
                else
                {
                    damage = 0;
                }

                break;

            case HitFail:
                damage = 0;
                break;
        }

        return damage;
    }

    /// <summary>CNpc::GetFinalDamage — NPC-vs-user damage.</summary>
    public int GetFinalDamage(AiUser? user, int type = 1)
    {
        _ = type;
        int damage = 0;

        if (user is null)
            return damage;

        float attack = HitRate;
        float avoid = user.AvoidRate;
        short hit = Damage;
        // The C++ Ac expression algebraically collapses to m_sAC; kept as written.
        short ac = (short)(user.ItemAC + user.Level + (short)(user.AC - user.Level - user.ItemAC));

        short hitB = (short)(hit * 200 / (ac + 240));

        int maxDamage = (int)(2.6 * Damage);

        byte result = GetHitRate(attack / avoid);

        switch (result)
        {
            case HitGreatSuccess:
                damage = hitB;
                if (damage <= 0)
                {
                    damage = 0;
                    break;
                }

                damage = (int)(0.3f * MyRand(0, damage));
                damage += (short)(0.85f * hitB);
                damage = damage * 3 / 2;
                break;

            case HitSuccess:
            case HitNormal:
                damage = hitB;
                if (damage <= 0)
                {
                    damage = 0;
                    break;
                }

                damage = (int)(0.3f * MyRand(0, damage));
                damage += (short)(0.85f * hitB);
                break;

            case HitFail:
                damage = 0;
                break;
        }

        if (damage > maxDamage)
            damage = maxDamage;

        return damage;
    }

    /// <summary>CNpc::IsCloseTarget(CUser*, range): double detection radius, locks target.</summary>
    public bool IsCloseTarget(AiUser? user, int range)
    {
        if (user is null)
            return false;

        if (user.HP <= 0 || user.Live == 0)
            return false;

        var npcPos = new Vector3(CurX, CurY, CurZ);
        var userPos = new Vector3(user.CurX, user.CurY, user.CurZ);
        float distance = GetDistance(npcPos, userPos);

        // Attacked state: twice the detection range.
        if ((int)distance > range * 2)
            return false;

        Target.Id = user.Uid + UserBand;
        Target.X = user.CurX;
        Target.Y = user.CurY;
        Target.Z = user.CurZ;

        return true;
    }

    /// <summary>CNpc::ChangeTarget — possibly retarget onto the attacking user.</summary>
    public void ChangeTarget(int attackType, AiUser? user)
    {
        int random = MyRand(0, 100);

        if (user is null)
            return;

        if (user.Live == AiUser.UserDead)
            return;

        // Same nation is never attacked.
        if (user.Nation == Group)
            return;

        // Game masters are ignored (AUTHORITY_MANAGER == 0).
        if (user.IsOperator == 0)
            return;

        if (IsGateLikeNpc)
            return;

        if (State == NpcState.Fainting)
            return;

        AiUser? previous = null;
        if (Target.Id >= 0 && Target.Id < NpcBand)
            previous = GetUserPtr(Target.Id - UserBand);

        if (ReferenceEquals(user, previous))
        {
            // Family types redirect aggro to friends in sight.
            if (GroupType != 0)
            {
                Target.FailCount = 0;
                FindFriend(NpcType == NpcTypeBoss ? 1 : 0);
            }
            else if (NpcType == NpcTypeBoss)
            {
                Target.FailCount = 0;
                FindFriend(1);
            }

            return;
        }

        if (previous is not null)
        {
            // Strongest attacker (50%).
            if (random < 50)
            {
                int preDamage = previous.GetDamage(Nid + NpcBand);
                int lastDamage = user.GetDamage(Nid + NpcBand);
                if (preDamage > lastDamage)
                    return;
            }
            // Closest player (30%).
            else if (random < 80)
            {
                var npcPos = new Vector3(CurX, CurY, CurZ);
                float distance1 = GetDistance(npcPos, new Vector3(previous.CurX, 0, previous.CurZ));
                float distance2 = GetDistance(npcPos, new Vector3(user.CurX, 0, user.CurZ));
                if (distance2 > distance1)
                    return;
            }

            // Whom the NPC hurts most (15%).
            if (random is >= 80 and < 95)
            {
                if (GetFinalDamage(previous, 0) > GetFinalDamage(user, 0))
                    return;
            }

            // 95-100: heal-magic user — no preference (empty branch in the C++).
        }
        else if (attackType == 1004)
        {
            // No previous target: don't react to heal magic.
            return;
        }

        Target.Id = user.Uid + UserBand;
        Target.X = user.CurX;
        Target.Y = user.CurY;
        Target.Z = user.CurZ;

        // Idle states counterattack immediately.
        if (State is NpcState.Standing or NpcState.Moving or NpcState.Sleeping)
        {
            if (IsCloseTarget(user, AttackRange))
            {
                State = NpcState.Fighting;
                Delay = 0;
                DelayTime = TimeGet();
            }
            else
            {
                int value = GetTargetPath(1);
                if (value == 1)
                {
                    State = NpcState.Tracing;
                    Delay = 0;
                    DelayTime = TimeGet();
                }
                else if (value == -1)
                {
                    State = NpcState.Standing;
                    Delay = 0;
                    DelayTime = TimeGet();
                }
                else if (value == 0)
                {
                    SecForMeter = Speed2; // run speed while attacking
                    IsNoPathFind(SecForMeter);
                    State = NpcState.Tracing;
                    Delay = 0;
                    DelayTime = TimeGet();
                }
            }
        }

        if (GroupType != 0)
        {
            Target.FailCount = 0;
            FindFriend(NpcType == NpcTypeBoss ? 1 : 0);
        }
        else if (NpcType == NpcTypeBoss)
        {
            Target.FailCount = 0;
            FindFriend(1);
        }
    }

    /// <summary>CNpc::ChangeNTarget — possibly retarget onto an attacking NPC.</summary>
    public void ChangeNTarget(Npc? npc)
    {
        if (npc is null)
            return;

        if (npc.State == NpcState.Dead)
            return;

        Npc? previous = null;
        if (Target.Id >= NpcBand && Target.Id < InvalidBand)
            previous = World?.Npcs.GetValueOrDefault(Target.Id - NpcBand);

        if (ReferenceEquals(npc, previous))
            return;

        if (previous is not null)
        {
            int preDamage = GetNFinalDamage(previous);
            int lastDamage = GetNFinalDamage(npc);

            var npcPos = new Vector3(CurX, CurY, CurZ);
            float distance = GetDistance(npcPos, new Vector3(previous.CurX, 0, previous.CurZ));
            preDamage = (int)(preDamage / (double)distance + 0.5);
            distance = GetDistance(npcPos, new Vector3(npc.CurX, 0, npc.CurZ));
            lastDamage = (int)(lastDamage / (double)distance + 0.5);

            if (preDamage > lastDamage)
                return;
        }

        Target.Id = npc.Nid + NpcBand;
        Target.X = npc.CurX;
        Target.Y = npc.CurZ; // C++ quirk: y is set from CurZ
        Target.Z = npc.CurZ;

        if (State is NpcState.Standing or NpcState.Moving or NpcState.Sleeping)
        {
            if (IsCloseTarget(AttackRange) == 1)
            {
                State = NpcState.Fighting;
                Delay = 0;
                DelayTime = TimeGet();
            }
            else
            {
                int value = GetTargetPath();
                if (value == 1)
                {
                    State = NpcState.Tracing;
                    Delay = 0;
                    DelayTime = TimeGet();
                }
                else if (value == -1)
                {
                    State = NpcState.Standing;
                    Delay = 0;
                    DelayTime = TimeGet();
                }
                else if (value == 0)
                {
                    SecForMeter = Speed2;
                    IsNoPathFind(SecForMeter);
                    State = NpcState.Tracing;
                    Delay = 0;
                    DelayTime = TimeGet();
                }
            }
        }

        if (GroupType != 0)
        {
            Target.FailCount = 0;
            FindFriend();
        }
    }

    /// <summary>
    /// CNpc::SetDamage — damage bookkeeping + death + retarget/faint reactions.
    /// Returns false when the NPC died.
    /// </summary>
    public bool SetDamage(int attackType, int damage, string sourceName, int uid)
    {
        if (State == NpcState.Dead)
            return true;

        if (HP <= 0)
            return true;

        if (damage < 0)
            return true;

        if (GetMapByIndex() is null)
            return true;

        AiUser? user = null;
        Npc? targetNpc = null;
        bool applyToList = true;

        if (uid is >= UserBand and < NpcBand)
        {
            user = GetUserPtr(uid);
            if (user is null)
                return true;
        }
        // C++ quirk: the NPC branch checks m_Target.id (not uid) against INVALID_BAND.
        else if (uid >= NpcBand && Target.Id < InvalidBand)
        {
            targetNpc = World?.Npcs.GetValueOrDefault(uid - NpcBand);
            if (targetNpc is null)
                return true;

            applyToList = false; // goto go_result
        }

        int userDamage = damage;

        if (applyToList)
        {
            // Surplus damage beyond the remaining HP doesn't count.
            if (HP - damage < 0)
                userDamage = HP;

            bool durationFlag = false;
            string durationId = string.Empty;
            bool accounted = false;

            for (int i = 0; i < AiConstants.NpcMaxUserList; i++)
            {
                if (DamagedUserList[i].Uid != uid)
                    continue;

                if (string.Equals("**duration**", sourceName, StringComparison.OrdinalIgnoreCase))
                {
                    durationFlag = true;
                    durationId = user!.UserId;
                    if (string.Equals(DamagedUserList[i].UserId, durationId, StringComparison.OrdinalIgnoreCase))
                    {
                        DamagedUserList[i].Damage += userDamage;
                        accounted = true;
                        break;
                    }
                }
                else if (string.Equals(DamagedUserList[i].UserId, sourceName, StringComparison.OrdinalIgnoreCase))
                {
                    DamagedUserList[i].Damage += userDamage;
                    accounted = true;
                    break;
                }
            }

            if (!accounted)
            {
                for (int i = 0; i < AiConstants.NpcMaxUserList; i++)
                {
                    if (DamagedUserList[i].Uid != -1 || DamagedUserList[i].Damage > 0)
                        continue;

                    int len = sourceName.Length;
                    if (len > ProtocolConstants.MaxIdSize || len <= 0)
                        continue;

                    if (durationFlag)
                        DamagedUserList[i].UserId = durationId;
                    else if (string.Equals("**duration**", sourceName, StringComparison.OrdinalIgnoreCase))
                        DamagedUserList[i].UserId = user!.UserId;
                    else
                        DamagedUserList[i].UserId = sourceName;

                    DamagedUserList[i].Uid = uid;
                    DamagedUserList[i].Damage = userDamage;
                    DamagedUserList[i].InSight = false;
                    break;
                }
            }
        }

        // go_result:
        TotalDamage += userDamage;
        HP -= damage;

        if (HP <= 0)
        {
            HP = 0;
            Dead();
            return false;
        }

        int random = MyRand(1, 100);

        if (uid is >= UserBand and < NpcBand)
        {
            // Stun skill (attack type 3).
            if (attackType == 3 && State != NpcState.Fainting)
            {
                int lightningResistChance = (int)(10 + (40 - 40 * (LightningResist / 80.0)));
                if (Compare(random, 0, lightningResistChance))
                {
                    State = NpcState.Fainting;
                    Delay = 0;
                    DelayTime = TimeGet();
                    FaintingTime = TimeGet();
                }
                else
                {
                    ChangeTarget(attackType, user);
                }
            }
            else
            {
                ChangeTarget(attackType, user);
            }
        }

        if (uid >= NpcBand && Target.Id < InvalidBand)
            ChangeNTarget(targetNpc);

        return true;
    }

    /// <summary>CNpc::SetHMagicDamage — heal-family magic applied to a NPC.</summary>
    public bool SetHMagicDamage(int damage)
    {
        if (State == NpcState.Dead)
            return false;

        if (HP <= 0)
            return false;

        if (damage <= 0)
            return false;

        HP += damage;
        if (HP < 0)
            HP = 0;
        else if (HP > MaxHP)
            HP = MaxHP;

        var buffer = new byte[16];
        var writer = new PacketWriter(buffer);
        writer.SetByte(AiOpcode.AG_USER_SET_HP);
        writer.SetShort(Nid + NpcBand);
        writer.SetDWord((uint)HP);
        SendAll(writer.Written);

        return true;
    }

    /// <summary>CNpc::SendDead — after Dead(): item drop + respawn delay.</summary>
    public int SendDead(int type = 1)
    {
        if (State != NpcState.Dead || HP > 0)
            return 0;

        if (type != 0)
            GiveNpcHaveItem();

        return RegenTime;
    }

    private const int TypeMoneySid = 900000000; // TYPE_MONEY_SID (Packet.h)
    private const int ArrowMin = 391010000;
    private const int ArrowMax = 392010000;
    private const byte ElmoradMan = 2; // ELMORAD_MAN (KarusMan lives in Npc.State.cs)

    /// <summary>Item-drop log hook (spdlog AIServerItem logger in the C++).</summary>
    public Action<int>? LogItemDrop;

    /// <summary>CNpc::GiveNpcHaveItem — rolls drops and broadcasts AG_NPC_GIVE_ITEM.</summary>
    public void GiveNpcHaveItem()
    {
        var giveItems = new (int Sid, short Count)[AiConstants.NpcHaveItemList];
        int count = 1;

        int random = MyRand(70, 100);
        int money = Money * random / 100;

        if (money <= 0)
        {
            count = 0;
        }
        else
        {
            if (money > 32767)
                money = 32000; // int16 cap, as in the C++
            giveItems[0] = (TypeMoneySid, (short)money);
        }

        if (World is not null)
        {
            foreach (Data.Models.MonsterItem row in World.MonsterItemTable)
            {
                if (row.MonsterId != Item)
                    continue;

                // Slots mirror the C++ matrix: pairs of (itemId, chance);
                // pair index 0 corresponds to matrix column j == 1.
                for (int pair = 0; pair < row.ItemId.Length && pair < row.DropChance.Length; pair++)
                {
                    int itemId = row.ItemId[pair];
                    if (itemId == 0)
                        continue;

                    random = MyRand(1, 10000);
                    int chance = row.DropChance[pair];
                    if (chance == 0)
                        continue;

                    if (random > chance)
                        continue;

                    int makeItemCode;
                    if (pair == 0) // j == 1
                    {
                        makeItemCode = itemId < 100 ? ItemProdution(itemId) : GetItemGroupNumber(itemId);
                        if (makeItemCode == 0)
                            continue;

                        giveItems[count] = (makeItemCode, 1);
                    }
                    else if (pair == 1) // j == 3
                    {
                        makeItemCode = GetItemGroupNumber(itemId);
                        if (makeItemCode == 0)
                            continue;

                        giveItems[count] = (makeItemCode, 1);
                    }
                    else
                    {
                        short itemCount = Compare(itemId, ArrowMin, ArrowMax) ? (short)20 : (short)1;
                        giveItems[count] = (itemId, itemCount);
                    }

                    if (++count >= AiConstants.NpcHaveItemList)
                        break;
                }
            }
        }

        if (MaxDamageUserId < 0 || MaxDamageUserId > AiConstants.MaxUser)
            return;

        var buffer = new byte[1024];
        var writer = new PacketWriter(buffer);
        writer.SetByte(OpenKO.Core.Protocol.AiOpcode.AG_NPC_GIVE_ITEM);
        writer.SetShort(MaxDamageUserId);
        writer.SetShort(Nid + NpcBand);
        writer.SetShort(CurZone);
        writer.SetShort(RegionX);
        writer.SetShort(RegionZ);
        writer.SetFloat(CurX);
        writer.SetFloat(CurZ);
        writer.SetFloat(CurY);
        writer.SetByte((byte)count);
        for (int i = 0; i < count; i++)
        {
            writer.SetInt(giveItems[i].Sid);
            writer.SetShort(giveItems[i].Count);

            if (giveItems[i].Sid != TypeMoneySid)
                LogItemDrop?.Invoke(giveItems[i].Sid);
        }

        SendAll(writer.Written);
    }

    /// <summary>CNpc::ItemProdution — composes a random item id from the drop tables.</summary>
    public int ItemProdution(int itemNumber)
    {
        int random = MyRand(1, 10000);

        int itemGrade = GetItemGrade(itemNumber);
        if (itemGrade == 0)
            return 0;

        int itemLevel = Level / 5;
        int itemId;

        // Weapons (40%).
        if (Compare(random, 1, 4001))
        {
            const int baseCode = 100000000;

            // Weapon kind (dagger, sword, axe, ...).
            random = MyRand(1, 10000);
            int kind = 0;
            if (Compare(random, 1, 701)) kind = 10000000;
            else if (Compare(random, 701, 1401)) kind = 20000000;
            else if (Compare(random, 1401, 2101)) kind = 30000000;
            else if (Compare(random, 2101, 2801)) kind = 40000000;
            else if (Compare(random, 2801, 3501)) kind = 50000000;
            else if (Compare(random, 3501, 5501)) kind = 60000000;
            else if (Compare(random, 5501, 6501)) kind = 70000000;
            else if (Compare(random, 6501, 8501)) kind = 80000000;
            else if (Compare(random, 8501, 10001)) kind = 90000000;

            int classCode = GetWeaponItemCodeNumber(1);
            if (classCode == 0)
                return 0;

            int itemCode = classCode * 100000;

            // Nation (elmorad/karus).
            int nation = MyRand(1, 10000);
            nation = Compare(nation, 1, 5000) ? 10000 : 50000;

            // One- vs two-handed.
            int handed = MyRand(1, 10000);
            handed = Compare(handed, 1, 5000) ? 0 : 5000000;

            int rareCode = GetItemCodeNumber(itemLevel, 1);
            if (rareCode == -1)
                return 0;

            itemId = baseCode + itemCode + kind + nation + handed + rareCode * 10 + itemGrade;
        }
        // Armor (40%).
        else if (Compare(random, 4001, 8001))
        {
            const int baseCode = 200000000;

            int classCode = GetWeaponItemCodeNumber(2);
            if (classCode == 0)
                return 0;

            int itemCode = classCode * 1000000;
            int job = 0, race = 0;

            if (MaxDamagedNation == KarusMan)
            {
                random = MyRand(0, 10000);
                if (Compare(random, 0, 2000))
                {
                    job = 0;
                    race = 10000;   // warrior armor: Arch Tuarek only
                }
                else if (Compare(random, 2000, 4000))
                {
                    job = 40000000;
                    race = 20000;   // rogue armor: Tuarek only
                }
                else if (Compare(random, 4000, 6000))
                {
                    job = 60000000;
                    race = 30000;   // mage armor: Wrinkle Tuarek only
                }
                else if (Compare(random, 6000, 10001))
                {
                    job = 80000000;
                    random = MyRand(0, 10000);
                    race = Compare(random, 0, 5000) ? 20000 : 40000; // priest: Tuarek/Purituarek
                }
            }
            else if (MaxDamagedNation == ElmoradMan)
            {
                random = MyRand(0, 10000);
                if (Compare(random, 0, 3300))
                {
                    job = 0;

                    int key = MyRand(0, 10000);
                    if (Compare(key, 0, 3333)) race = 110000;
                    else if (Compare(key, 3333, 6666)) race = 120000;
                    else if (Compare(key, 6666, 10001)) race = 130000;
                }
                else if (Compare(random, 3300, 5600))
                {
                    job = 40000000;
                    race = Compare(MyRand(0, 10000), 0, 5000) ? 120000 : 130000;
                }
                else if (Compare(random, 5600, 7800))
                {
                    job = 60000000;
                    race = Compare(MyRand(0, 10000), 0, 5000) ? 120000 : 130000;
                }
                else if (Compare(random, 7800, 10001))
                {
                    job = 80000000;
                    race = Compare(MyRand(0, 10000), 0, 5000) ? 120000 : 130000;
                }
            }

            // Body part.
            int part = 0;
            int temp = MyRand(0, 10000);
            if (Compare(temp, 0, 2000)) part = 1000;
            else if (Compare(temp, 2000, 4000)) part = 2000;
            else if (Compare(temp, 4000, 6000)) part = 3000;
            else if (Compare(temp, 6000, 8000)) part = 4000;
            else if (Compare(temp, 8000, 10001)) part = 5000;

            int rareCode = GetItemCodeNumber(itemLevel, 2);
            if (rareCode == -1)
                return 0;

            itemId = baseCode + job + itemCode + race + part + rareCode * 10 + itemGrade;
        }
        // Accessories (20%).
        else
        {
            const int baseCode = 300000000;

            random = MyRand(0, 10000);
            int kind = 0;
            if (Compare(random, 0, 2500)) kind = 10000000;
            else if (Compare(random, 2500, 5000)) kind = 20000000;
            else if (Compare(random, 5000, 7500)) kind = 30000000;
            else if (Compare(random, 7500, 10001)) kind = 40000000;

            int nation = MyRand(1, 10000);
            nation = Compare(nation, 1, 5000) ? 110000 : 150000;

            int rareCode = GetItemCodeNumber(itemLevel, 3);
            if (rareCode == -1)
                return 0;

            itemId = baseCode + kind + nation + rareCode * 10 + itemGrade;
        }

        return itemId;
    }

    /// <summary>CNpc::GetItemGrade — rolls the grade (1-9) from MAKE_ITEM_GRADECODE.</summary>
    public int GetItemGrade(int itemGrade)
    {
        Data.Models.MakeItemGradeCode? row = World?.MakeGradeItemTable.GetValueOrDefault(itemGrade);
        if (row is null)
            return 0;

        int random = MyRand(1, 1000);

        int percent = 0;
        for (int i = 0; i < row.Grade.Length; i++)
        {
            int grade = row.Grade[i];
            if (grade == 0)
                continue;

            if (Compare(random, percent, percent + grade))
                return i + 1;

            percent += grade;
        }

        return 0;
    }

    /// <summary>CNpc::GetWeaponItemCodeNumber — class roll from MAKE_WEAPON/MAKE_DEFENSIVE.</summary>
    public int GetWeaponItemCodeNumber(int itemType)
    {
        int itemLevel = Level / 10;
        short[]? classes = null;

        if (itemType == 1)
        {
            classes = World?.MakeWeaponTable.GetValueOrDefault(itemLevel)?.Class;
        }
        else if (itemType == 2)
        {
            Data.Models.MakeDefensive? row = World?.MakeDefensiveTable.GetValueOrDefault(itemLevel);
            if (row is not null)
                classes = [row.Class1, row.Class2, row.Class3, row.Class4, row.Class5, row.Class6, row.Class7];
        }

        if (classes is null)
            return 0;

        int random = MyRand(0, 1000);

        int percent = 0;
        for (int i = 0; i < classes.Length; i++)
        {
            if (classes[i] == 0)
                continue;

            if (Compare(random, percent, percent + classes[i]))
                return i + 1;

            percent += classes[i];
        }

        return 0;
    }

    /// <summary>CNpc::GetItemCodeNumber — rare/magic/general roll from MAKE_ITEM_LARECODE.</summary>
    public int GetItemCodeNumber(int level, int itemType)
    {
        int random = MyRand(0, 1000);
        Data.Models.MakeItemRareCode? row = World?.MakeRareItemTable.GetValueOrDefault(level);
        if (row is null)
            return -1;

        int[] percentages = [row.RareItem, row.MagicItem, row.GeneralItem];

        int kind = 0;
        int percent = 0;
        for (int i = 0; i < 3; i++)
        {
            if (Compare(random, percent, percent + percentages[i]))
            {
                kind = i + 1;
                break;
            }

            percent += percentages[i];
        }

        int itemCode = 0;
        switch (kind)
        {
            case 1: // rare
                if (itemType == 1) itemCode = MyRand(16, 24);
                else if (itemType == 2) itemCode = MyRand(12, 24);
                else if (itemType == 3) itemCode = MyRand(0, 10);
                break;

            case 2: // magic
                if (itemType == 1) itemCode = MyRand(6, 15);
                else if (itemType == 2) itemCode = MyRand(6, 11);
                else if (itemType == 3) itemCode = MyRand(0, 10);
                break;

            case 3: // general
                if (itemType == 1) itemCode = 5;
                else if (itemType == 2) itemCode = 5;
                else if (itemType == 3) itemCode = MyRand(0, 10);
                break;
        }

        return itemCode;
    }

    /// <summary>CNpc::GetItemGroupNumber — random slot from MAKE_ITEM_GROUP.</summary>
    public int GetItemGroupNumber(int groupId)
    {
        Data.Models.MakeItemGroup? row = World?.MakeItemGroupTable.GetValueOrDefault(groupId);
        if (row is null)
            return 0;

        int slot = MyRand(0, 10000) % row.Item.Length;
        if (slot < 0 || slot >= row.Item.Length)
            return 0;

        return row.Item[slot];
    }

    private const byte NpcAttackShout = 0; // NPC_ATTACK_SHOUT

    /// <summary>
    /// CNpc::FindFriend — scans surrounding regions for allies.
    /// type 0: same group+family aggro help, 1: everyone helps, 2: healer scan
    /// (returns the NID of the friend most in need of healing).
    /// </summary>
    public int FindFriend(int type = 0)
    {
        AiZone? map = GetMapByIndex();
        if (map is null)
            return 0;

        if (SearchRange == 0)
            return 0;

        if (type != 2 && Target.Id == -1)
            return 0;

        int maxXx = map.RegionsX;
        int maxZz = map.RegionsZ;

        int minX = (int)(CurX - SearchRange) / AiConstants.ViewDistance;
        if (minX < 0)
            minX = 0;

        int minZ = (int)(CurZ - SearchRange) / AiConstants.ViewDistance;
        if (minZ < 0)
            minZ = 0;

        int maxX = (int)(CurX + SearchRange) / AiConstants.ViewDistance;
        if (maxX >= maxXx)
            maxX = maxXx - 1;

        int maxZ = (int)(CurZ + SearchRange) / AiConstants.ViewDistance;
        // C++ quirk: clamps min_z against the Z region count instead of max_z.
        if (minZ >= maxZz)
            minZ = maxZz - 1;

        int searchX = maxX - minX + 1;
        int searchZ = maxZ - minZ + 1;

        var healers = new (short Nid, short Value)[9];
        for (int i = 0; i < healers.Length; i++)
            healers[i] = (-1, 0);

        int count = 0;
        for (int i = 0; i < searchX; i++)
        {
            for (int j = 0; j < searchZ; j++)
                FindFriendRegion(minX + i, minZ + j, map, ref healers[count], type);
        }

        int bestValue = 0, monsterNid = 0;
        for (int i = 0; i < healers.Length; i++)
        {
            if (bestValue < healers[i].Value)
            {
                bestValue = healers[i].Value;
                monsterNid = healers[i].Nid;
            }
        }

        if (monsterNid != 0)
        {
            Target.Id = monsterNid;
            return monsterNid;
        }

        return 0;
    }

    /// <summary>CNpc::FindFriendRegion — one region of the friend scan.</summary>
    private void FindFriendRegion(int x, int z, AiZone map, ref (short Nid, short Value) healer, int type)
    {
        if (!map.IsValidRegion(x, z))
            return;

        int[] npcIds = [.. map.Regions[x, z].Npcs];

        // Attacked state: type 2 scans within attack range, others within tracing range.
        float searchRange = type == 2 ? AttackRange : TracingRange;
        var start = new Vector3(CurX, CurY, CurZ);
        int bestValue = 0;

        foreach (int nid in npcIds)
        {
            if (nid < NpcBand)
                continue;

            Npc? npc = World?.Npcs.GetValueOrDefault(nid - NpcBand);
            if (npc is null || npc.State == NpcState.Dead || npc.Nid == Nid)
                continue;

            float distance = GetDistance(start, new Vector3(npc.CurX, npc.CurY, npc.CurZ));
            if (distance > searchRange)
                continue;

            if (type == 1)
            {
                if (npc.Target.Id > -1 && npc.State == NpcState.Fighting)
                    continue;

                npc.Target.Id = Target.Id;
                npc.Target.X = Target.X;
                npc.Target.Y = Target.Y;
                npc.Target.Z = Target.Z;
                npc.Target.FailCount = 0;
                npc.NpcStrategy(NpcAttackShout);
            }
            else if (type == 0)
            {
                if (npc.GroupType != 0 && npc.FamilyType == FamilyType)
                {
                    if (npc.Target.Id > -1 && npc.State == NpcState.Fighting)
                        continue;

                    npc.Target.Id = Target.Id;
                    npc.Target.X = Target.X;
                    npc.Target.Y = Target.Y;
                    npc.Target.Z = Target.Z;
                    npc.Target.FailCount = 0;
                    npc.NpcStrategy(NpcAttackShout);
                }
            }
            else if (type == 2)
            {
                // Healer scan: pick the friend missing the most HP (below 90%).
                int threshold = (int)(npc.MaxHP * 0.9);
                if (npc.HP <= threshold)
                {
                    int compValue = (int)((npc.MaxHP - npc.HP) / (npc.MaxHP * 0.01));
                    if (bestValue < compValue)
                    {
                        bestValue = compValue;
                        healer.Nid = (short)(npc.Nid + NpcBand);
                        healer.Value = (short)bestValue;
                    }
                }
            }
        }
    }

    /// <summary>CNpc::NpcStrategy — reaction to an ally's attack shout.</summary>
    public void NpcStrategy(byte type)
    {
        switch (type)
        {
            case NpcAttackShout:
                State = NpcState.Tracing;
                Delay = Speed;
                DelayTime = TimeGet();
                break;
        }
    }

    /// <summary>CNpc::IsUserInSight — refresh the InSight flags of the damage list (50m).</summary>
    public void IsUserInSight()
    {
        var start = new Vector3(CurX, CurY, CurZ);

        for (int j = 0; j < AiConstants.NpcMaxUserList; j++)
            DamagedUserList[j].InSight = false;

        for (int i = 0; i < AiConstants.NpcMaxUserList; i++)
        {
            AiUser? user = GetUserPtr(DamagedUserList[i].Uid);
            if (user is null)
                continue;

            float distance = GetDistance(start, new Vector3(user.CurX, user.CurY, user.CurZ));
            if ((int)distance <= AiConstants.NpcExpRange
                && DamagedUserList[i].Uid == user.Uid
                && string.Equals(DamagedUserList[i].UserId, user.UserId, StringComparison.OrdinalIgnoreCase))
            {
                DamagedUserList[i].InSight = true;
            }
        }
    }

    /// <summary>CNpc::IsLevelCheck — true when the user outlevels the NPC by 8+.</summary>
    public bool IsLevelCheck(int level)
    {
        if (level <= Level)
            return false;

        return level - Level >= 8;
    }

    /// <summary>CNpc::IsHPCheck — true when below 20% HP.</summary>
    public bool IsHPCheck(int hp)
    {
        _ = hp; // unused in the C++
        return HP < MaxHP * 0.2;
    }

    /// <summary>CNpc::IsCompStatus — flee when low on HP.</summary>
    public bool IsCompStatus(AiUser user)
    {
        if (IsHPCheck(user.HP))
        {
            if (RandomBackMove())
                return true;
        }

        return false;
    }

    /// <summary>CNpc::IsInExpRange — within 50m and the same zone.</summary>
    public bool IsInExpRange(AiUser user)
    {
        var start = new Vector3(CurX, CurY, CurZ);
        var end = new Vector3(user.CurX, user.CurY, user.CurZ);
        float distance = GetDistance(start, end);

        return (int)distance <= AiConstants.NpcExpRange && CurZone == user.CurZone;
    }

    /// <summary>CNpc::GetPartyDamage — accumulated damage of one party's members.</summary>
    public int GetPartyDamage(int partyNumber)
    {
        int damage = 0;
        AiUser? user = null;

        for (int i = 0; i < AiConstants.NpcMaxUserList; i++)
        {
            if (DamagedUserList[i].Uid < 0 || DamagedUserList[i].Damage <= 0)
                continue;

            // C++ quirk: pUser is only refreshed when InSight is set — a stale
            // pointer from the previous iteration is reused otherwise.
            if (DamagedUserList[i].InSight)
                user = GetUserPtr(DamagedUserList[i].Uid);

            if (user is null)
                continue;

            if (user.PartyNumber != partyNumber)
                continue;

            damage += DamagedUserList[i].Damage;
        }

        return damage;
    }

    /// <summary>CNpc::GetPartyExp — party exp scaling by level gap.</summary>
    public int GetPartyExp(int partyLevel, int members, int npcExp)
    {
        int level = Level - partyLevel / members;

        if (level < 2)
            return npcExp;

        double value;
        if (level < 5)
            value = npcExp * 1.2;
        else if (level < 8)
            value = npcExp * 1.5;
        else
            return npcExp * 2;

        int exp = (int)value;
        if (value > exp)
            exp++;

        return exp;
    }

    /// <summary>CNpc::SendExpToUserList — distributes exp/loyalty over the damage list.</summary>
    public void SendExpToUserList()
    {
        AiZone? map = GetMapByIndex();
        if (map is null)
            return;

        double compDamage = 0;
        AiUser? user = null;
        string maxDamageUser = string.Empty;

        IsUserInSight();

        for (int i = 0; i < AiConstants.NpcMaxUserList; i++)
        {
            if (DamagedUserList[i].Uid < 0 || DamagedUserList[i].Damage <= 0)
                continue;

            // C++ quirk: pUser persists across iterations when InSight is unset.
            if (DamagedUserList[i].InSight)
                user = GetUserPtr(DamagedUserList[i].Uid);
            if (user is null)
                continue;

            if (user.NowParty == 1)
            {
                double totalDamage = GetPartyDamage(user.PartyNumber);

                int partyExp;
                if (totalDamage == 0 || TotalDamage == 0)
                {
                    partyExp = 0;
                }
                else
                {
                    if (compDamage < totalDamage)
                    {
                        compDamage = totalDamage;
                        MaxDamageUserId = (short)DamagedUserList[i].Uid;
                        AiUser? maxUser = GetUserPtr(DamagedUserList[i].Uid);
                        MaxDamagedNation = (maxUser ?? user).Nation;
                        maxDamageUser = (maxUser ?? user).UserId;
                    }

                    double value = Exp * (totalDamage / TotalDamage);
                    partyExp = (int)value;
                    if (value > partyExp)
                        partyExp++;
                }

                int partyLoyalty;
                if (Loyalty == 0 || totalDamage == 0 || TotalDamage == 0)
                {
                    partyLoyalty = 0;
                }
                else
                {
                    double value = Loyalty * (totalDamage / TotalDamage);
                    partyLoyalty = (int)value;
                    if (value > partyLoyalty)
                        partyLoyalty++;
                }

                // The C++ only distributes when this party wasn't already handled
                // (i==0, or no earlier list entry shares the party).
                bool distribute;
                if (i == 0)
                {
                    distribute = true;
                }
                else
                {
                    int count = 0;
                    AiUser? partyUser = null;
                    for (int j = 0; j < i; j++)
                    {
                        if (DamagedUserList[j].Uid < 0 || DamagedUserList[j].Damage <= 0)
                            continue;

                        if (DamagedUserList[j].InSight)
                            partyUser = GetUserPtr(DamagedUserList[j].Uid);

                        if (partyUser is null)
                            continue;

                        if (user.PartyNumber == partyUser.PartyNumber)
                            continue;

                        count++;
                    }

                    distribute = count == i;
                }

                if (distribute && World is not null
                    && World.Parties.TryGetValue(user.PartyNumber, out PartyGroup? party))
                {
                    int totalMan = 0, totalLevel = 0;
                    foreach (short memberUid in party.Users)
                    {
                        AiUser? member = GetUserPtr(memberUid);
                        if (member is null)
                            continue;

                        totalMan++;
                        totalLevel += member.Level;
                    }

                    partyExp = GetPartyExp(totalLevel, totalMan, partyExp);

                    foreach (short memberUid in party.Users)
                    {
                        AiUser? member = GetUserPtr(memberUid);
                        if (member is null)
                            continue;

                        if (!IsInExpRange(member))
                            continue;

                        double value = partyExp * (1 + 0.3 * (totalMan - 1)) * member.Level / (double)totalLevel;
                        int exp = (int)value;
                        if (value > exp)
                            exp++;

                        int loyalty;
                        if (partyLoyalty <= 0)
                        {
                            loyalty = 0;
                        }
                        else
                        {
                            value = partyLoyalty * (1 + 0.2 * (totalMan - 1)) * member.Level / (double)totalLevel;
                            loyalty = (int)value;
                            if (value > loyalty)
                                loyalty++;
                        }

                        member.SetPartyExp(exp, loyalty, totalLevel, totalMan);
                    }
                }
            }
            else if (user.NowParty == 2)
            {
                // Troops: empty in the C++.
            }
            else
            {
                double totalDamage = DamagedUserList[i].Damage;

                if (totalDamage != 0 && TotalDamage != 0)
                {
                    if (compDamage < totalDamage)
                    {
                        compDamage = totalDamage;
                        MaxDamageUserId = (short)DamagedUserList[i].Uid;
                        AiUser? maxUser = GetUserPtr(DamagedUserList[i].Uid);
                        MaxDamagedNation = (maxUser ?? user).Nation;
                        maxDamageUser = (maxUser ?? user).UserId;
                    }

                    double value = Exp * (totalDamage / TotalDamage);
                    int exp = (int)value;
                    if (value > exp)
                        exp++;

                    int loyalty = 0;
                    if (Loyalty != 0)
                    {
                        value = Loyalty * (totalDamage / TotalDamage);
                        loyalty = (int)value;
                        if (value > loyalty)
                            loyalty++;
                    }

                    user.SetExp(exp, loyalty, Level);
                }
            }
        }

        // Battle-zone bookkeeping (BATTLE_EVENT_MAX_USER / victory result).
        if ((GetBattleEventType?.Invoke() ?? 0xFF) == BattlezoneOpen
            && Battle is not null
            && SpecialType is >= 90 and <= 100
            && maxDamageUser.Length != 0)
        {
            var buffer = new byte[100];
            var writer = new PacketWriter(buffer);
            writer.SetByte(AiOpcode.AG_BATTLE_EVENT);
            writer.SetByte(4); // BATTLE_EVENT_MAX_USER

            switch (SpecialType)
            {
                case 100: writer.SetByte(1); break;
                case 90: writer.SetByte(3); Battle.NpcsKilledByKarus++; break;
                case 91: writer.SetByte(4); Battle.NpcsKilledByKarus++; break;
                case 92: writer.SetByte(5); Battle.NpcsKilledByElmorad++; break;
                case 93: writer.SetByte(6); Battle.NpcsKilledByElmorad++; break;
                case 98: writer.SetByte(7); Battle.NpcsKilledByKarus++; break;
                case 99: writer.SetByte(8); Battle.NpcsKilledByElmorad++; break;
            }

            writer.SetString1(System.Text.Encoding.Latin1.GetBytes(maxDamageUser));
            SendAll(writer.Written);

            // Victory checks need the zone's RoomEvent room counts.
            if (Battle.KarusRooms > 0 && Battle.NpcsKilledByKarus == Battle.KarusRooms)
                SendBattleResult(2, maxDamageUser); // ELMORAD_ZONE loses → value 2
            else if (Battle.ElmoradRooms > 0 && Battle.NpcsKilledByElmorad == Battle.ElmoradRooms)
                SendBattleResult(1, maxDamageUser); // KARUS_ZONE
        }
    }

    private void SendBattleResult(byte winnerZoneType, string maxDamageUser)
    {
        var buffer = new byte[100];
        var writer = new PacketWriter(buffer);
        writer.SetByte(AiOpcode.AG_BATTLE_EVENT);
        writer.SetByte(3); // BATTLE_EVENT_RESULT
        writer.SetByte(winnerZoneType);
        writer.SetString1(System.Text.Encoding.Latin1.GetBytes(maxDamageUser));
        SendAll(writer.Written);
    }

    // Attack results (shared/globals.h).
    private const byte AttackFail = 0;
    private const byte AttackSuccess = 1;
    private const byte AttackTargetDead = 2;
    private const byte AttackTargetDeadOk = 3;

    private const byte UserStateDisconnected = 0x03; // STATE_DISCONNECTED
    private const byte MagicEffecting = 3;           // MAGIC_EFFECTING (e_MagicOpcode)

    /// <summary>
    /// Hook for CNpcMagicProcess::MagicPacket (area magic) until the magic
    /// processor is ported. Receives the MAGIC_EFFECTING payload the C++ builds.
    /// </summary>
    public Action<byte[]>? NpcMagicPacket;

    /// <summary>
    /// CNpc::IsSurround(CUser*): claims an 8-direction attack slot around the target.
    /// 0: ranged NPC (skip), -2: no user, -1: target fully surrounded, else the slot.
    /// </summary>
    public int IsSurround(AiUser? user)
    {
        if (LongType != 0)
            return 0;

        if (user is null)
            return -2;

        int dir = user.IsSurroundCheck(CurX, 0.0f, CurZ, Nid + NpcBand);
        if (dir != 0)
        {
            AttackPos = (byte)dir;
            return dir;
        }

        return -1;
    }

    /// <summary>CNpc::Attack — melee/magic attack executor; returns the next delay.</summary>
    public int DoAttack()
    {
        const int percent = 1000;

        // Pure long-range NPCs always use the magic path.
        if (LongType == 1)
        {
            Delay = LongAndMagicAttack();
            return Delay;
        }

        int standingTime = StandTime;
        int ret = IsCloseTarget(AttackRange, 2);

        if (ret == 0)
        {
            // Stationary gate NPCs never chase.
            if (IsGateLikeNpc)
            {
                State = NpcState.Standing;
                InitTarget();
                return 0;
            }

            StepCount = 0;
            ActionFlag = AttackToTrace;
            State = NpcState.Tracing;
            return 0;
        }

        if (ret == 2)
        {
            if (LongType == 2)
            {
                Delay = LongAndMagicAttack();
                return Delay;
            }

            if (IsGateLikeNpc)
            {
                State = NpcState.Standing;
                InitTarget();
                return 0;
            }

            StepCount = 0;
            ActionFlag = AttackToTrace;
            State = NpcState.Tracing;
            return 0;
        }

        if (ret == -1)
        {
            State = NpcState.Standing;
            InitTarget();
            return 0;
        }

        int targetId = Target.Id;

        if (targetId is >= UserBand and < NpcBand)
        {
            AiUser? user = GetUserPtr(targetId - UserBand);

            if (user is null)
            {
                InitTarget();
                State = NpcState.Standing;
                return standingTime;
            }

            if (user.Live == AiUser.UserDead)
            {
                SendAttackSuccess(AttackTargetDeadOk, user.Uid, 0, 0);
                InitTarget();
                State = NpcState.Standing;
                return standingTime;
            }

            if (user.State == UserStateDisconnected)
            {
                InitTarget();
                State = NpcState.Standing;
                return standingTime;
            }

            if (user.IsOperator == 0) // AUTHORITY_MANAGER
            {
                InitTarget();
                State = NpcState.Moving;
                return standingTime;
            }

            // Area-magic monsters (attack types 4/5): 10% roll for an area cast.
            if (WhatAttackType is 4 or 5)
            {
                if (MyRand(1, 10000) < percent)
                {
                    var buffer = new byte[32];
                    var writer = new PacketWriter(buffer);
                    writer.SetByte(MagicEffecting);
                    writer.SetDWord((uint)Magic2);
                    writer.SetShort(Nid + NpcBand);
                    writer.SetShort(-1);
                    writer.SetShort((short)CurX);
                    writer.SetShort((short)CurY);
                    writer.SetShort((short)CurZ);
                    writer.SetShort(0);
                    writer.SetShort(0);
                    writer.SetShort(0);

                    // TODO(stage3.7): route through the ported NpcMagicProcess.
                    NpcMagicPacket?.Invoke(writer.Written.ToArray());
                    return AttackDelay + 1000;
                }
            }
            else if (WhatAttackType == 2)
            {
                // Poison attackers: 10% roll for the poison proc.
                if (MyRand(1, 10000) < percent)
                {
                    var buffer = new byte[32];
                    var writer = new PacketWriter(buffer);
                    writer.SetByte(OpenKO.Core.Protocol.AiOpcode.AG_MAGIC_ATTACK_RESULT);
                    writer.SetByte(MagicEffecting);
                    writer.SetDWord((uint)Magic1);
                    writer.SetShort(Nid + NpcBand);
                    writer.SetShort(user.Uid);
                    writer.SetShort(0);
                    writer.SetShort(0);
                    writer.SetShort(0);
                    writer.SetShort(0);
                    writer.SetShort(0);
                    writer.SetShort(0);
                    SendAll(writer.Written);

                    return AttackDelay;
                }
            }

            int damage = GetFinalDamage(user);

            if (World?.TestMode == true)
                damage = 10;

            if (damage > 0)
            {
                user.SetDamage(damage, Nid + NpcBand);
                if (user.Live != AiUser.UserDead)
                    SendAttackSuccess(AttackSuccess, user.Uid, (short)damage, user.HP);
            }
            else
            {
                SendAttackSuccess(AttackFail, user.Uid, (short)damage, user.HP);
            }
        }
        else if (targetId >= NpcBand && Target.Id < InvalidBand)
        {
            Npc? npc = World?.Npcs.GetValueOrDefault(targetId - NpcBand);

            if (npc is null)
            {
                InitTarget();
                State = NpcState.Standing;
                return standingTime;
            }

            // Healers heal friendly NPCs instead of attacking.
            if (NpcType == NpcTypeHealer && npc.Group == Group)
            {
                State = NpcState.Healing;
                return 0;
            }

            if (npc.HP <= 0 || npc.State == NpcState.Dead)
            {
                SendAttackSuccess(AttackTargetDead, npc.Nid + NpcBand, 0, 0);
                InitTarget();
                State = NpcState.Standing;
                return standingTime;
            }

            int damage = GetNFinalDamage(npc);

            if (damage > 0)
            {
                npc.SetDamage(0, damage, Name, Nid + NpcBand);
                SendAttackSuccess(AttackSuccess, npc.Nid + NpcBand, (short)damage, npc.HP);
            }
            else
            {
                SendAttackSuccess(AttackFail, npc.Nid + NpcBand, (short)damage, npc.HP);
            }
        }

        return AttackDelay;
    }

    private const byte MagicCasting = 1; // MAGIC_CASTING (e_MagicOpcode)

    /// <summary>CNpc::LongAndMagicAttack — ranged/magic attack executor.</summary>
    public int LongAndMagicAttack()
    {
        int standingTime = StandTime;
        int ret = IsCloseTarget(AttackRange, 2);

        if (ret == 0)
        {
            StepCount = 0;
            ActionFlag = AttackToTrace;
            State = NpcState.Tracing;
            return 0;
        }

        if (ret == 2 && LongType == 1)
        {
            StepCount = 0;
            ActionFlag = AttackToTrace;
            State = NpcState.Tracing;
            return 0;
        }

        if (ret == -1)
        {
            State = NpcState.Standing;
            InitTarget();
            return 0;
        }

        int targetId = Target.Id;

        if (targetId is >= UserBand and < NpcBand)
        {
            AiUser? user = GetUserPtr(targetId - UserBand);

            if (user is null)
            {
                InitTarget();
                State = NpcState.Standing;
                return standingTime;
            }

            if (user.Live == AiUser.UserDead)
            {
                SendAttackSuccess(AttackTargetDeadOk, user.Uid, 0, 0);
                InitTarget();
                State = NpcState.Standing;
                return standingTime;
            }

            if (user.State == UserStateDisconnected)
            {
                InitTarget();
                State = NpcState.Standing;
                return standingTime;
            }

            if (user.IsOperator == 0) // AUTHORITY_MANAGER
            {
                InitTarget();
                State = NpcState.Moving;
                return standingTime;
            }

            // Cast magic 1 at the target (the C++ only ever uses magic 1 here).
            var buffer = new byte[32];
            var writer = new PacketWriter(buffer);
            writer.SetByte(MagicCasting);
            writer.SetDWord((uint)Magic1);
            writer.SetShort(Nid + NpcBand);
            writer.SetShort(user.Uid);
            writer.SetShort(0);
            writer.SetShort(0);
            writer.SetShort(0);
            writer.SetShort(0);
            writer.SetShort(0);
            writer.SetShort(0);

            // TODO(stage3.7): route through the ported NpcMagicProcess.
            NpcMagicPacket?.Invoke(writer.Written.ToArray());
        }
        else if (targetId >= NpcBand && Target.Id < InvalidBand)
        {
            Npc? npc = World?.Npcs.GetValueOrDefault(targetId - NpcBand);

            if (npc is null)
            {
                InitTarget();
                State = NpcState.Standing;
                return standingTime;
            }

            if (npc.HP <= 0 || npc.State == NpcState.Dead)
            {
                SendAttackSuccess(AttackTargetDead, npc.Nid + NpcBand, 0, 0);
                InitTarget();
                State = NpcState.Standing;
                return standingTime;
            }

            // NPC-vs-NPC long attack is commented out in the C++.
        }

        return AttackDelay;
    }

    /// <summary>CNpc::TracingAttack — opportunistic hit while chasing (0: fail, 1: success).</summary>
    public int TracingAttack()
    {
        int targetId = Target.Id;

        if (targetId is >= UserBand and < NpcBand)
        {
            AiUser? user = GetUserPtr(targetId - UserBand);

            if (user is null)
                return 0;

            if (user.Live == AiUser.UserDead)
            {
                SendAttackSuccess(AttackTargetDeadOk, user.Uid, 0, 0);
                return 0;
            }

            if (user.State == UserStateDisconnected)
                return 0;

            if (user.IsOperator == 0) // AUTHORITY_MANAGER
                return 0;

            int damage = GetFinalDamage(user);

            if (World?.TestMode == true)
                damage = 1;

            if (damage > 0)
            {
                user.SetDamage(damage, Nid + NpcBand);

                if (user.Live != AiUser.UserDead)
                    SendAttackSuccess(AttackSuccess, user.Uid, (short)damage, user.HP);
            }
            else
            {
                SendAttackSuccess(AttackFail, user.Uid, (short)damage, user.HP);
            }
        }
        else if (targetId >= NpcBand && Target.Id < InvalidBand)
        {
            Npc? npc = World?.Npcs.GetValueOrDefault(targetId - NpcBand);

            if (npc is null)
                return 0;

            if (npc.HP <= 0 || npc.State == NpcState.Dead)
            {
                SendAttackSuccess(AttackTargetDead, npc.Nid + NpcBand, 0, 0);
                return 0;
            }

            int damage = GetNFinalDamage(npc);

            if (damage > 0)
            {
                if (npc.SetDamage(0, damage, Name, Nid + NpcBand))
                {
                    SendAttackSuccess(AttackSuccess, npc.Nid + NpcBand, (short)damage, npc.HP);
                }
                else
                {
                    // Target died from this hit: success + dead notification.
                    SendAttackSuccess(AttackSuccess, npc.Nid + NpcBand, (short)damage, npc.HP);
                    SendAttackSuccess(AttackTargetDead, npc.Nid + NpcBand, (short)damage, npc.HP);
                    return 0;
                }
            }
            else
            {
                SendAttackSuccess(AttackFail, npc.Nid + NpcBand, (short)damage, npc.HP);
            }
        }

        return 1;
    }

    /// <summary>CNpc::FindEnemy — enemy acquisition scan over own + neighbor regions.</summary>
    public bool FindEnemy()
    {
        if (IsGateLikeNpc)
            return false;

        AiZone? map = GetMapByIndex();
        if (map is null)
            return false;

        // Healer NPCs first look for a friend to heal.
        if (NpcType == NpcTypeHealer)
        {
            if (FindFriend(2) != 0)
                return true;
        }

        float compareDis = 0.0f;
        float searchRange = SearchRange;

        FindEnemyRegion();

        if (RegionX > map.RegionsX - 1 || RegionZ > map.RegionsZ - 1
            || RegionX < 0 || RegionZ < 0)
            return false;

        bool isHostileToPlayers = true;

        if (CurZone == 31 /* ZONE_BIFROST */ || CurZone == 21 /* ZONE_MORADON */
            || CurZone / 10 == 5)
        {
            if (Group != 0)
                isHostileToPlayers = false;
        }

        // Guards in Moradon are not hostile (the C++ checks this per loop; upfront here
        // as the upstream NOTE describes).
        if (NpcType is NpcTypeGuard or NpcTypePatrolGuard or NpcTypeStoreGuard
            && CurZone == 21)
        {
            isHostileToPlayers = false;
        }

        if (isHostileToPlayers)
        {
            compareDis = FindEnemyExpand(RegionX, RegionZ, compareDis, 1);

            for (int l = 0; l < 4; l++)
            {
                if (FindX[l] == 0 && FindY[l] == 0)
                    continue;

                int x = RegionX + FindX[l];
                int y = RegionZ + FindY[l];

                if (x < 0 || y < 0 || x > map.RegionsX - 1 || y > map.RegionsZ - 1)
                    continue;

                compareDis = FindEnemyExpand(x, y, compareDis, 1);
            }

            if (Target.Id >= 0 && compareDis <= searchRange)
                return true;
        }

        compareDis = 0.0f;

        // Guards additionally target foreign monsters.
        if (NpcType is NpcTypeGuard or NpcTypePatrolGuard or NpcTypeStoreGuard)
        {
            compareDis = FindEnemyExpand(RegionX, RegionZ, compareDis, 2);

            for (int l = 0; l < 4; l++)
            {
                if (FindX[l] == 0 && FindY[l] == 0)
                    continue;

                int x = RegionX + FindX[l];
                int y = RegionZ + FindY[l];

                if (x < 0 || y < 0 || x > map.RegionsX - 1 || y > map.RegionsZ - 1)
                    continue;

                compareDis = FindEnemyExpand(x, y, compareDis, 2);
            }
        }

        if (Target.Id >= 0 && compareDis <= searchRange)
            return true;

        // Nobody around: reset bookkeeping.
        InitUserList();
        InitTarget();
        return false;
    }

    /// <summary>
    /// CNpc::FindEnemyRegion — determines which neighbor regions the search radius
    /// spills into. C++ quirk kept: all four iCur* values derive from m_fCurX
    /// (the Z coordinate is never used).
    /// </summary>
    public int FindEnemyRegion()
    {
        int sx = RegionX * AiConstants.ViewDistance;
        int sz = RegionZ * AiConstants.ViewDistance;
        int ex = (RegionX + 1) * AiConstants.ViewDistance;
        int ez = (RegionZ + 1) * AiConstants.ViewDistance;
        int curSX = (int)CurX - SearchRange;
        int curSY = (int)CurX - SearchRange;
        int curEX = (int)CurX + SearchRange;
        int curEY = (int)CurX + SearchRange;

        int myPos = GetMyField();
        int ret = 0;

        switch (myPos)
        {
            case 1:
                if (curSX < sx && curSY < sz) ret = 1;
                else if (curSX > sx && curSY < sz) ret = 2;
                else if (curSX < sx && curSY > sz) ret = 4;
                else if (curSX >= sx && curSY >= sz) ret = 0;
                break;

            case 2:
                if (curEX < ex && curSY < sz) ret = 2;
                else if (curEX > ex && curSY < sz) ret = 3;
                else if (curEX <= ex && curSY >= sz) ret = 0;
                else if (curEX > ex && curSY > sz) ret = 5;
                break;

            case 3:
                if (curSX < sx && curEY < ez) ret = 4;
                else if (curSX >= sx && curEY <= ez) ret = 0;
                else if (curSX < sx && curEY > ez) ret = 6;
                else if (curSX > sx && curEY > ez) ret = 7;
                break;

            case 4:
                if (curEX <= ex && curEY <= ez) ret = 0;
                else if (curEX > ex && curEY < ez) ret = 5;
                else if (curEX < ex && curEY > ez) ret = 7;
                else if (curEX > ex && curEY > ez) ret = 8;
                break;
        }

        if (ret <= 0)
            ret = 0;

        (short X, short Y)[] offsets = ret switch
        {
            1 => [(-1, -1), (0, -1), (-1, 0), (0, 0)],
            2 => [(0, -1), (0, 0), (0, 0), (0, 0)],
            3 => [(0, 0), (1, 0), (0, 1), (1, 1)],
            4 => [(-1, 0), (0, 0), (0, 0), (0, 0)],
            5 => [(0, 0), (1, 0), (0, 0), (0, 0)],
            6 => [(-1, 0), (0, 0), (-1, 1), (0, 1)],
            7 => [(0, 0), (0, 0), (0, 1), (0, 0)],
            8 => [(0, 0), (1, 0), (0, 1), (1, 1)],
            _ => [(0, 0), (0, 0), (0, 0), (0, 0)],
        };

        for (int i = 0; i < 4; i++)
        {
            FindX[i] = offsets[i].X;
            FindY[i] = offsets[i].Y;
        }

        return ret;
    }

    /// <summary>
    /// CNpc::FindEnemyExpand — scans one region for the target candidate.
    /// nType 1: users (passive NPCs only lock onto attackers), nType 2: foreign NPCs
    /// (guards). C++ quirk kept: the comparison is fDis &gt;= fComp, i.e. the
    /// FARTHEST candidate within search range wins.
    /// </summary>
    public float FindEnemyExpand(int rx, int rz, float compDis, int type)
    {
        AiZone? map = GetMapByIndex();
        if (map is null)
            return 0.0f;

        float comp = compDis;
        float searchRange = SearchRange;
        var npcPos = new Vector3(CurX, CurY, CurZ);

        if (!map.IsValidRegion(rx, rz))
            return 0.0f;

        if (type == 1)
        {
            int[] ids = [.. map.Regions[rx, rz].Users];
            if (ids.Length == 0)
                return 0.0f;

            foreach (int userId in ids)
            {
                if (userId < 0)
                    continue;

                AiUser? user = GetUserPtr(userId);
                if (user is null || user.Live != AiUser.UserLive)
                    continue;

                if (Group == user.Nation)
                    continue;

                if (user.IsOperator == 0) // AUTHORITY_MANAGER
                    continue;

                float distance = GetDistance(new Vector3(user.CurX, user.CurY, user.CurZ), npcPos);
                if (distance > searchRange)
                    continue;

                if (distance >= comp)
                {
                    int targetUid = user.Uid;
                    comp = distance;

                    if (AttType == 0)
                    {
                        // Passive: only lock onto users who damaged us (or group aggro).
                        if (IsDamagedUserList(user) || (GroupType != 0 && Target.Id == targetUid))
                        {
                            Target.Id = targetUid;
                            Target.FailCount = 0;
                            Target.X = user.CurX;
                            Target.Y = user.CurY;
                            Target.Z = user.CurZ;
                        }
                    }
                    else
                    {
                        Target.Id = targetUid;
                        Target.FailCount = 0;
                        Target.X = user.CurX;
                        Target.Y = user.CurY;
                        Target.Z = user.CurZ;
                    }
                }
            }
        }
        else if (type == 2)
        {
            int[] ids = [.. map.Regions[rx, rz].Npcs];
            if (ids.Length == 0)
                return 0.0f;

            foreach (int npcId in ids)
            {
                if (npcId < NpcBand)
                    continue;

                Npc? npc = World?.Npcs.GetValueOrDefault(npcId - NpcBand);
                if (npc is null || npc.State == NpcState.Dead || npc.Nid == Nid)
                    continue;

                if (CurZone == 31 || CurZone == 21 || CurZone / 10 == 5)
                {
                    if (npc.Group != 0)
                        continue;
                }

                if (Group == npc.Group)
                    continue;

                float distance = GetDistance(new Vector3(npc.CurX, npc.CurY, npc.CurZ), npcPos);
                if (distance > searchRange)
                    continue;

                if (distance >= comp)
                {
                    comp = distance;
                    Target.Id = npcId;
                    Target.FailCount = 0;
                    Target.X = npc.CurX;
                    Target.Y = npc.CurY;
                    Target.Z = npc.CurZ;
                }
            }
        }

        return comp;
    }
}

using System.Numerics;
using Microsoft.Extensions.Logging.Abstractions;
using OpenKO.Core.Protocol;
using OpenKO.GameData.Math;
using OpenKO.Network;

namespace OpenKO.Servers.AIServer.Ai;

/// <summary>
/// Port of the CUser combat/exp methods (Server/AIServer/User.cpp) used by the
/// stage 3.7 part-2 NPC combat layer.
/// </summary>
public partial class AiUser
{
    // ---- Define.h / globals.h constants ----
    private const int UserBand = 0;         // USER_BAND
    private const int NpcBand = 10000;      // NPC_BAND
    private const int InvalidBand = 20000;  // INVALID_BAND

    private const byte GreatSuccess = 1;    // GREAT_SUCCESS
    private const byte Success = 2;         // SUCCESS
    private const byte Normal = 3;          // NORMAL
    private const byte Fail = 4;            // FAIL

    private const byte AttackFail = 0;          // ATTACK_FAIL
    private const byte AttackSuccess = 1;       // ATTACK_SUCCESS
    private const byte AttackTargetDeadResult = 2; // ATTACK_TARGET_DEAD

    private const byte AuthorityManager = 0;        // AUTHORITY_MANAGER
    private const byte AuthorityLimitedManager = 250; // AUTHORITY_LIMITED_MANAGER

    private const int UserDamageOverrideGm = 30_000;       // USER_DAMAGE_OVERRIDE_GM
    private const int UserDamageOverrideLimitedGm = 0;     // USER_DAMAGE_OVERRIDE_LIMITED_GM
    private const int UserDamageOverrideTestMode = 10_000; // USER_DAMAGE_OVERRIDE_TEST_MODE

    // e_NpcType values (shared/globals.h) checked by GetDamage.
    private const byte NpcTypeArtifact = 60;        // NPC_ARTIFACT
    private const byte NpcTypePhoenixGate = 51;     // NPC_PHOENIX_GATE
    private const byte NpcTypeGateLever = 55;       // NPC_GATE_LEVER
    private const byte NpcTypeSpecialGate = 52;     // NPC_SPECIAL_GATE

    // User.cpp globals: 1m surround circle (the NPC-side arrays are the 2m ones).
    private static readonly float[] SurroundFx =
        [0.0f, -0.7071f, -1.0f, -0.7083f, 0.0f, 0.7059f, 1.0000f, 0.7083f];

    private static readonly float[] SurroundFz =
        [1.0f, 0.7071f, 0.0f, -0.7059f, -1.0f, -0.7083f, -0.0017f, 0.7059f];

    // ---- m_pMain replacements (same pattern as Npc) ----

    /// <summary>World state (replaces the m_pMain AIServerApp pointer).</summary>
    public AiWorld? World;

    /// <summary>Replaces CUser::SendAll's zone-socket send (AIServerApp::Send to m_curZone).</summary>
    public Func<byte[], ValueTask>? SendToZone;

    private int MyRand(int min, int max) => World?.Rand(min, max) ?? min;

    private Npc? GetNpcPtr(int nid) => World?.Npcs.GetValueOrDefault(nid);

    private AiZone? GetMapByIndex()
        => World is { } w && ZoneIndex >= 0 && ZoneIndex < w.Zones.Count ? w.Zones[ZoneIndex] : null;

    /// <summary>CUser::SendAll — uid bounds + map check, then send on the zone socket.</summary>
    public void SendAll(ReadOnlySpan<byte> buf)
    {
        if (Uid < 0 || Uid >= AiConstants.MaxUser)
            return;

        if (GetMapByIndex() is null)
            return;

        if (SendToZone is { } send)
            _ = send(buf.ToArray());
    }

    /// <summary>CUser::Attack — user attacks NPC tid.</summary>
    public void Attack(int sid, int tid)
    {
        _ = sid; // unused in the C++ as well

        Npc? npc = GetNpcPtr(tid - NpcBand);
        if (npc is null)
            return;

        if (npc.State == NpcState.Dead)
            return;

        if (npc.HP == 0)
            return;

        int nFinalDamage = GetDamage(tid);

        if (IsOperator == AuthorityManager)
            nFinalDamage = UserDamageOverrideGm;
        else if (IsOperator == AuthorityLimitedManager)
            nFinalDamage = UserDamageOverrideLimitedGm;
        else if (World?.TestMode == true)
            nFinalDamage = UserDamageOverrideTestMode;

        // NPC died from the hit.
        if (!npc.SetDamage(0, nFinalDamage, UserId, Uid + UserBand))
        {
            npc.SendExpToUserList();
            npc.SendDead();
            SendAttackSuccess(tid, AttackTargetDeadResult, (short)nFinalDamage, npc.HP);
        }
        // Send the attack result.
        else
        {
            SendAttackSuccess(tid, AttackSuccess, (short)nFinalDamage, npc.HP);
        }
    }

    /// <summary>CUser::SendAttackSuccess — AG_ATTACK_RESULT (type 0x01, user is attacker).</summary>
    public void SendAttackSuccess(int tuid, byte result, short sDamage, int nHP = 0, byte byAttackType = 1)
    {
        var buf = new byte[256];
        var w = new PacketWriter(buf);
        w.SetByte(AiOpcode.AG_ATTACK_RESULT);
        w.SetByte(0x01);            // type
        w.SetByte(result);
        w.SetShort(Uid + UserBand); // sid
        w.SetShort(tuid);           // tid
        w.SetShort(sDamage);
        w.SetDWord((uint)nHP);
        w.SetByte(byAttackType);

        SendAll(w.Written);
    }

    /// <summary>CUser::SendMagicAttackResult — AG_MAGIC_ATTACK_RESULT (type 0x01).</summary>
    public void SendMagicAttackResult(int tuid, byte result, short sDamage, short sHP = 0)
    {
        var buf = new byte[256];
        var w = new PacketWriter(buf);
        w.SetByte(AiOpcode.AG_MAGIC_ATTACK_RESULT);
        w.SetByte(0x01);            // type
        w.SetByte(result);
        w.SetShort(Uid + UserBand); // sid
        w.SetShort(tuid);           // tid
        w.SetShort(sDamage);
        w.SetShort(sHP);

        SendAll(w.Written);
    }

    /// <summary>CUser::SetDamage — HP bookkeeping; death is handled by Dead.</summary>
    public void SetDamage(int damage, int tid)
    {
        if (damage <= 0)
            return;

        if (Live == UserDead)
            return;

        HP -= (short)damage;

        if (HP <= 0)
        {
            HP = 0;

            // C++ CUser::Dead(tid, damage); reuse the part-1 Dead port.
            if (World is { } world)
            {
                Dead(world, tid, damage, NullLogger.Instance, b =>
                {
                    _ = SendToZone?.Invoke(b);
                    return true;
                });
            }
        }
    }

    /// <summary>CUser::SendHP — AG_USER_SET_HP.</summary>
    public void SendHP()
    {
        if (Live == UserDead)
            return;

        var buf = new byte[256];
        var w = new PacketWriter(buf);
        w.SetByte(AiOpcode.AG_USER_SET_HP);
        w.SetShort(Uid);
        w.SetDWord(unchecked((uint)HP)); // SetDWORD(m_sHP): int16 sign-extended like the C++

        SendAll(w.Written);
    }

    /// <summary>CUser::SetExp — level-difference scaling then SendExp.</summary>
    public void SetExp(int iNpcExp, int iLoyalty, int iLevel)
    {
        int nExp = 0;
        int nLoyalty = 0;
        double tempValue;
        int nLevel = iLevel - Level;

        if (nLevel <= -14)
        {
            tempValue = iNpcExp * 0.2;
            nExp = (int)tempValue;
            if (tempValue > nExp)
                ++nExp;

            tempValue = iLoyalty * 0.2;
            nLoyalty = (int)tempValue;
            if (tempValue > nLoyalty)
                ++nLoyalty;
        }
        else if (nLevel <= -8 && nLevel >= -13)
        {
            tempValue = iNpcExp * 0.5;
            nExp = (int)tempValue;
            if (tempValue > nExp)
                ++nExp;

            tempValue = iLoyalty * 0.5;
            nLoyalty = (int)tempValue;
            if (tempValue > nLoyalty)
                ++nLoyalty;
        }
        else if (nLevel <= -2 && nLevel >= -7)
        {
            tempValue = iNpcExp * 0.8;
            nExp = (int)tempValue;
            if (tempValue > nExp)
                ++nExp;

            tempValue = iLoyalty * 0.8;
            nLoyalty = (int)tempValue;
            if (tempValue > nLoyalty)
                ++nLoyalty;
        }
        else if (nLevel >= -1)
        {
            nExp = iNpcExp;
            nLoyalty = iLoyalty;
        }

        SendExp(nExp, nLoyalty);
    }

    /// <summary>CUser::SetPartyExp — the party scaling happens at the NPC; passthrough.</summary>
    public void SetPartyExp(int iNpcExp, int iLoyalty, int iPartyLevel, int iMan)
    {
        _ = iPartyLevel; // unused in the C++ as well
        _ = iMan;

        SendExp(iNpcExp, iLoyalty);
    }

    /// <summary>CUser::SendExp — AG_USER_EXP.</summary>
    public void SendExp(int iExp, int iLoyalty, int tType = 1)
    {
        _ = tType; // unused in the C++ as well

        var buf = new byte[256];
        var w = new PacketWriter(buf);
        w.SetByte(AiOpcode.AG_USER_EXP);
        w.SetShort(Uid);
        w.SetShort(iExp);
        w.SetShort(iLoyalty);

        SendAll(w.Written);
    }

    /// <summary>CUser::GetDamage — melee/skill/arrow damage against NPC tid.</summary>
    public short GetDamage(int tid, int magicid = 0)
    {
        short damage = 0;
        int random;
        byte result = Fail;

        if (tid < NpcBand || tid > InvalidBand)
            return damage;

        Npc? npc = GetNpcPtr(tid - NpcBand);
        if (npc is null)
            return damage;

        if (npc.NpcType is NpcTypeArtifact or NpcTypePhoenixGate or NpcTypeGateLever or NpcTypeSpecialGate)
            return damage;

        float attack = HitRate;                     // 공격민첩
        float avoid = npc.EvadeRate;                // 방어민첩
        short hit = HitDamage;                      // 공격자 Hit
        short ac = npc.Defense;                     // 방어자 Ac
        short hitB = (short)(int)((hit * 200) / (ac + 240));

        Data.Models.Magic? pTable = null;

        // Skill Hit.
        if (magicid > 0)
        {
            pTable = World?.MagicTable.GetValueOrDefault(magicid);
            if (pTable is null)
                return -1;

            // SKILL HIT!
            if (pTable.Type1 == 1)
            {
                Data.Models.MagicType1? pType1 = World?.MagicType1Table.GetValueOrDefault(magicid);
                if (pType1 is null)
                    return -1;

                // Non-relative hit.
                if (pType1.Type != 0)
                {
                    random = MyRand(0, 100);
                    result = pType1.HitRateMod <= random ? Fail : Success;
                }
                // Relative hit.
                else
                {
                    result = GetHitRate((attack / avoid) * (pType1.HitRateMod / 100.0f));
                }

                hit = (short)(hitB * (pType1.DamageMod / 100.0f));
            }
            // ARROW HIT!
            else if (pTable.Type1 == 2)
            {
                Data.Models.MagicType2? pType2 = World?.MagicType2Table.GetValueOrDefault(magicid);
                if (pType2 is null)
                    return -1;

                // Non-relative/Penetration hit.
                if (pType2.HitType == 1 || pType2.HitType == 2)
                {
                    random = MyRand(0, 100);
                    result = pType2.HitRateMod <= random ? Fail : Success;
                }
                // Relative hit/Arc hit.
                else
                {
                    result = GetHitRate((attack / avoid) * (pType2.HitRateMod / 100.0f));
                }

                if (pType2.HitType == 1)
                    hit = (short)(HitDamage * (pType2.DamageMod / 100.0f));
                else
                    hit = (short)(hitB * (pType2.DamageMod / 100.0f));
            }
        }
        // Normal Hit.
        else
        {
            result = GetHitRate(attack / avoid);
        }

        switch (result)
        {
            case GreatSuccess:
            case Success:
            case Normal:
                // Skill attack.
                if (magicid > 0)
                {
                    damage = hit;
                    random = MyRand(0, damage);
                    if (pTable!.Type1 == 1)
                        damage = (short)(hit + 0.3f * random + 0.99);
                    else
                        damage = (short)(hit * 0.6f + 1.0f * random + 0.99);
                }
                // Normal attack.
                else
                {
                    damage = hitB;
                    random = MyRand(0, damage);
                    damage = (short)(0.85f * hitB + 0.3f * random);
                }
                break;

            case Fail:
                damage = 0;
                break;
        }

        damage = GetMagicDamage(damage, (short)tid); // 2. Magical item damage....

        return damage;
    }

    /// <summary>CUser::GetMagicDamage — elemental weapon bonuses vs NPC resistances.</summary>
    public short GetMagicDamage(int damage, short tid)
    {
        short totalR = 0, tempDamage = 0;

        Npc? npc = GetNpcPtr(tid - NpcBand);
        if (npc is null)
            return (short)damage;

        // RIGHT HAND!!! by Yookozuna
        if (MagicTypeRightHand > 4 && MagicTypeRightHand < 8)
            tempDamage = (short)(damage * MagicAmountRightHand / 100);

        // RIGHT HAND!!!
        switch (MagicTypeRightHand)
        {
            case 1: // Fire Damage
                totalR = npc.FireResist;
                break;

            case 2: // Ice Damage
                totalR = npc.ColdResist;
                break;

            case 3: // Lightning Damage
                totalR = npc.LightningResist;
                break;

            case 4: // Poison Damage
                totalR = npc.PoisonResist;
                break;

            case 5: // HP Drain
                break;

            case 6: // MP Damage
                npc.MSpChange(2, -tempDamage);
                break;

            case 7: // MP Drain
                break;
        }

        if (MagicTypeRightHand > 0 && MagicTypeRightHand < 5)
        {
            tempDamage = (short)(MagicAmountRightHand - MagicAmountRightHand * totalR / 200);
            damage += tempDamage;
        }

        // Reset all temporary data.
        totalR = 0;
        tempDamage = 0;

        // LEFT HAND!!! by Yookozuna
        if (MagicTypeLeftHand > 4 && MagicTypeLeftHand < 8)
            tempDamage = (short)(damage * MagicAmountLeftHand / 100);

        // LEFT HAND!!!
        switch (MagicTypeLeftHand)
        {
            case 1: // Fire Damage
                totalR = npc.FireResist;
                break;

            case 2: // Ice Damage
                totalR = npc.ColdResist;
                break;

            case 3: // Lightning Damage
                totalR = npc.LightningResist;
                break;

            case 4: // Poison Damage
                totalR = npc.PoisonResist;
                break;

            case 5: // HP Drain
                break;

            case 6: // MP Damage
                npc.MSpChange(2, -tempDamage);
                break;

            case 7: // MP Drain
                break;
        }

        if (MagicTypeLeftHand > 0 && MagicTypeLeftHand < 5)
        {
            if (totalR > 200)
                totalR = 200;

            tempDamage = (short)(MagicAmountLeftHand - MagicAmountLeftHand * totalR / 200);
            damage += tempDamage;
        }

        return (short)damage;
    }

    /// <summary>CUser::GetHitRate — hit-quality table (identical to CNpc::GetHitRate).</summary>
    public byte GetHitRate(float rate)
    {
        byte result = Fail;
        int random = MyRand(1, 10000);

        if (rate >= 5.0f)
        {
            if (random >= 1 && random <= 3500)
                result = GreatSuccess;
            else if (random >= 3501 && random <= 7500)
                result = Success;
            else if (random >= 7501 && random <= 9800)
                result = Normal;
        }
        else if (rate >= 3.0f)
        {
            if (random >= 1 && random <= 2500)
                result = GreatSuccess;
            else if (random >= 2501 && random <= 6000)
                result = Success;
            else if (random >= 6001 && random <= 9600)
                result = Normal;
        }
        else if (rate >= 2.0f)
        {
            if (random >= 1 && random <= 2000)
                result = GreatSuccess;
            else if (random >= 2001 && random <= 5000)
                result = Success;
            else if (random >= 5001 && random <= 9400)
                result = Normal;
        }
        else if (rate >= 1.25f)
        {
            if (random >= 1 && random <= 1500)
                result = GreatSuccess;
            else if (random >= 1501 && random <= 4000)
                result = Success;
            else if (random >= 4001 && random <= 9200)
                result = Normal;
        }
        else if (rate >= 0.8f)
        {
            if (random >= 1 && random <= 1000)
                result = GreatSuccess;
            else if (random >= 1001 && random <= 3000)
                result = Success;
            else if (random >= 3001 && random <= 9000)
                result = Normal;
        }
        else if (rate >= 0.5f)
        {
            if (random >= 1 && random <= 800)
                result = GreatSuccess;
            else if (random >= 801 && random <= 2500)
                result = Success;
            else if (random >= 2501 && random <= 8000)
                result = Normal;
        }
        else if (rate >= 0.33f)
        {
            if (random >= 1 && random <= 600)
                result = GreatSuccess;
            else if (random >= 601 && random <= 2000)
                result = Success;
            else if (random >= 2001 && random <= 7000)
                result = Normal;
        }
        else if (rate >= 0.2f)
        {
            if (random >= 1 && random <= 400)
                result = GreatSuccess;
            else if (random >= 401 && random <= 1500)
                result = Success;
            else if (random >= 1501 && random <= 6000)
                result = Normal;
        }
        else
        {
            if (random >= 1 && random <= 200)
                result = GreatSuccess;
            else if (random >= 201 && random <= 1000)
                result = Success;
            else if (random >= 1001 && random <= 5000)
                result = Normal;
        }

        return result;
    }

    /// <summary>CUser::IsSurroundCheck — claim the nearest free 8-direction slot for NpcID.</summary>
    public int IsSurroundCheck(float fX, float fY, float fZ, int npcId)
    {
        int nDir = 0;
        var vNpc = new Vector3(fX, fY, fZ);
        float fCurDis = 1000.0f;
        bool bFlag = false;

        for (int i = 0; i < 8; i++)
        {
            if (SurroundNpcNumber[i] == npcId)
            {
                if (bFlag)
                {
                    SurroundNpcNumber[i] = -1;
                }
                else
                {
                    SurroundNpcNumber[i] = (short)npcId;
                    nDir = i + 1;
                    bFlag = true;
                }
            }

            if (SurroundNpcNumber[i] == -1 && !bFlag)
            {
                float fDX = CurX + SurroundFx[i];
                float fDZ = CurZ + SurroundFz[i];
                var vUser = new Vector3(fDX, 0.0f, fDZ);
                float fDis = KoMath.Magnitude(vUser - vNpc);

                if (fDis < fCurDis)
                {
                    nDir = i + 1;
                    fCurDis = fDis;
                }
            }
        }

        if (nDir != 0)
            SurroundNpcNumber[nDir - 1] = (short)npcId;

        return nDir;
    }

    /// <summary>CUser::HealMagic — aggro nearby enemy NPCs after a heal.</summary>
    public void HealMagic()
    {
        int regionX = (int)(CurX / AiConstants.ViewDistance);
        int regionZ = (int)(CurZ / AiConstants.ViewDistance);

        AiZone? zone = GetMapByIndex();
        if (zone is null)
            return;

        int maxXx = zone.RegionsX;
        int maxZz = zone.RegionsZ;

        int minX = regionX - 1;
        if (minX < 0)
            minX = 0;

        int minZ = regionZ - 1;
        if (minZ < 0)
            minZ = 0;

        int maxX = regionX + 1;
        if (maxX >= maxXx)
            maxX = maxXx - 1;

        int maxZ = regionZ + 1;
        if (minZ >= maxZz)
            minZ = maxZz - 1; // verbatim C++ bug: clamps min_z where max_z was meant

        int searchX = maxX - minX + 1;
        int searchZ = maxZ - minZ + 1;

        for (int i = 0; i < searchX; i++)
        {
            for (int j = 0; j < searchZ; j++)
                HealAreaCheck(minX + i, minZ + j);
        }
    }

    /// <summary>CUser::HealAreaCheck — retarget enemy NPCs within 10m of the healer.</summary>
    public void HealAreaCheck(int rx, int rz)
    {
        AiZone? zone = GetMapByIndex();
        if (zone is null)
            return;

        if (rx < 0 || rz < 0 || rx > zone.RegionsX - 1 || rz > zone.RegionsZ - 1)
            return;

        // 30m (comment kept from the C++; the value is 10)
        const float fRadius = 10.0f;

        var vStart = new Vector3(CurX, 0f, CurZ);

        foreach (int nid in zone.Regions[rx, rz].Npcs.ToArray())
        {
            if (nid < NpcBand)
                continue;

            Npc? npc = GetNpcPtr(nid - NpcBand);

            if (npc is not null && npc.State != NpcState.Dead)
            {
                if (Nation == npc.Group)
                    continue;

                var vEnd = new Vector3(npc.CurX, npc.CurY, npc.CurZ);
                float fDis = Npc.GetDistance(vStart, vEnd);

                // Only NPCs within the radius.
                if (fDis > fRadius)
                    continue;

                npc.ChangeTarget(1004, this);
            }
        }
    }
}

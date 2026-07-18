namespace OpenKO.Client.Game.Ui;

/// <summary>
/// The NPC-blacksmith repair-price formula — a pure port of
/// <c>CItemRepairMgr::CalcRepairGold</c> (Client/WarFare/ItemRepairMgr.cpp:206). The cost is
/// <c>((allPrice-10)/10000 + allPrice^0.75) · (missingDurability / maxDurability)</c>, computed
/// in 32-bit float and truncated to an int exactly like the executable. <paramref name="allPrice"/>
/// is the composed item price (<c>ItemBasic.Price · ItemExt.PriceMultiply</c>); a non-positive
/// max durability yields 0 (a countable / non-wearing item cannot be repaired).
/// </summary>
public static class RepairCost
{
    /// <summary>
    /// CItemRepairMgr::CalcRepairGold — the gold cost to repair from
    /// <paramref name="curDurability"/> back to <paramref name="maxDurability"/>.
    /// </summary>
    public static int Calc(float allPrice, int curDurability, int maxDurability)
    {
        if (maxDurability <= 0)
            return 0;

        float temp = ((allPrice - 10.0f) / 10000.0f) + MathF.Pow(allPrice, 0.75f);
        float value = temp * ((float)(maxDurability - curDurability) / maxDurability);
        return (int)value;
    }
}

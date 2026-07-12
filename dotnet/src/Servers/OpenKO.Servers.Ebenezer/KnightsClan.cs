namespace OpenKO.Servers.Ebenezer;

/// <summary>_KNIGHTS_USER (one member slot in the clan cache).</summary>
public struct KnightsUserSlot
{
    public byte Used;
    public string UserName;
}

/// <summary>Port of <c>CKnights</c> (Server/Ebenezer/Knights.h).</summary>
public sealed class KnightsClan
{
    public const int MaxClan = 36;      // MAX_CLAN
    public const byte ClanType = 1;     // CLAN_TYPE
    public const byte KnightsType = 2;  // KNIGHTS_TYPE

    public short Index;

    /// <summary>m_byFlag: 1 clan, 2 knights.</summary>
    public byte Flag;

    public byte Nation;

    /// <summary>m_byGrade (1..5, derived from the points).</summary>
    public byte Grade = 5;

    public byte Ranking;

    public string Name = string.Empty;

    public short Members;

    public string Chief = string.Empty;
    public string ViceChief1 = string.Empty;
    public string ViceChief2 = string.Empty;
    public string ViceChief3 = string.Empty;

    public long Money;
    public short AllianceKnights;
    public short MarkVersion;
    public short Cape;
    public short Domination;
    public int Points;

    /// <summary>m_arKnightsUser.</summary>
    public readonly KnightsUserSlot[] Users = CreateSlots();

    private static KnightsUserSlot[] CreateSlots()
    {
        var slots = new KnightsUserSlot[MaxClan];
        for (int i = 0; i < MaxClan; i++)
            slots[i].UserName = string.Empty;
        return slots;
    }
}

namespace OpenKO.Data.Models;

/// <summary>ITEM table subset the DB agent needs (Num, Countable).</summary>
public sealed record ItemRow(int Num, byte Countable)
{
    public bool IsCountable => Countable != 0;
}

/// <summary>KNIGHTS table (IDNum, Nation, Ranking, IDName, Members, Points).</summary>
public sealed record KnightsInfo(short Id, byte Nation, string Name, short Members, uint Points, byte Ranking);

/// <summary>One LOAD_KNIGHTS_MEMBERS row.</summary>
public sealed record KnightsMember(string CharId, byte Fame, byte Level, short Class);

/// <summary>One entry of the knights ranking list (KNIGHTS_ALLLIST_REQ).</summary>
public sealed record KnightsRankingEntry(short Id, uint Points, byte Ranking);

/// <summary>LOAD_CHAR_INFO result (character summary + the 8 visible equip items).</summary>
public sealed record CharInfo(
    string CharId,
    byte Race,
    short Class,
    byte Level,
    byte Face,
    byte HairColor,
    byte Zone,
    IReadOnlyList<(int ItemId, short Duration)> VisibleEquipment)
{
    /// <summary>An empty/unset character slot (sent with zeroed fields, like the C++).</summary>
    public static CharInfo Empty(string charId) => new(
        charId, 0, 0, 0, 0, 0, 0,
        new (int, short)[8]);
}

/// <summary>LOAD_ACCOUNT_CHARID result: the three character slots of an account.</summary>
public sealed record AllCharIds(string CharId1, string CharId2, string CharId3);

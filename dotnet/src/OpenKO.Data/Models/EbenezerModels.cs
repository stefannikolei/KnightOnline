namespace OpenKO.Data.Models;

/// <summary>
/// COEFFICIENT table (deps/db-models Full/model): per-class weapon and stat
/// coefficients used by CUser::SetUserAbility and validated on character creation.
/// </summary>
public sealed record Coefficient
{
    /// <summary>Column [sClass].</summary>
    public required short ClassId { get; init; }

    public required double ShortSword { get; init; }

    public required double Sword { get; init; }

    public required double Axe { get; init; }

    public required double Club { get; init; }

    public required double Spear { get; init; }

    public required double Pole { get; init; }

    public required double Staff { get; init; }

    public required double Bow { get; init; }

    /// <summary>Column [Hp].</summary>
    public required double HitPoint { get; init; }

    /// <summary>Column [Mp].</summary>
    public required double ManaPoint { get; init; }

    public required double Sp { get; init; }

    /// <summary>Column [Ac].</summary>
    public required double Armor { get; init; }

    /// <summary>Column [Hitrate].</summary>
    public required double HitRate { get; init; }

    /// <summary>Column [Evasionrate].</summary>
    public required double EvasionRate { get; init; }
}

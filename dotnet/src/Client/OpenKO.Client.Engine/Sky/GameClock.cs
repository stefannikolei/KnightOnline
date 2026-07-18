namespace OpenKO.Client.Engine.Sky;

/// <summary>
/// The authoritative game clock the sky is driven from once a WIZ_TIME push has
/// arrived (CN3SkyMng::SetGameTime + GetGameTime): the server sends the exact
/// hour+minute, which seeds a 0..1 day-fraction; between packets the fraction
/// advances with real time scaled by <see cref="DayNightCycle.TimeRealPerGame"/>
/// (the game runs ~10× real, so a full day of 86400 game-seconds elapses in
/// 8640 real-seconds). Pure and headless-testable; the device layer reads
/// <see cref="DayFraction"/> each frame.
/// </summary>
public sealed class GameClock
{
    private float _dayFraction;

    /// <summary>True once <see cref="SetFromServer"/> has been called at least once.</summary>
    public bool HasServerTime { get; private set; }

    /// <summary>The current game-day fraction (0..1); noon ≈ 0.5, midnight = 0.</summary>
    public float DayFraction => _dayFraction;

    /// <summary>
    /// Anchor the clock to a server hour+minute (CN3SkyMng::SetCheckGameTime):
    /// resets the day-fraction to exactly that time-of-day.
    /// </summary>
    public void SetFromServer(int hour, int minute)
    {
        _dayFraction = DayNightCycle.DayFraction(hour, minute);
        HasServerTime = true;
    }

    /// <summary>
    /// Advance the day-fraction by <paramref name="realDeltaSeconds"/> of real
    /// time scaled to game time (×TIME_REAL_PER_GAME), wrapping at the day
    /// boundary. A no-op-ish call before any server time still advances, but
    /// callers should ignore <see cref="DayFraction"/> until
    /// <see cref="HasServerTime"/>.
    /// </summary>
    public void Advance(float realDeltaSeconds)
    {
        float gameSeconds = realDeltaSeconds * DayNightCycle.TimeRealPerGame;
        _dayFraction += gameSeconds / DayNightCycle.SecondsPerDay;
        _dayFraction -= MathF.Floor(_dayFraction); // wrap into [0,1)
    }
}

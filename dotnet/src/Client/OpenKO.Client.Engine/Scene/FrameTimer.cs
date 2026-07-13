namespace OpenKO.Client.Engine.Scene;

/// <summary>
/// The C++ frame clock (CN3Eng::Present): s_fSecPerFrm is the measured
/// per-frame delta, snapped to 30 fps when implausible (&lt;= 1 ms or &gt;= 1 s).
/// All animation code consumes SecPerFrame exactly like the C++.
/// </summary>
public sealed class FrameTimer
{
    public const float FallbackSecPerFrame = 0.033333f;

    public float SecPerFrame { get; private set; } = FallbackSecPerFrame;

    public float FramesPerSecond => 1f / SecPerFrame;

    /// <summary>Total game time in seconds (for double-click timing etc.).</summary>
    public double TotalSeconds { get; private set; }

    public void Tick(double elapsedSeconds)
    {
        TotalSeconds += elapsedSeconds;

        var delta = (float)elapsedSeconds;
        if (delta <= 0.001f || delta >= 1.0f)
            delta = FallbackSecPerFrame; // C++ snaps to 30 fps
        SecPerFrame = delta;
    }
}

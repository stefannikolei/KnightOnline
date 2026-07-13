using Microsoft.Xna.Framework;

namespace OpenKO.Client.Viewer;

/// <summary>
/// Stage-6.1 placeholder: clears with a slowly pulsing color so a running
/// window proves loop, timer and input plumbing work.
/// </summary>
public sealed class EmptyScene : IScene
{
    private float _phase;

    public string Name => "Leere Szene";

    public void Load(ViewerContext context)
    {
    }

    public void Tick(ViewerContext context)
    {
        _phase += context.Timer.SecPerFrame;
    }

    public void Render(ViewerContext context)
    {
        float pulse = 0.5f + 0.5f * MathF.Sin(_phase);
        context.Device.Clear(new Color(0.1f * pulse, 0.1f, 0.25f));
    }

    public void Unload()
    {
    }
}

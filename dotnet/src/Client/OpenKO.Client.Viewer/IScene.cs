using Microsoft.Xna.Framework.Graphics;
using OpenKO.Client.Engine.Input;
using OpenKO.Client.Engine.Scene;

namespace OpenKO.Client.Viewer;

/// <summary>Shared services handed to every viewer scene.</summary>
public sealed class ViewerContext
{
    public required GraphicsDevice Device { get; init; }

    /// <summary>Root of the client asset corpus (Client/Data), or null.</summary>
    public string? DataPath { get; init; }

    public required FrameTimer Timer { get; init; }

    public required InputState Input { get; init; }
}

/// <summary>
/// One debug scene — the viewer analog of a CGameProcedure: it owns its
/// frame (clear + draw) like the C++ procedures own BeginScene/Present.
/// </summary>
public interface IScene
{
    string Name { get; }

    void Load(ViewerContext context);

    void Tick(ViewerContext context);

    void Render(ViewerContext context);

    void Unload();
}

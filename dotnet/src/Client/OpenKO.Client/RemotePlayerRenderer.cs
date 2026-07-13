using Microsoft.Xna.Framework.Graphics;
using OpenKO.Client.Assets.Player;
using OpenKO.Client.Engine.Interop;
using OpenKO.Client.Engine.Objects;
using OpenKO.Client.Engine.Scene;
using OpenKO.Client.Game.World;
using NumVector3 = System.Numerics.Vector3;

namespace OpenKO.Client;

/// <summary>
/// The client-side <c>CPlayerOtherMgr</c>: keeps a runtime-assembled
/// <see cref="ChrRenderer"/> per region-visible remote player (keyed by socket
/// id), assembling each on first sight from its race/face/hair/equipment and
/// gliding it toward the streamed WIZ_MOVE target. Ticks and renders them all.
/// </summary>
public sealed class RemotePlayerRenderer(CharacterFactory factory)
{
    private sealed class Entry
    {
        public ChrRenderer? Renderer { get; init; }

        public NumVector3 RenderPos { get; set; }
    }

    /// <summary>Interpolation glide speed (m/s) — the default player run speed.</summary>
    private const float GlideSpeed = 8f;

    private readonly Dictionary<short, Entry> _entries = [];
    private readonly List<short> _stale = [];

    public void SyncAndRender(
        GraphicsDevice device, BasicEffect effect, N3EngineCamera camera,
        FrameTimer timer, WorldEntities world, float dt)
    {
        // Assemble newcomers, glide the rest toward their roster position.
        foreach ((short id, RemotePlayer player) in world.Players)
        {
            if (!_entries.TryGetValue(id, out Entry? entry))
            {
                entry = CreateEntry(player);
                _entries[id] = entry;
            }

            if (entry.Renderer is not { HasSkeleton: true } renderer)
                continue;

            var target = new NumVector3(player.X, player.Y, player.Z);
            entry.RenderPos = EntityInterpolator.MoveTowards(entry.RenderPos, target, GlideSpeed, dt, out _);

            renderer.Chr.Position = entry.RenderPos;
            renderer.Chr.Rotation = System.Numerics.Quaternion.CreateFromAxisAngle(
                NumVector3.UnitY, player.Direction * 0.01f); // WIZ_ROTATE yaw*100

            renderer.Tick(camera, timer);
            renderer.Render(device, effect);
        }

        // Drop players that left the region (WIZ_USER_INOUT out).
        _stale.Clear();
        foreach (short id in _entries.Keys)
            if (!world.Players.ContainsKey(id))
                _stale.Add(id);
        foreach (short id in _stale)
            _entries.Remove(id);
    }

    private Entry CreateEntry(RemotePlayer player)
    {
        ChrRenderer? renderer = factory.CreatePlayer(
            (KoRace)player.Race, player.Face, player.Hair, player.Items);
        return new Entry { Renderer = renderer, RenderPos = new NumVector3(player.X, player.Y, player.Z) };
    }
}

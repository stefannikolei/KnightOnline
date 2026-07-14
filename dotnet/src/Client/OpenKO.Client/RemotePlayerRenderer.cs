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
/// <see cref="ChrRenderer"/> per region-visible remote player and NPC (keyed by
/// their server id), each assembled on first sight (players from their
/// race/face/hair/equipment, NPCs from NPC_Looks), glided toward its streamed
/// move target and ticked/rendered every frame; entities that leave the region
/// are dropped.
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

    private readonly Dictionary<short, Entry> _players = [];
    private readonly Dictionary<short, Entry> _npcs = [];
    private readonly List<short> _stale = [];

    public void SyncAndRender(
        GraphicsDevice device, BasicEffect effect, N3EngineCamera camera,
        FrameTimer timer, WorldEntities world, float dt)
    {
        // Remote players — Direction is the WIZ_ROTATE yaw*100.
        foreach ((short id, RemotePlayer p) in world.Players)
        {
            Entry entry = Ensure(_players, id, () => factory.CreatePlayer(
                (KoRace)p.Race, p.Face, p.Hair, p.Items), new NumVector3(p.X, p.Y, p.Z));
            Render(device, effect, camera, timer, entry, new NumVector3(p.X, p.Y, p.Z), p.Direction * 0.01f, dt);
        }

        DropMissing(_players, id => world.Players.ContainsKey(id));

        // NPCs / monsters — Direction is a 0..255 compass byte.
        foreach ((short id, NpcEntity n) in world.Npcs)
        {
            Entry entry = Ensure(_npcs, id, () => factory.CreateNpc(n.ProtoId),
                new NumVector3(n.X, n.Y, n.Z));
            float yaw = n.Direction / 256f * MathF.Tau;
            Render(device, effect, camera, timer, entry, new NumVector3(n.X, n.Y, n.Z), yaw, dt);
        }

        DropMissing(_npcs, id => world.Npcs.ContainsKey(id));
    }

    private static Entry Ensure(
        Dictionary<short, Entry> map, short id, Func<ChrRenderer?> create, NumVector3 spawn)
    {
        if (!map.TryGetValue(id, out Entry? entry))
        {
            entry = new Entry { Renderer = create(), RenderPos = spawn };
            map[id] = entry;
        }

        return entry;
    }

    private static void Render(
        GraphicsDevice device, BasicEffect effect, N3EngineCamera camera, FrameTimer timer,
        Entry entry, NumVector3 target, float yaw, float dt)
    {
        if (entry.Renderer is not { HasSkeleton: true } renderer)
            return;

        entry.RenderPos = EntityInterpolator.MoveTowards(entry.RenderPos, target, GlideSpeed, dt, out _);
        renderer.Chr.Position = entry.RenderPos;
        renderer.Chr.Rotation = System.Numerics.Quaternion.CreateFromAxisAngle(NumVector3.UnitY, yaw);

        renderer.Tick(camera, timer);
        renderer.Render(device, effect);
    }

    private void DropMissing(Dictionary<short, Entry> map, Func<short, bool> present)
    {
        _stale.Clear();
        foreach (short id in map.Keys)
            if (!present(id))
                _stale.Add(id);
        foreach (short id in _stale)
            map.Remove(id);
    }
}

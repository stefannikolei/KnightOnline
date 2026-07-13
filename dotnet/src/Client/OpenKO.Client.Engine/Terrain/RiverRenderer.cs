using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OpenKO.Client.Assets;
using OpenKO.Client.Engine.Interop;
using OpenKO.Client.Engine.IO;
using OpenKO.Client.Engine.Rendering;
using OpenKO.Client.Engine.Scene;

namespace OpenKO.Client.Engine.Terrain;

/// <summary>
/// Device layer for the water (CN3River): each river strip is drawn as a
/// two-texture modulate — the animated caustic frame (misc\river\caustNN.dxt)
/// modulated by the per-river wave overlay — alpha-blended with mip filtering
/// off, exactly like <c>CN3River::Render</c>. DualTextureEffect is Modulate2X,
/// so the vertex-colour RGB is halved (the <see cref="DualTextureCompensation"/>
/// trick, alpha kept). UV scroll, the 32-frame caustic cycle and the
/// <c>UpdateWaterPositions</c> wave bob all run in <see cref="Tick"/>.
/// </summary>
public sealed class RiverRenderer : IDisposable
{
    private const int MaxRiverTex = 32; // MAX_RIVER_TEX

    private sealed class RiverState
    {
        public required N3VertexRiver[] Vertices { get; init; }

        public required short[] Indices { get; init; }

        public required RiverWaveDiff[] Diff { get; init; }

        public required RiverVertex[] RenderBuffer { get; init; }

        public Texture2D? Wave { get; init; }

        public System.Numerics.Vector3 Center { get; init; }

        public float Radius { get; init; }

        public bool Visible { get; set; }
    }

    private readonly List<RiverState> _rivers = [];
    private readonly List<Texture2D> _ownedTextures = [];
    private readonly Texture2D?[] _caustics = new Texture2D?[MaxRiverTex];
    private readonly DualTextureEffect _dual;
    private float _texIndex;
    private float _waveTimer;

    public RiverRenderer(GraphicsDevice device, N3Terrain terrain, KoPathResolver resolver)
    {
        _dual = new DualTextureEffect(device) { VertexColorEnabled = true };

        for (int i = 0; i < MaxRiverTex; i++)
            _caustics[i] = LoadTexture(device, resolver, $"misc\\river\\caust{i:00}.dxt");

        foreach (N3RiverInfo info in terrain.Rivers)
        {
            if (info.Vertices.Length == 0)
                continue;

            Texture2D? wave = string.IsNullOrEmpty(info.TextureName)
                ? null
                : LoadTexture(device, resolver, $"misc\\river\\{info.TextureName}");

            (System.Numerics.Vector3 center, float radius) = Bounds(info.Vertices);
            _rivers.Add(new RiverState
            {
                Vertices = (N3VertexRiver[])info.Vertices.Clone(),
                Indices = RiverVertexBuilder.BuildIndices(info.IndexCount),
                Diff = RiverVertexBuilder.BuildWaveDiff(info.Vertices.Length),
                RenderBuffer = new RiverVertex[info.Vertices.Length],
                Wave = wave,
                Center = center,
                Radius = radius,
            });
        }
    }

    public int RiverCount => _rivers.Count;

    /// <summary>CN3River::Tick — cull, UV scroll, caustic frame, wave bob.</summary>
    public void Tick(N3EngineCamera camera, float secPerFrame)
    {
        foreach (RiverState river in _rivers)
        {
            river.Visible = !camera.Frustum.IsOutOfFrustum(river.Center, river.Radius);
            if (!river.Visible)
                continue;

            for (int j = 0; j < river.Vertices.Length; j++)
            {
                river.Vertices[j].V += 0.01f * secPerFrame;
                river.Vertices[j].V2 += 0.01f * secPerFrame;
            }
        }

        _texIndex += secPerFrame * 15.0f;
        if (_texIndex >= 32.0f)
            _texIndex -= 32.0f;

        _waveTimer += secPerFrame;
        if (_waveTimer > 0.1f)
        {
            _waveTimer = 0f;
            UpdateWaterPositions();
        }
    }

    private void UpdateWaterPositions()
    {
        float[] yDelta = [];
        foreach (RiverState river in _rivers)
        {
            if (yDelta.Length < river.Vertices.Length)
                yDelta = new float[river.Vertices.Length];

            RiverVertexBuilder.StepWave(river.Diff, yDelta);
            for (int j = 0; j < river.Vertices.Length; j++)
                river.Vertices[j].Position.Y += yDelta[j];
        }
    }

    public void Render(GraphicsDevice device, N3EngineCamera camera)
    {
        if (_rivers.Count == 0)
            return;

        int frame = (int)_texIndex;
        if (frame < 0 || frame >= MaxRiverTex || _caustics[frame] == null)
            return;

        _dual.World = Matrix.Identity;
        _dual.View = camera.View.ToXna();
        _dual.Projection = camera.Projection.ToXna();

        device.BlendState = BlendState.NonPremultiplied; // SRCALPHA / INVSRCALPHA
        device.DepthStencilState = DepthStencilState.DepthRead; // water does not write Z
        device.RasterizerState = RasterizerState.CullCounterClockwise;
        // MIPFILTER NONE on both stages (CN3River::Render).
        device.SamplerStates[0] = SamplerState.LinearWrap;
        device.SamplerStates[1] = SamplerState.LinearWrap;

        _dual.Texture = _caustics[frame];

        foreach (RiverState river in _rivers)
        {
            if (!river.Visible)
                continue;

            _dual.Texture2 = river.Wave ?? _caustics[frame];
            FillRenderBuffer(river);

            foreach (EffectPass pass in _dual.CurrentTechnique.Passes)
            {
                pass.Apply();
                device.DrawUserIndexedPrimitives(
                    PrimitiveType.TriangleList, river.RenderBuffer, 0, river.RenderBuffer.Length,
                    river.Indices, 0, river.Indices.Length / 3, RiverVertex.VertexDeclaration);
            }
        }

        device.DepthStencilState = DepthStencilState.Default;
        device.BlendState = BlendState.Opaque;
    }

    private static void FillRenderBuffer(RiverState river)
    {
        for (int i = 0; i < river.Vertices.Length; i++)
        {
            N3VertexRiver v = river.Vertices[i];
            Color color = ColorInterop.FromArgb(v.Color);
            // Modulate2X compensation: halve RGB, keep alpha (DualTexture doubles RGB).
            var compensated = new Color(
                (byte)(color.R * DualTextureCompensation.DiffuseRgbScale),
                (byte)(color.G * DualTextureCompensation.DiffuseRgbScale),
                (byte)(color.B * DualTextureCompensation.DiffuseRgbScale),
                color.A);
            river.RenderBuffer[i] = new RiverVertex(
                v.Position.ToXna(), compensated, new Vector2(v.U, v.V), new Vector2(v.U2, v.V2));
        }
    }

    private static (System.Numerics.Vector3 Center, float Radius) Bounds(N3VertexRiver[] verts)
    {
        float stX = verts[0].Position.X, enX = verts[0].Position.X;
        float stZ = verts[0].Position.Z, enZ = verts[0].Position.Z;
        foreach (N3VertexRiver v in verts)
        {
            stX = MathF.Min(stX, v.Position.X);
            enX = MathF.Max(enX, v.Position.X);
            stZ = MathF.Min(stZ, v.Position.Z);
            enZ = MathF.Max(enZ, v.Position.Z);
        }

        var center = new System.Numerics.Vector3(
            ((enX - stX) / 2f) + stX, verts[0].Position.Y, ((enZ - stZ) / 2f) + stZ);
        float radius = ((enX - stX) > (enZ - stZ) ? (enX - stX) : (enZ - stZ)) * 2f;
        return (center, radius);
    }

    private Texture2D? LoadTexture(GraphicsDevice device, KoPathResolver resolver, string koPath)
    {
        string? full = resolver.Resolve(koPath);
        if (full == null)
            return null;

        try
        {
            var n3 = new N3Texture();
            n3.LoadFromFile(full);
            Texture2D texture = TextureFactory.FromN3Texture(device, n3);
            _ownedTextures.Add(texture);
            return texture;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void Dispose()
    {
        foreach (Texture2D texture in _ownedTextures)
            texture.Dispose();
        _ownedTextures.Clear();
        _rivers.Clear();
        _dual.Dispose();
    }
}

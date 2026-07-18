using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OpenKO.Client.Assets;
using OpenKO.Client.Engine.Interop;
using OpenKO.Client.Engine.Rendering;
using OpenKO.Client.Engine.Scene;

namespace OpenKO.Client.Engine.Fx;

/// <summary>
/// Device layer for the effect parts (the <c>Render</c> side of the N3FX parts).
/// It consumes the pure simulators' finished vertex arrays and issues the alpha
/// draws the C++ queued into <c>s_AlphaMgr</c>: camera-facing particle/billboard
/// quads and ground-hugging bottom-board fans, each with its own
/// source/destination blend taken from the part's render flags
/// (<see cref="RenderStateMapper"/>), Z-write off, double-sided. Not unit-tested
/// (no GPU in CI); all the maths lives in the pure builders.
/// <para>
/// Textures are resolved through the injected <see cref="TextureProvider"/>
/// callback (part + frame index → the animation frame), so this file never
/// touches the filesystem. Mesh/shape parts render through the existing PMesh
/// mesh renderers and are dispatched by the game FX manager (slice 9.10c).
/// </para>
/// </summary>
public sealed class FxRenderer : IDisposable
{
    private static readonly short[] QuadFanIndices = [0, 1, 2, 0, 2, 3];

    private readonly GraphicsDevice _device;
    private readonly BasicEffect _effect;
    private readonly N3VertexXyzColorT1[] _quad = new N3VertexXyzColorT1[FxParticleVertexBuilder.VerticesPerParticle];
    private VertexPositionColorTexture[] _scratch = new VertexPositionColorTexture[256];
    private short[] _indexScratch = new short[384];

    /// <param name="textureProvider">
    /// part + texture-frame index → the frame texture (or null). Wire it with
    /// <c>TextureFactory</c> at the host.
    /// </param>
    /// <param name="shapeProvider">
    /// mesh-part descriptor → the resolved <see cref="FxShapeInstance"/> (or null).
    /// Wire it at the host over the FX shape/pmesh loaders (cached by shape file
    /// name); leave null to skip mesh-part rendering (particles/boards still draw).
    /// </param>
    public FxRenderer(
        GraphicsDevice device,
        Func<N3FXPartBase, int, Texture2D?> textureProvider,
        Func<N3FXPartMesh, FxShapeInstance?>? shapeProvider = null)
    {
        _device = device;
        TextureProvider = textureProvider;
        ShapeProvider = shapeProvider;
        _effect = new BasicEffect(device)
        {
            VertexColorEnabled = true,
            TextureEnabled = true,
            LightingEnabled = false,
        };
    }

    public Func<N3FXPartBase, int, Texture2D?> TextureProvider { get; set; }

    /// <summary>mesh-part descriptor → resolved shape geometry (null = mesh parts skipped).</summary>
    public Func<N3FXPartMesh, FxShapeInstance?>? ShapeProvider { get; set; }

    /// <summary>
    /// Draws every live part of a bundle. Call after the opaque world; FX is
    /// additive/alpha and writes no depth.
    /// </summary>
    public void Render(FxBundleSimulator bundle, N3EngineCamera camera)
    {
        if (bundle.State == FxBundleState.Dead)
            return;

        System.Numerics.Matrix4x4 viewInverse = System.Numerics.Matrix4x4.Invert(camera.View, out var inv)
            ? inv
            : System.Numerics.Matrix4x4.Identity;
        viewInverse.Translation = System.Numerics.Vector3.Zero; // m_mtxVI.PosSet(0,0,0)

        _effect.World = Matrix.Identity;
        _effect.View = camera.View.ToXna();
        _effect.Projection = camera.Projection.ToXna();

        _device.DepthStencilState = DepthStencilState.DepthRead; // FX does not write Z
        _device.RasterizerState = RasterizerState.CullNone; // RF_DOUBLESIDED
        _device.SamplerStates[0] = SamplerState.LinearClamp;

        FxBundleContext context = bundle.Context;
        foreach (FxBundlePartRuntime slot in bundle.Parts)
        {
            if (slot.Part.State is FxPartLifeState.Dead or FxPartLifeState.Ready)
                continue;

            switch (slot.Part)
            {
                case FxParticleSimulator particles:
                    RenderParticles(particles, viewInverse);
                    break;
                case FxBillboardSimulator billboard:
                    RenderBillboard(billboard, viewInverse, camera, context);
                    break;
                case FxBottomBoardSimulator bottom:
                    RenderBottomBoard(bottom);
                    break;
                case FxMeshSimulator mesh:
                    RenderMesh(mesh);
                    break;
            }
        }

        _effect.World = Matrix.Identity;
        _device.DepthStencilState = DepthStencilState.Default;
        _device.BlendState = BlendState.Opaque;
        _device.RasterizerState = RasterizerState.CullCounterClockwise;
    }

    /// <summary>
    /// CN3FXPartMesh::Render → CN3FXShape/CN3FXSPart::Render: draw the resolved
    /// shape's parts under the sim's parent matrix. The FX pass is a separate
    /// additive/no-Z-write layer here (the same pipeline particles/boards use), so
    /// both alpha and opaque shape parts draw inline with the FX part's blend, tinted
    /// by the fade colour (m_dwCurrColor). Queuing alpha parts into a shared
    /// AlphaManager to sort against the world alpha is deferred — the client's FX
    /// pass carries no such queue.
    /// </summary>
    private void RenderMesh(FxMeshSimulator mesh)
    {
        FxShapeInstance? shape = ShapeProvider?.Invoke(mesh.Descriptor);
        if (shape == null || shape.Parts.Count == 0)
            return;

        N3FXPartMesh desc = mesh.Descriptor;
        _device.BlendState = RenderStateMapper.GetBlendState(desc.SrcBlend, desc.DestBlend);

        Microsoft.Xna.Framework.Color color = ColorInterop.FromArgb(mesh.CurrColor);

        foreach (FxShapeInstance.Part part in shape.Parts)
        {
            // CN3FXSPart::Tick — m_WorldMtx = Translation(pivot) * m_mtxParent.
            System.Numerics.Matrix4x4 world = System.Numerics.Matrix4x4.CreateTranslation(part.Pivot)
                * mesh.ParentMatrix;

            int vertCount = part.Vertices.Length;
            int indexCount = part.Indices.Length - part.Indices.Length % 3;
            if (vertCount == 0 || indexCount < 3)
                continue;

            EnsureScratch(vertCount);
            for (int i = 0; i < vertCount; i++)
            {
                ref readonly N3VertexXyzColorT1 v = ref part.Vertices[i];
                _scratch[i] = new VertexPositionColorTexture(v.Position.ToXna(), color, new Vector2(v.Tu, v.Tv));
            }

            EnsureIndexScratch(indexCount);
            for (int i = 0; i < indexCount; i++)
                _indexScratch[i] = (short)part.Indices[i];

            Texture2D? texture = CurrentFrameTexture(part, mesh.CurrFrame);
            _effect.Texture = texture;
            _effect.TextureEnabled = texture != null;
            _effect.World = world.ToXna();

            foreach (EffectPass pass in _effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                _device.DrawUserIndexedPrimitives(
                    PrimitiveType.TriangleList, _scratch, 0, vertCount, _indexScratch, 0, indexCount / 3);
            }
        }

        _effect.World = Matrix.Identity;
    }

    private static Texture2D? CurrentFrameTexture(FxShapeInstance.Part part, float frame)
    {
        int count = part.Textures.Length;
        if (count == 0)
            return null;
        int index = (int)MathF.Max(0f, frame) % count;
        return part.Textures[index];
    }

    private void RenderParticles(FxParticleSimulator particles, in System.Numerics.Matrix4x4 viewInverse)
    {
        if (particles.AliveCount == 0)
            return;

        N3FXPartParticles desc = particles.Descriptor;
        _device.BlendState = RenderStateMapper.GetBlendState(desc.SrcBlend, desc.DestBlend);

        // Each particle can be on a different animation frame, so draw per quad.
        foreach (FxRuntimeParticle p in particles.AliveParticles)
        {
            if (p.TexIndex >= desc.NumTex)
                continue;

            FxParticleVertexBuilder.BuildParticle(
                p, viewInverse, desc.TexRotateVelocity, desc.ScaleVelX, desc.ScaleVelY, _quad, 0);

            _effect.Texture = TextureProvider(desc, p.TexIndex);
            _effect.TextureEnabled = _effect.Texture != null;
            DrawQuadFan(_quad);
        }
    }

    private void RenderBillboard(
        FxBillboardSimulator billboard,
        in System.Numerics.Matrix4x4 viewInverse,
        N3EngineCamera camera,
        FxBundleContext context)
    {
        N3FXPartBillBoard desc = billboard.Descriptor;
        if (billboard.TexIndex >= desc.NumTex)
            return;

        int vertCount = desc.Num * 4;
        var verts = new N3VertexXyzColorT1[vertCount];
        billboard.Build(viewInverse, camera.Eye, camera.At, camera.NearPlane, context, verts);

        _device.BlendState = RenderStateMapper.GetBlendState(desc.SrcBlend, desc.DestBlend);
        _effect.Texture = TextureProvider(desc, billboard.TexIndex);
        _effect.TextureEnabled = _effect.Texture != null;

        for (int b = 0; b < desc.Num; b++)
            DrawQuadFan(verts.AsSpan(b * 4, 4));
    }

    private void RenderBottomBoard(FxBottomBoardSimulator bottom)
    {
        N3FXPartBottomBoard desc = bottom.Descriptor;
        if (bottom.TexIndex >= desc.NumTex)
            return;

        _device.BlendState = RenderStateMapper.GetBlendState(desc.SrcBlend, desc.DestBlend);
        _effect.Texture = TextureProvider(desc, bottom.TexIndex);
        _effect.TextureEnabled = _effect.Texture != null;

        int n = FxBottomBoardSimulator.VertexCount;
        EnsureScratch(n);
        for (int i = 0; i < n; i++)
            _scratch[i] = ToDeviceVertex(bottom.Vertices[i]);

        short[] indices = FxBottomBoardSimulator.FanIndices();
        foreach (EffectPass pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            _device.DrawUserIndexedPrimitives(
                PrimitiveType.TriangleList, _scratch, 0, n, indices, 0, indices.Length / 3);
        }
    }

    private void DrawQuadFan(ReadOnlySpan<N3VertexXyzColorT1> quad)
    {
        EnsureScratch(4);
        for (int i = 0; i < 4; i++)
            _scratch[i] = ToDeviceVertex(quad[i]);

        foreach (EffectPass pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            _device.DrawUserIndexedPrimitives(
                PrimitiveType.TriangleList, _scratch, 0, 4, QuadFanIndices, 0, 2);
        }
    }

    private static VertexPositionColorTexture ToDeviceVertex(in N3VertexXyzColorT1 v) => new(
        v.Position.ToXna(), ColorInterop.FromArgb(v.Color), new Vector2(v.Tu, v.Tv));

    private void EnsureScratch(int count)
    {
        if (_scratch.Length < count)
            _scratch = new VertexPositionColorTexture[Math.Max(count, _scratch.Length * 2)];
    }

    private void EnsureIndexScratch(int count)
    {
        if (_indexScratch.Length < count)
            _indexScratch = new short[Math.Max(count, _indexScratch.Length * 2)];
    }

    public void Dispose() => _effect.Dispose();
}

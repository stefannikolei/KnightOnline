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

    /// <param name="textureProvider">
    /// part + texture-frame index → the frame texture (or null). Wire it with
    /// <c>TextureFactory</c> at the host.
    /// </param>
    public FxRenderer(GraphicsDevice device, Func<N3FXPartBase, int, Texture2D?> textureProvider)
    {
        _device = device;
        TextureProvider = textureProvider;
        _effect = new BasicEffect(device)
        {
            VertexColorEnabled = true,
            TextureEnabled = true,
            LightingEnabled = false,
        };
    }

    public Func<N3FXPartBase, int, Texture2D?> TextureProvider { get; set; }

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

                // Mesh parts render through the PMesh renderers (slice 9.10c).
            }
        }

        _device.DepthStencilState = DepthStencilState.Default;
        _device.BlendState = BlendState.Opaque;
        _device.RasterizerState = RasterizerState.CullCounterClockwise;
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

    public void Dispose() => _effect.Dispose();
}

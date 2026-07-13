using Microsoft.Xna.Framework.Graphics;
using OpenKO.Client.Assets;
using OpenKO.Client.Engine.Interop;
using OpenKO.Client.Engine.Rendering;
using OpenKO.Client.Engine.Scene;

namespace OpenKO.Client.Engine.Objects;

/// <summary>
/// Port of the CN3Shape/CN3SPart runtime: per-part frustum culling, LOD,
/// texture animation, RF_BOARD_Y billboarding and RF_WINDY wobble in Tick;
/// opaque parts draw directly, RF_ALPHABLENDING parts defer to the
/// <see cref="AlphaManager"/> — exactly the C++ split.
/// </summary>
public sealed class ShapeRenderer
{
    private sealed class PartState
    {
        public required N3SPart Part { get; init; }

        public required MaterialPlan Plan { get; init; }

        public PMeshInstanceRenderer? Renderer { get; init; }

        public N3PMesh? Mesh { get; init; }

        public Texture2D?[] Textures { get; init; } = [];

        public System.Numerics.Matrix4x4 Matrix { get; set; } = System.Numerics.Matrix4x4.Identity;

        public float TexIndex { get; set; }

        public bool OutOfCameraRange { get; set; }

        public WindyState? Windy { get; init; }
    }

    private readonly N3Shape _shape;
    private readonly List<PartState> _parts = [];

    public ShapeRenderer(N3Shape shape, PMeshCache meshes, TextureCache textures, Random random)
    {
        _shape = shape;
        shape.ReCalcMatrix();

        foreach (N3SPart part in shape.Parts)
        {
            N3PMesh? mesh = meshes.Get(part.MeshFileName);
            MaterialPlan plan = MaterialBinder.Plan(part.Material, hasOverlayTexture: false);
            _parts.Add(new PartState
            {
                Part = part,
                Plan = plan,
                Mesh = mesh,
                Renderer = mesh != null ? new PMeshInstanceRenderer(mesh) : null,
                Textures = [.. part.TexFileNames.Select(textures.Get)],
                Windy = plan.Windy ? new WindyState(random) : null,
            });
        }
    }

    public N3Shape Shape => _shape;

    /// <summary>True after Tick when the whole shape was culled (m_bDontRender).</summary>
    public bool DontRender { get; private set; }

    /// <summary>CN3Shape::Tick + CN3SPart::Tick.</summary>
    public void Tick(N3EngineCamera camera, FrameTimer timer)
    {
        // Largest scale component loosens the culling (C++).
        float scale = MathF.Max(_shape.Scale.X, MathF.Max(_shape.Scale.Y, _shape.Scale.Z));

        (System.Numerics.Vector3 shapeMin, System.Numerics.Vector3 shapeMax, float radius) = ShapeBounds();
        System.Numerics.Vector3 center = (shapeMin + shapeMax) * 0.5f;

        float dist = (_shape.Position - camera.Eye).Length();
        if (dist > camera.FarPlane + radius * scale * 2f || camera.Frustum.IsOutOfFrustum(center, radius))
        {
            DontRender = true;
            return;
        }

        DontRender = false;

        foreach (PartState state in _parts)
        {
            if (state.Mesh == null || state.Renderer == null)
                continue;

            // CN3SPart::ReCalcMatrix: pivot translation into the parent.
            System.Numerics.Matrix4x4 m = System.Numerics.Matrix4x4.Identity;
            m.Translation = state.Part.Pivot;
            m *= _shape.Matrix;
            state.Matrix = m;

            System.Numerics.Vector3 partCenter =
                System.Numerics.Vector3.Transform((state.Mesh.Min + state.Mesh.Max) * 0.5f, state.Matrix);
            if (camera.Frustum.IsOutOfFrustum(partCenter, state.Mesh.Radius * scale))
            {
                state.OutOfCameraRange = true;
                continue;
            }

            state.OutOfCameraRange = false;

            float partDist = (partCenter - camera.Eye).Length();
            state.Renderer.SetLod(partDist * camera.Fov / scale);

            // Texture animation (only with more than one frame, like the C++).
            if (state.Textures.Length > 1)
            {
                state.TexIndex += timer.SecPerFrame * state.Part.TexFps;
                if (state.TexIndex >= state.Textures.Length)
                    state.TexIndex %= state.Textures.Length;
            }

            if (state.Plan.BoardY)
            {
                state.Matrix = BillboardMath.BoardY(
                    state.Part.Pivot, _shape.Matrix, _shape.Rotation, camera.Eye);
            }

            if (state.Windy != null)
            {
                System.Numerics.Matrix4x4? windy =
                    state.Windy.Tick(timer.SecPerFrame, state.Part.Pivot, _shape.Matrix);
                if (windy.HasValue)
                    state.Matrix = windy.Value;
            }
        }
    }

    /// <summary>CN3Shape::Render — opaque directly, alpha parts into the manager.</summary>
    public void Render(
        GraphicsDevice device, BasicEffect effect, AlphaManager alphaManager, N3EngineCamera camera)
    {
        if (DontRender)
            return;

        foreach (PartState state in _parts)
        {
            if (state.OutOfCameraRange || state.Renderer == null || state.Mesh == null)
                continue;

            Texture2D? texture = CurrentTexture(state);

            if (state.Plan.DeferToAlphaManager)
            {
                System.Numerics.Vector3 center =
                    System.Numerics.Vector3.Transform((state.Mesh.Min + state.Mesh.Max) * 0.5f, state.Matrix);
                alphaManager.Add(BuildAlphaPrimitive(state, texture), camera.Eye, center);
                continue;
            }

            device.RasterizerState = state.Plan.CullNone
                ? RasterizerState.CullNone
                : RasterizerState.CullCounterClockwise;
            device.SamplerStates[0] = state.Plan.UvClamp ? SamplerState.LinearClamp : SamplerState.LinearWrap;

            bool fogWasEnabled = effect.FogEnabled;
            effect.World = state.Matrix.ToXna();
            effect.Texture = texture;
            effect.TextureEnabled = texture != null;
            effect.FogEnabled = fogWasEnabled && !state.Plan.DisableFog;

            foreach (EffectPass pass in effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                state.Renderer.Draw(device);
            }

            effect.FogEnabled = fogWasEnabled;
        }

        device.RasterizerState = RasterizerState.CullCounterClockwise;
    }

    private static Texture2D? CurrentTexture(PartState state)
    {
        if (state.Textures.Length == 0)
            return null;
        int index = Math.Clamp((int)state.TexIndex, 0, state.Textures.Length - 1);
        return state.Textures[index];
    }

    private static AlphaPrimitive BuildAlphaPrimitive(PartState state, Texture2D? texture)
    {
        N3PMeshInstance instance = state.Renderer!.Instance;
        return new AlphaPrimitive
        {
            Vertices = MeshGeometry.ToXna(instance.Mesh.Vertices),
            Indices = MeshGeometry.ToIndexBuffer(instance.Indices.AsSpan(0, instance.NumIndices)),
            VertexCount = instance.NumVertices,
            PrimitiveCount = instance.NumIndices / 3,
            Texture = texture,
            World = state.Matrix,
            Plan = state.Plan,
        };
    }

    private (System.Numerics.Vector3 Min, System.Numerics.Vector3 Max, float Radius) ShapeBounds()
    {
        var min = new System.Numerics.Vector3(float.MaxValue);
        var max = new System.Numerics.Vector3(float.MinValue);
        bool any = false;
        foreach (PartState state in _parts)
        {
            if (state.Mesh == null)
                continue;
            any = true;
            System.Numerics.Matrix4x4 m = System.Numerics.Matrix4x4.Identity;
            m.Translation = state.Part.Pivot;
            m *= _shape.Matrix;
            min = System.Numerics.Vector3.Min(min, System.Numerics.Vector3.Transform(state.Mesh.Min, m));
            max = System.Numerics.Vector3.Max(max, System.Numerics.Vector3.Transform(state.Mesh.Max, m));
        }

        if (!any)
            return (_shape.Position, _shape.Position, 0f);

        return (min, max, (max - min).Length() * 0.5f);
    }
}

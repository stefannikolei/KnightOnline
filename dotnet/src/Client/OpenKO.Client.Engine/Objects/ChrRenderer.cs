using Microsoft.Xna.Framework.Graphics;
using OpenKO.Client.Assets;
using OpenKO.Client.Engine.Interop;
using OpenKO.Client.Engine.IO;
using OpenKO.Client.Engine.Rendering;
using OpenKO.Client.Engine.Scene;
using NumMatrix = System.Numerics.Matrix4x4;
using NumVector3 = System.Numerics.Vector3;

namespace OpenKO.Client.Engine.Objects;

/// <summary>Shared caches for character assets (one per data root).</summary>
public sealed class ChrAssetCaches(KoPathResolver resolver, TextureCache textures, PMeshCache meshes)
{
    public KoPathResolver Resolver { get; } = resolver;

    public TextureCache Textures { get; } = textures;

    public PMeshCache Meshes { get; } = meshes;

    public AssetCache<N3CPart> Parts { get; } = new(resolver);

    public AssetCache<N3CPartSkins> Skins { get; } = new(resolver);

    public AssetCache<N3AnimControl> AnimControls { get; } = new(resolver);

    public AssetCache<N3Joint> Joints { get; } = new(resolver);
}

/// <summary>
/// Port of the CN3Chr runtime: skeleton init (bind pose + inverse
/// matrices), animation frame machine, joint ticking (with motion blend),
/// CPU skinning per part and plug rendering on the joint matrices.
/// The skeleton is loaded per character (not shared) — the C++ shares the
/// tree but re-ticks it per character each frame anyway.
/// </summary>
public sealed class ChrRenderer
{
    /// <summary>CHR_LOD_CALCULATION_VALUES row 0 (s_iLODDelta default).</summary>
    private static readonly float[] LodThresholds = [8f, 16f, 48f, 128f];

    private sealed class PartState
    {
        public required N3CPart Part { get; init; }

        public required N3CPartSkins Skins { get; init; }

        public Texture2D? Texture { get; init; }

        public required MaterialPlan Plan { get; init; }

        public NumVector3[] DeformScratch { get; set; } = [];
    }

    private sealed class PlugState
    {
        public required N3CPlugBase Plug { get; init; }

        public required PMeshInstanceRenderer Renderer { get; init; }

        public Texture2D? Texture { get; init; }

        public NumMatrix Local { get; init; }
    }

    private readonly N3Chr _chr;
    private readonly N3Joint? _rootJoint;
    private readonly N3Joint[] _jointRefs = [];
    private readonly NumMatrix[] _mtxJoints = [];
    private readonly NumMatrix[] _mtxInverses = [];
    private readonly List<PartState> _parts = [];
    private readonly List<PlugState> _plugs = [];

    public AnimPlayer Anim { get; } = new();

    public N3Chr Chr => _chr;

    public N3AnimControl? AnimControl { get; }

    public int Lod { get; private set; }

    public ChrRenderer(N3Chr chr, ChrAssetCaches caches)
    {
        _chr = chr;
        chr.ReCalcMatrix();

        // Skeleton: per-character instance + bind pose (CN3Chr::Init).
        _rootJoint = caches.Joints.Load(chr.JointFileName);
        if (_rootJoint != null)
        {
            (N3Joint[] joints, System.Numerics.Matrix4x4[] inverse) = SkinDeformer.ComputeBindPose(_rootJoint);
            _jointRefs = joints;
            _mtxInverses = inverse;
            _mtxJoints = new NumMatrix[joints.Length];
            for (int i = 0; i < joints.Length; i++)
                _mtxJoints[i] = joints[i].Matrix;
        }

        foreach (string partFile in chr.PartFileNames)
        {
            N3CPart? part = caches.Parts.Get(partFile);
            if (part == null)
                continue;

            N3CPartSkins? skins = caches.Skins.Get(part.SkinsFileName);
            if (skins == null)
                continue;

            _parts.Add(new PartState
            {
                Part = part,
                Skins = skins,
                Texture = caches.Textures.Get(part.TexFileName),
                Plan = MaterialBinder.Plan(part.Material, hasOverlayTexture: false),
            });
        }

        foreach (string plugFile in chr.PlugFileNames)
        {
            // Type dispatch by extension (CN3CPlugBase::GetPlugTypeByFileName).
            N3CPlugBase? plug = N3CPlugBase.GetPlugTypeByFileName(plugFile) == N3PlugType.Cloak
                ? new AssetCache<N3CPlugCloak>(caches.Resolver).Load(plugFile)
                : new AssetCache<N3CPlug>(caches.Resolver).Load(plugFile);
            if (plug == null)
                continue;

            N3PMesh? mesh = caches.Meshes.Get(plug.PMeshFileName);
            if (mesh == null)
                continue;

            // CN3CPlugBase::ReCalcMatrix: Scale * Rot, translation = pos * scale.
            NumMatrix local = NumMatrix.CreateScale(plug.Scale) * plug.RotationMatrix;
            local.Translation = plug.Position * plug.Scale;

            _plugs.Add(new PlugState
            {
                Plug = plug,
                Renderer = new PMeshInstanceRenderer(mesh),
                Texture = caches.Textures.Get(plug.TexFileName),
                Local = local,
            });
        }

        AnimControl = caches.AnimControls.Get(chr.AniCtrlFileName);
        if (AnimControl is { Clips.Count: > 0 })
            Anim.SetAnim(AnimControl.Clips[0]);
    }

    public IReadOnlyList<NumMatrix> JointMatrices => _mtxJoints;

    /// <summary>CN3Chr::Tick + TickJoints (lower body path).</summary>
    public void Tick(N3EngineCamera camera, FrameTimer timer)
    {
        if (_rootJoint == null)
        {
            Lod = -1;
            return;
        }

        _chr.ReCalcMatrix();

        // LOD selection like the C++ (radius unknown until parts deform;
        // use a nominal 1.0 radius scale — refined by screenshots later).
        float dist = (_chr.Position - camera.Eye).Length();
        float lodValue = dist * camera.Fov / MathF.Max(_chr.Scale.X, 0.01f);
        Lod = LodThresholds.Length;
        for (int i = 0; i < LodThresholds.Length; i++)
        {
            if (lodValue < LodThresholds[i])
            {
                Lod = i;
                break;
            }
        }

        if (Lod >= LodThresholds.Length)
        {
            Lod = -1; // beyond the last threshold: not rendered
            return;
        }

        Anim.Tick(timer.SecPerFrame);

        // TickJoints (no upper-body split yet).
        for (int i = 0; i < _jointRefs.Length; i++)
        {
            if (Anim.BlendTime > 0f)
            {
                _jointRefs[i].ReCalcMatrixBlended(Anim.FrmCur, Anim.BlendFrm, Anim.BlendFactor);
            }
            else
            {
                _jointRefs[i].TickAnimationKey(Anim.FrmCur);
                _jointRefs[i].ReCalcMatrix();
            }

            _mtxJoints[i] = _jointRefs[i].Matrix;
        }

        foreach (PlugState plug in _plugs)
            plug.Renderer.SetLod(lodValue);
    }

    /// <summary>CN3Chr::Render — BuildMesh + per-part draw + plugs.</summary>
    public void Render(GraphicsDevice device, BasicEffect effect)
    {
        if (Lod < 0 || _rootJoint == null)
            return;

        effect.World = _chr.Matrix.ToXna();

        foreach (PartState state in _parts)
        {
            // Clamp to the deepest LOD that carries data (empty LOD skins
            // exist in the corpus; the C++ just skips empty ones).
            int lod = Lod;
            while (lod > 0 && state.Skins.Skins[lod].VertexCount == 0)
                lod--;
            N3Skin skin = state.Skins.Skins[lod];
            if (skin.VertexCount == 0 || skin.FaceCount == 0)
                continue;

            if (state.DeformScratch.Length < skin.VertexCount)
                state.DeformScratch = new NumVector3[skin.VertexCount];

            SkinDeformer.Deform(skin, _mtxJoints, _mtxInverses, state.DeformScratch);
            VertexPositionNormalTexture[] flat = SkinDeformer.Flatten(skin, state.DeformScratch);

            device.RasterizerState = state.Plan.CullNone
                ? RasterizerState.CullNone
                : RasterizerState.CullCounterClockwise;

            effect.Texture = state.Texture;
            effect.TextureEnabled = state.Texture != null;

            foreach (EffectPass pass in effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                device.DrawUserPrimitives(PrimitiveType.TriangleList, flat, 0, skin.FaceCount);
            }
        }

        // Plugs: world = plugLocal · joint · chrMatrix (CN3CPlugBase::Render).
        foreach (PlugState plug in _plugs)
        {
            if (plug.Plug.JointIndex < 0 || plug.Plug.JointIndex >= _mtxJoints.Length)
                continue;

            NumMatrix world = plug.Local * _mtxJoints[plug.Plug.JointIndex] * _chr.Matrix;
            effect.World = world.ToXna();
            effect.Texture = plug.Texture;
            effect.TextureEnabled = plug.Texture != null;

            foreach (EffectPass pass in effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                plug.Renderer.Draw(device);
            }
        }

        device.RasterizerState = RasterizerState.CullCounterClockwise;
    }
}

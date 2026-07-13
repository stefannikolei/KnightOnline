using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OpenKO.Client.Assets;
using OpenKO.Client.Engine.Input;
using OpenKO.Client.Engine.Interop;
using OpenKO.Client.Engine.IO;
using OpenKO.Client.Engine.Objects;
using OpenKO.Client.Engine.Rendering;
using OpenKO.Client.Engine.Scene;

namespace OpenKO.Client.Viewer;

/// <summary>
/// Stage-6.4 scene: browses the .n3chr corpus with animated skeletons,
/// CPU-skinned parts and joint plugs. Left/Right switches characters,
/// PgUp/PgDn switches animation clips.
/// </summary>
public sealed class CharacterScene : IScene
{
    private readonly List<string> _chrFiles = [];
    private BasicEffect? _effect;
    private ChrAssetCaches? _caches;
    private ChrRenderer? _renderer;
    private int _index;
    private int _clipIndex;
    private float _orbit;

    public string Name
    {
        get
        {
            if (_renderer == null)
                return "Charakter-Szene (keine Daten)";
            string clip = _renderer.Anim.Data?.Name is { Length: > 0 } n ? n : $"Clip {_clipIndex}";
            return $"Charakter [{_index + 1}/{_chrFiles.Count}] " +
                   $"{Path.GetFileName(_chrFiles[_index])} — {clip} (Frm {_renderer.Anim.FrmCur:F1})";
        }
    }

    public void Load(ViewerContext context)
    {
        _effect = new BasicEffect(context.Device);
        _effect.EnableDefaultLighting();

        if (context.DataPath != null)
        {
            var resolver = new KoPathResolver(context.DataPath);
            _caches = new ChrAssetCaches(
                resolver,
                new TextureCache(context.Device, resolver),
                new PMeshCache(resolver));
            _chrFiles.AddRange(Directory
                .EnumerateFiles(context.DataPath, "*.n3chr", new EnumerationOptions
                {
                    MatchCasing = MatchCasing.CaseInsensitive,
                    RecurseSubdirectories = true,
                })
                .Order(StringComparer.OrdinalIgnoreCase));
        }

        LoadCurrent();
    }

    private void LoadCurrent()
    {
        _renderer = null;
        _clipIndex = 0;
        if (_chrFiles.Count == 0 || _caches == null)
            return;

        try
        {
            var chr = new N3Chr();
            chr.LoadFromFile(_chrFiles[_index]);
            _renderer = new ChrRenderer(chr, _caches);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"{_chrFiles[_index]}: {ex.Message}");
        }
    }

    public void Tick(ViewerContext context)
    {
        _orbit += context.Timer.SecPerFrame * 0.35f;

        if (_chrFiles.Count > 0 && context.Input.IsKeyPress(KeyMap.DIK_RIGHT))
        {
            _index = (_index + 1) % _chrFiles.Count;
            LoadCurrent();
        }

        if (_chrFiles.Count > 0 && context.Input.IsKeyPress(KeyMap.DIK_LEFT))
        {
            _index = (_index - 1 + _chrFiles.Count) % _chrFiles.Count;
            LoadCurrent();
        }

        if (_renderer?.AnimControl is { Clips.Count: > 0 } anims)
        {
            if (context.Input.IsKeyPress(KeyMap.DIK_NEXT))
            {
                _clipIndex = (_clipIndex + 1) % anims.Clips.Count;
                _renderer.Anim.SetAnim(anims.Clips[_clipIndex]);
            }

            if (context.Input.IsKeyPress(KeyMap.DIK_PRIOR))
            {
                _clipIndex = (_clipIndex - 1 + anims.Clips.Count) % anims.Clips.Count;
                _renderer.Anim.SetAnim(anims.Clips[_clipIndex]);
            }
        }
    }

    public void Render(ViewerContext context)
    {
        GraphicsDevice device = context.Device;
        device.Clear(new Color(28, 30, 38));
        if (_renderer == null || _effect == null)
            return;

        device.DepthStencilState = DepthStencilState.Default;
        device.RasterizerState = RasterizerState.CullCounterClockwise;
        device.BlendState = BlendState.Opaque;
        device.SamplerStates[0] = SamplerState.LinearWrap;

        var camera = new N3EngineCamera
        {
            Eye = new System.Numerics.Vector3(MathF.Sin(_orbit) * 4.5f, 1.6f, MathF.Cos(_orbit) * 4.5f),
            At = new System.Numerics.Vector3(0f, 1.0f, 0f),
            Fov = N3EngineCamera.CharSelectFov,
            Aspect = device.Viewport.AspectRatio,
            NearPlane = 0.1f,
            FarPlane = 100f,
        };
        camera.Update();

        _renderer.Tick(camera, context.Timer);

        _effect.View = camera.View.ToXna();
        _effect.Projection = camera.Projection.ToXna();

        _renderer.Render(device, _effect);
    }

    public void Unload()
    {
        _caches?.Textures.Dispose();
        _caches = null;
        _effect?.Dispose();
        _effect = null;
        _chrFiles.Clear();
        _renderer = null;
        _index = 0;
    }
}

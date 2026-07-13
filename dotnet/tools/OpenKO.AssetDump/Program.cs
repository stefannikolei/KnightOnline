using System.Text.Json;
using OpenKO.Client.Assets;
using OpenKO.AssetDump;

if (args.Length == 0)
{
    Console.Error.WriteLine("usage: OpenKO.AssetDump <file-or-directory>... [--png <outdir>]");
    Console.Error.WriteLine("  Dumps N3 asset metrics as JSON lines; --png also decodes .dxt to PNG.");
    return 1;
}

string? pngDir = null;
var paths = new List<string>();
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--png" && i + 1 < args.Length)
        pngDir = args[++i];
    else
        paths.Add(args[i]);
}

var jsonOptions = new JsonSerializerOptions { WriteIndented = false };
int ok = 0, failed = 0;

foreach (string path in paths)
{
    if (Directory.Exists(path))
    {
        var options = new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive, RecurseSubdirectories = true };
        foreach (string file in Directory.EnumerateFiles(path, "*", options))
            DumpOne(file);
    }
    else
    {
        DumpOne(path);
    }
}

Console.Error.WriteLine($"{ok} parsed, {failed} failed");
return failed == 0 ? 0 : 2;

void DumpOne(string file)
{
    string ext = Path.GetExtension(file).ToLowerInvariant();
    try
    {
        object? summary = ext switch
        {
            ".dxt" => DumpTexture(file),
            ".n3pmesh" => Load<N3PMesh>(file, m => new
            {
                m.Name,
                m.MaxNumVertices,
                m.MaxNumIndices,
                m.MinNumVertices,
                m.NumCollapses,
                LodCtrlValues = m.LodCtrlValues.Length,
                Min = $"{m.Min.X} {m.Min.Y} {m.Min.Z}",
                Max = $"{m.Max.X} {m.Max.Y} {m.Max.Z}",
                m.Radius,
            }),
            ".n3vmesh" => Load<N3VMesh>(file, m => new { m.Name, Vertices = m.Vertices.Length, Indices = m.Indices.Length, m.Radius }),
            ".n3joint" => Load<N3Joint>(file, j => new { j.Name, Nodes = j.NodeCount(), PosKeys = j.KeyPos.Count, RotKeys = j.KeyRot.Count }),
            ".n3anim" => Load<N3AnimControl>(file, a => new { Clips = a.Clips.Count, Names = a.Clips.Select(c => c.Name).ToArray() }),
            ".n3cpart" => Load<N3CPart>(file, p => new { p.Name, p.TexFileName, p.SkinsFileName }),
            ".n3cskins" => Load<N3CPartSkins>(file, s => new { s.Name, LodVertexCounts = s.Skins.Select(k => k.VertexCount).ToArray() }),
            ".n3cplug" => Load<N3CPlug>(file, p => new { p.Name, p.JointIndex, p.PMeshFileName, p.TexFileName, p.TraceStep, HasFxMesh = p.FxPMesh != null }),
            ".n3cloak" => Load<N3CPlugCloak>(file, p => new { p.Name, p.JointIndex, p.PMeshFileName }),
            ".n3chr" => Load<N3Chr>(file, c => new { c.Name, c.JointFileName, Parts = c.PartFileNames.Count, Plugs = c.PlugFileNames.Count, c.AniCtrlFileName }),
            ".n3shape" => Load<N3Shape>(file, s => new { s.Name, Parts = s.Parts.Count, s.Belong, s.EventId, s.EventType, s.NpcId }),
            ".gtd" => Load<N3Terrain>(file, t => new { t.Name, t.MapSize, TileTextures = t.TileTextures.Count, Rivers = t.Rivers.Count, t.GrassFileName }),
            ".uif" => Load<N3UiBase>(file, u => new { u.Id, Widgets = CountWidgets(u), Children = u.Children.Count }),
            _ => null,
        };

        if (summary == null)
            return; // not an N3 asset

        Console.WriteLine(JsonSerializer.Serialize(new { File = file, Type = ext, Data = summary }, jsonOptions));
        ok++;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"{file}: {ex.GetType().Name}: {ex.Message}");
        failed++;
    }
}

static object Load<T>(string file, Func<T, object> project) where T : N3BaseFile, new()
{
    var asset = new T();
    asset.LoadFromFile(file);
    return project(asset);
}

object DumpTexture(string file)
{
    var tex = new N3Texture();
    tex.LoadFromFile(file);

    if (pngDir != null && N3Texture.IsCompressed(tex.Format))
    {
        byte[] rgba = DxtDecoder.Decode(tex.Format, tex.MipLevels[0], tex.Width, tex.Height);
        Directory.CreateDirectory(pngDir);
        string outFile = Path.Combine(pngDir, Path.GetFileNameWithoutExtension(file) + ".png");
        PngWriter.Write(outFile, rgba, tex.Width, tex.Height);
    }
    else if (pngDir != null)
    {
        byte[] rgba = DxtDecoder.DecodeUncompressed(tex.Format, tex.MipLevels[0], tex.Width, tex.Height);
        Directory.CreateDirectory(pngDir);
        PngWriter.Write(Path.Combine(pngDir, Path.GetFileNameWithoutExtension(file) + ".png"), rgba, tex.Width, tex.Height);
    }

    return new { tex.Name, tex.Width, tex.Height, Format = tex.Format.ToString(), tex.HasMipMaps, Levels = tex.MipLevels.Count };
}

static int CountWidgets(N3UiBase ui)
{
    int count = 1;
    foreach (N3UiBase child in ui.Children)
        count += CountWidgets(child);
    return count;
}

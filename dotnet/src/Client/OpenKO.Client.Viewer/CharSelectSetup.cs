using OpenKO.Client.Assets;
using OpenKO.Client.Engine.IO;

namespace OpenKO.Client.Viewer;

/// <summary>
/// Pure composition logic for the char-select milestone scene (headless
/// testable): picks the background shape, up to four characters and the
/// camera parameters the C++ char select uses (FOV 0.96 rad, NP 0.1, FP 100,
/// GameProcCharacterSelect.cpp).
/// </summary>
public sealed record CharSelectSetup(
    string? BackgroundShapePath,
    IReadOnlyList<string> ChrPaths,
    float CameraFov,
    float CameraNearPlane,
    float CameraFarPlane)
{
    public const int MaxCharacters = 4;

    public static CharSelectSetup Compose(string dataPath)
    {
        var options = new EnumerationOptions
        {
            MatchCasing = MatchCasing.CaseInsensitive,
            RecurseSubdirectories = false,
        };

        string chrSelectDir = Directory
            .EnumerateDirectories(dataPath)
            .FirstOrDefault(d => Path.GetFileName(d).Equals("ChrSelect", StringComparison.OrdinalIgnoreCase))
            ?? dataPath;
        string chrDir = Directory
            .EnumerateDirectories(dataPath)
            .FirstOrDefault(d => Path.GetFileName(d).Equals("Chr", StringComparison.OrdinalIgnoreCase))
            ?? dataPath;

        // Background: a ChrSelect stage shape (the C++ picks per nation).
        string? background = Directory
            .EnumerateFiles(chrSelectDir, "*.n3shape", options)
            .Order(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        // Characters: prefer the playable "upc_*" models in ChrSelect/, but
        // many of those reference the known pre-1264 legacy skeletons (UB in
        // the 1298 C++ too) — only candidates with a cleanly parsing,
        // plausible skeleton are used; Chr/ models fill remaining slots.
        var resolver = new KoPathResolver(dataPath);
        var chrs = Directory
            .EnumerateFiles(chrSelectDir, "upc_*.n3chr", options)
            .Order(StringComparer.OrdinalIgnoreCase)
            .Where(path => IsRenderableCharacter(path, resolver))
            .Take(MaxCharacters)
            .ToList();

        if (chrs.Count < MaxCharacters)
        {
            chrs.AddRange(Directory
                .EnumerateFiles(chrDir, "*.n3chr", options)
                .Order(StringComparer.OrdinalIgnoreCase)
                .Where(path => IsRenderableCharacter(path, resolver))
                .Take(MaxCharacters - chrs.Count));
        }

        return new CharSelectSetup(background, chrs, CameraFov: 0.96f, CameraNearPlane: 0.1f, CameraFarPlane: 100f);
    }

    /// <summary>Slot position in the row (in front of the background stage).</summary>
    public static System.Numerics.Vector3 SlotPosition(int slot)
        => new((slot - (MaxCharacters - 1) * 0.5f) * 1.8f, 0f, -3.0f);

    /// <summary>
    /// A character renders standalone when its skeleton parses fully into a
    /// plausible tree AND the file carries body parts — many ChrSelect
    /// models store no parts (the C++ equips them at runtime).
    /// </summary>
    public static bool IsRenderableCharacter(string chrPath, KoPathResolver resolver)
    {
        try
        {
            var chr = new N3Chr();
            chr.LoadFromFile(chrPath);
            if (!chr.PartFileNames.Any(p => p.Length > 0))
                return false;

            string? jointPath = resolver.Resolve(chr.JointFileName);
            if (jointPath == null)
                return false;

            using FileStream stream = File.OpenRead(jointPath);
            var joint = new N3Joint();
            joint.Load(new BinaryReader(stream));
            // Legacy pre-orient skeletons misparse into root-only trees with
            // trailing bytes; healthy 1298 skeletons consume the file fully.
            return stream.Position == stream.Length && joint.NodeCount() >= 8;
        }
        catch (Exception)
        {
            return false;
        }
    }
}

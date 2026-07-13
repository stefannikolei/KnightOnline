using OpenKO.Client.Viewer;
using Xunit;

namespace OpenKO.Client.Engine.Tests;

/// <summary>Stage-6.6 pins: the char-select composition logic (headless).</summary>
public class CharSelectSetupTests
{
    [Fact]
    public void SlotPositions_FormACenteredRow()
    {
        System.Numerics.Vector3 first = CharSelectSetup.SlotPosition(0);
        System.Numerics.Vector3 last = CharSelectSetup.SlotPosition(CharSelectSetup.MaxCharacters - 1);

        Assert.Equal(-first.X, last.X, 4); // symmetric around the origin
        Assert.Equal(0f, first.Y);
        Assert.True(first.X < CharSelectSetup.SlotPosition(1).X); // monotone left→right
    }

    [Fact]
    public void Compose_UsesTheCppCharSelectCameraParameters()
    {
        string empty = Path.Combine(Path.GetTempPath(), $"cs-setup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(empty);
        try
        {
            CharSelectSetup setup = CharSelectSetup.Compose(empty);

            // GameProcCharacterSelect: FOV 0.96 rad, NP 0.1, FP 100.
            Assert.Equal(0.96f, setup.CameraFov);
            Assert.Equal(0.1f, setup.CameraNearPlane);
            Assert.Equal(100f, setup.CameraFarPlane);
            Assert.Empty(setup.ChrPaths);
            Assert.Null(setup.BackgroundShapePath);
        }
        finally
        {
            Directory.Delete(empty, recursive: true);
        }
    }

    [Fact]
    [Trait("Category", "Corpus")]
    public void Compose_OverTheCorpus_FindsBackgroundAndCharacters()
    {
        string? root = null;
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "Client", "Data");
            if (Directory.Exists(candidate) && Directory.EnumerateFileSystemEntries(candidate).Any())
            {
                root = candidate;
                break;
            }
        }

        if (root == null)
            return; // corpus not available (e.g. CI)

        CharSelectSetup setup = CharSelectSetup.Compose(root);

        Assert.NotNull(setup.BackgroundShapePath);
        Assert.True(File.Exists(setup.BackgroundShapePath));
        Assert.InRange(setup.ChrPaths.Count, 1, CharSelectSetup.MaxCharacters);
        Assert.All(setup.ChrPaths, p => Assert.True(File.Exists(p)));

        // Every selected character must have a usable (non-legacy) skeleton.
        var resolver = new OpenKO.Client.Engine.IO.KoPathResolver(root);
        Assert.All(setup.ChrPaths, p => Assert.True(CharSelectSetup.IsRenderableCharacter(p, resolver)));
    }
}

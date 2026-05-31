using OpenKO.IO;
using Xunit;

namespace OpenKO.Tests;

public class N3FileAccessTests
{
    [Fact]
    public void NameHeaderRoundTripsThroughSaveAndLoad()
    {
        string path = Path.Combine(Path.GetTempPath(), $"openko_n3_{Guid.NewGuid():N}.n3");
        try
        {
            var writer = new N3BaseFileAccess { Name = "test_resource" };
            Assert.True(writer.SaveToFile(path));

            var reader = new N3BaseFileAccess();
            Assert.True(reader.LoadFromFile(path, N3FormatVersion.V1298));
            Assert.Equal("test_resource", reader.Name);
            Assert.Equal(N3FormatVersion.V1298, reader.FileFormatVersion);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void FileReaderReadsTypedLittleEndianValues()
    {
        var data = new byte[] { 0x04, 0x00, 0x00, 0x00, 0x44, 0x33, 0x22, 0x11 };
        var reader = new FileReader();
        reader.OpenFromMemory(data);

        Assert.Equal(4, reader.ReadInt32());
        Assert.Equal(0x11223344u, reader.ReadUInt32());
        Assert.Equal(data.Length, reader.Offset);
    }

    [Fact]
    public void FileReaderSeekRespectsBounds()
    {
        var reader = new FileReader();
        reader.OpenFromMemory(new byte[8]);

        Assert.True(reader.Seek(4, SeekOrigin.Begin));
        Assert.False(reader.Seek(-1, SeekOrigin.Begin));
        Assert.False(reader.Seek(100, SeekOrigin.Begin));
        Assert.True(reader.Seek(0, SeekOrigin.End));
    }
}

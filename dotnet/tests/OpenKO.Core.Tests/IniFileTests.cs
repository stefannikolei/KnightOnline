using OpenKO.Core.Config;
using Xunit;

namespace OpenKO.Core.Tests;

public class IniFileTests
{
    private static string WriteTempIni(string content)
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".ini");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void LoadsSectionsAndKeys()
    {
        string path = WriteTempIni("""
            [DOWNLOAD]
            URL=ftp.example.com
            PATH = /patch/

            [ODBC]
            DSN=KN_online
            """);

        var ini = new IniFile(path);
        Assert.Equal("ftp.example.com", ini.GetString("DOWNLOAD", "URL", ""));
        // 'key = value' whitespace is trimmed
        Assert.Equal("/patch/", ini.GetString("DOWNLOAD", "PATH", ""));
        Assert.Equal("KN_online", ini.GetString("ODBC", "DSN", ""));
    }

    [Fact]
    public void SectionAndKeyLookupIsCaseInsensitive()
    {
        string path = WriteTempIni("[Server_List]\nCOUNT=2\n");

        var ini = new IniFile(path);
        Assert.Equal(2, ini.GetInt("SERVER_LIST", "count", 0));
    }

    [Fact]
    public void MissingKeyReturnsDefaultAndInsertsIt()
    {
        string path = WriteTempIni("[A]\nx=1\n");

        var ini = new IniFile(path);
        Assert.Equal(15100, ini.GetInt("SETTINGS", "PORT", 15100));

        // The default was inserted (C++ Get* semantics) and persists on save.
        string savePath = path + ".out";
        ini.Save(savePath);
        var reloaded = new IniFile(savePath);
        Assert.Equal(15100, reloaded.GetInt("SETTINGS", "PORT", 0));
    }

    [Fact]
    public void GetBoolIsOnlyTrueForExactlyOne()
    {
        string path = WriteTempIni("[F]\na=1\nb=2\nc=0\n");

        var ini = new IniFile(path);
        Assert.True(ini.GetBool("F", "a", false));
        Assert.False(ini.GetBool("F", "b", false)); // atoi==2 → not 1 → false, like the C++
        Assert.False(ini.GetBool("F", "c", true));
    }

    [Fact]
    public void InvalidSectionSkipsFollowingKeys()
    {
        string path = WriteTempIni("[GOOD]\nx=1\nbad-section-line\ny=2\n");

        var ini = new IniFile(path);
        Assert.Equal(1, ini.GetInt("GOOD", "x", 0));
        // 'y' followed an invalid section marker and must not land in [GOOD]
        Assert.Equal(0, ini.GetInt("GOOD", "y", 0));
    }
}

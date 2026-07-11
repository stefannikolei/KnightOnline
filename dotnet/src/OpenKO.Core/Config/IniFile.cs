using System.Text;

namespace OpenKO.Core.Config;

/// <summary>
/// Port of <c>shared/Ini.{h,cpp}</c> (CIni).
/// Sections and keys are case-insensitive; Get* calls insert the default value
/// into the in-memory map when the key is missing (so a later <see cref="Save()"/>
/// writes it out), exactly like the C++.
/// </summary>
public class IniFile
{
    private readonly SortedDictionary<string, SortedDictionary<string, string>> _configMap =
        new(StringComparer.OrdinalIgnoreCase);

    public string? Path { get; private set; }

    public IniFile()
    {
    }

    public IniFile(string path)
    {
        Load(path);
    }

    public bool Load() => Path is not null && Load(Path);

    public bool Load(string path)
    {
        Path = path;

        string[] lines;
        try
        {
            // Server INIs are ASCII/CP949; Latin1 preserves the raw bytes per char
            // so values round-trip losslessly to the wire encoding.
            lines = File.ReadAllLines(path, Encoding.Latin1);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }

        string currentSection = string.Empty;

        // If an invalid section is hit, ensure that we don't place key/value pairs
        // from the invalid section into the previously loaded section.
        bool skipNextSection = false;
        foreach (string rawLine in lines)
        {
            string line = rawLine.TrimEnd();
            if (line.Length == 0)
                continue;

            // Check for value strings first (matches the C++ trade-off: section
            // names cannot contain '=').
            int keySeparatorPos = line.IndexOf('=');
            if (keySeparatorPos >= 0)
            {
                if (skipNextSection)
                    continue;

                string key = line[..keySeparatorPos].TrimEnd();
                string value = line[(keySeparatorPos + 1)..].TrimStart();

                GetOrAddSection(currentSection)[key] = value;
                continue;
            }

            // Not a value, so assume it's a section.
            int sectionStart = line.IndexOf('[');
            int sectionEnd = line.LastIndexOf(']');

            if (sectionStart < 0 || sectionEnd < 0 || sectionStart > sectionEnd)
            {
                /* invalid section */
                skipNextSection = true;
                continue;
            }

            currentSection = line.Substring(sectionStart + 1, sectionEnd - sectionStart - 1);
            skipNextSection = false;
        }

        return true;
    }

    public void Save()
    {
        if (Path is not null)
            Save(Path);
    }

    public void Save(string path)
    {
        var sb = new StringBuilder();
        foreach (var (sectionName, keyValuePairs) in _configMap)
        {
            sb.Append('[').Append(sectionName).Append("]\n");

            foreach (var (key, value) in keyValuePairs)
                sb.Append(key).Append('=').Append(value).Append('\n');

            sb.Append('\n');
        }

        File.WriteAllText(path, sb.ToString(), Encoding.Latin1);
    }

    private SortedDictionary<string, string> GetOrAddSection(string section)
    {
        if (!_configMap.TryGetValue(section, out var entries))
        {
            entries = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _configMap.Add(section, entries);
        }

        return entries;
    }

    public int GetInt(string section, string key, int defaultValue)
    {
        if (_configMap.TryGetValue(section, out var entries)
            && entries.TryGetValue(key, out string? value))
            return Atoi(value);

        SetInt(section, key, defaultValue);
        return defaultValue;
    }

    public bool GetBool(string section, string key, bool defaultValue)
        => GetInt(section, key, defaultValue ? 1 : 0) == 1;

    public string GetString(string section, string key, string defaultValue)
    {
        if (_configMap.TryGetValue(section, out var entries)
            && entries.TryGetValue(key, out string? value))
            return value;

        SetString(section, key, defaultValue);
        return defaultValue;
    }

    public void SetInt(string section, string key, int value)
        => SetString(section, key, value.ToString());

    public void SetString(string section, string key, string value)
        => GetOrAddSection(section)[key] = value;

    /// <summary>C atoi semantics: leading whitespace, optional sign, digits; 0 if none.</summary>
    private static int Atoi(string s)
    {
        int i = 0;
        while (i < s.Length && char.IsWhiteSpace(s[i]))
            i++;

        bool negative = false;
        if (i < s.Length && (s[i] == '+' || s[i] == '-'))
        {
            negative = s[i] == '-';
            i++;
        }

        long result = 0;
        bool any = false;
        while (i < s.Length && s[i] is >= '0' and <= '9')
        {
            any = true;
            result = result * 10 + (s[i] - '0');
            if (result > int.MaxValue + 1L)
                break;
            i++;
        }

        if (!any)
            return 0;

        result = negative ? -result : result;
        return result < int.MinValue ? int.MinValue
            : result > int.MaxValue ? int.MaxValue
            : (int)result;
    }
}

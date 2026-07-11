using System.Text;

namespace OpenKO.Servers.ItemManager;

/// <summary>
/// Minimal replacement for the C++ spdlog daily_file_format_sink: one line per
/// entry, "[timestamp] [logger] [info] message", rolling to a new file per day
/// (suffix _YYYY-MM-DD before the extension, spdlog's daily naming).
/// </summary>
public sealed class DailyFileLogger(string basePath, string loggerName) : IDisposable
{
    private readonly Lock _lock = new();
    private StreamWriter? _writer;
    private DateOnly _currentDay;

    public void Info(string message)
    {
        DateTime now = DateTime.Now;

        lock (_lock)
        {
            var day = DateOnly.FromDateTime(now);
            if (_writer is null || day != _currentDay)
            {
                _writer?.Dispose();
                _writer = new StreamWriter(PathForDay(day), append: true, Encoding.UTF8);
                _currentDay = day;
            }

            _writer.WriteLine($"[{now:yyyy-MM-dd HH:mm:ss.fff}] [{loggerName}] [info] {message}");
            _writer.Flush();
        }
    }

    private string PathForDay(DateOnly day)
    {
        string directory = Path.GetDirectoryName(basePath) ?? ".";
        Directory.CreateDirectory(directory);

        string name = Path.GetFileNameWithoutExtension(basePath);
        string extension = Path.GetExtension(basePath);
        return Path.Combine(directory, $"{name}_{day:yyyy-MM-dd}{extension}");
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }
}

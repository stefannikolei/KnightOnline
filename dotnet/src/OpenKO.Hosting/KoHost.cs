using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace OpenKO.Hosting;

/// <summary>
/// Generic-host glue replacing the C++ AppThread (argparse + spdlog + FTXUI).
/// The FTXUI terminal UI is not ported; <c>--headless</c> is accepted and ignored
/// so existing launch scripts keep working.
/// </summary>
public static class KoHost
{
    public static HostApplicationBuilder CreateBuilder(string[] args)
    {
        // Strip flags the C++ servers accept but that have no meaning here.
        string[] filtered = args.Where(a => a is not ("--headless" or "-h")).ToArray();

        var builder = Host.CreateApplicationBuilder(filtered);
        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole(options =>
        {
            options.SingleLine = true;
            options.TimestampFormat = "[yyyy-MM-dd HH:mm:ss.fff] ";
        });

        return builder;
    }

    /// <summary>
    /// The C++ servers resolve their INI relative to the executable directory
    /// (AppThread::GetProgPath); tests and `dotnet run` use the working directory
    /// as fallback when no file exists next to the binary.
    /// </summary>
    public static string ResolveConfigPath(string fileName)
    {
        string besideBinary = Path.Combine(AppContext.BaseDirectory, fileName);
        if (File.Exists(besideBinary))
            return besideBinary;

        return Path.Combine(Environment.CurrentDirectory, fileName);
    }
}

using System.ComponentModel.DataAnnotations;
using OpenKO.Servers.ItemManager.Transport;

namespace OpenKO.Servers.ItemManager;

/// <summary>
/// Bound configuration for the item/exp logger (the modern replacement for the
/// legacy <c>ItemManager.ini</c>). The C++ shared-memory queue is replaced by a
/// TCP loopback listener on <see cref="Port"/>.
/// </summary>
public sealed class ItemManagerOptions
{
    public const string SectionName = "ItemManager";

    [Required]
    public string ItemLogFile { get; set; } = "logs/ItemLog.txt";

    [Required]
    public string ExpLogFile { get; set; } = "logs/ExpLog.txt";

    [Range(1, 65535)]
    public int Port { get; set; } = TcpItemLogSource.DefaultPort;
}

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenKO.Core.Protocol;
using OpenKO.Servers.ItemManager.Transport;

namespace OpenKO.Servers.ItemManager;

/// <summary>
/// Port of the ItemManager consumer loop (ItemManagerReadQueueThread::process_packet):
/// dispatches WIZ_ITEM_LOG / WIZ_DATASAVE messages to the item/exp log files.
/// </summary>
public sealed class ItemManagerService(
    IItemLogSource source,
    DailyFileLogger itemLogger,
    DailyFileLogger expLogger,
    ILogger<ItemManagerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("ItemManager started, processing requests");

        await foreach (byte[] message in source.ReadAllAsync(stoppingToken))
        {
            if (message.Length == 0)
                continue;

            var opcode = (GameOpcode)message[0];
            ReadOnlySpan<byte> body = message.AsSpan(1);

            switch (opcode)
            {
                case GameOpcode.WIZ_ITEM_LOG:
                {
                    string? line = ItemLogParser.ParseItemLog(body);
                    if (line is not null)
                        itemLogger.Info(line);
                    else
                        logger.LogTrace("ItemLogWrite failed validation");
                    break;
                }

                case GameOpcode.WIZ_DATASAVE:
                {
                    string? line = ItemLogParser.ParseExpLog(body);
                    if (line is not null)
                        expLogger.Info(line);
                    else
                        logger.LogTrace("ExpLogWrite failed validation");
                    break;
                }
            }
        }
    }
}

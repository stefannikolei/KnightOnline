using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using OpenKO.Data;
using OpenKO.Data.Models;

namespace OpenKO.Servers.Ebenezer.Db;

/// <summary>
/// Loads the GAME-DB tables the Ebenezer server reads at startup (the C++
/// recordset loaders in EbenezerApp). Grows table by table as the stage-4
/// slices land; each loader returns null and logs on SqlException, matching
/// the C++ Load_ForbidEmpty error handling.
/// </summary>
public sealed class EbenezerDb(SqlConnectionFactory connectionFactory, ILogger logger)
{
    /// <summary>EbenezerApp::LoadCoefficientTable (COEFFICIENT).</summary>
    public Task<List<Coefficient>?> LoadCoefficientTableAsync(CancellationToken cancellationToken = default) =>
        LoadTableAsync(
            "COEFFICIENT",
            "sClass, ShortSword, Sword, Axe, Club, Spear, Pole, Staff, Bow, Hp, Mp, Sp, Ac, Hitrate, Evasionrate",
            static reader => new Coefficient
            {
                ClassId = reader.GetInt16(0),
                ShortSword = reader.GetDouble(1),
                Sword = reader.GetDouble(2),
                Axe = reader.GetDouble(3),
                Club = reader.GetDouble(4),
                Spear = reader.GetDouble(5),
                Pole = reader.GetDouble(6),
                Staff = reader.GetDouble(7),
                Bow = reader.GetDouble(8),
                HitPoint = reader.GetDouble(9),
                ManaPoint = reader.GetDouble(10),
                Sp = reader.GetDouble(11),
                Armor = reader.GetDouble(12),
                HitRate = reader.GetDouble(13),
                EvasionRate = reader.GetDouble(14),
            },
            cancellationToken);

    /// <summary>EbenezerApp::MapFileLoad source rows (ZONE_INFO).</summary>
    public Task<List<ZoneInfo>?> LoadZoneInfoTableAsync(CancellationToken cancellationToken = default) =>
        LoadTableAsync(
            "ZONE_INFO",
            "ServerNo, ZoneNo, strZoneName, InitX, InitZ, InitY, Type, RoomEvent",
            static reader => new ZoneInfo
            {
                ServerId = (byte)reader.GetInt16(0),
                ZoneId = reader.GetInt16(1),
                Name = reader.IsDBNull(2) ? string.Empty : reader.GetString(2).TrimEnd(),
                InitX = reader.GetInt32(3),
                InitZ = reader.GetInt32(4),
                InitY = reader.GetInt32(5),
                Type = reader.GetByte(6),
                RoomEvent = reader.GetByte(7),
            },
            cancellationToken);

    private async Task<List<T>?> LoadTableAsync<T>(
        string tableName, string columns, Func<SqlDataReader, T> readRow, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await connectionFactory.OpenAsync(cancellationToken);
            await using var command = new SqlCommand($"SELECT {columns} FROM {tableName}", connection);

            var list = new List<T>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                list.Add(readRow(reader));

            return list;
        }
        catch (SqlException ex)
        {
            logger.LogError(ex, "Load of table {TableName} failed", tableName);
            return null;
        }
    }
}

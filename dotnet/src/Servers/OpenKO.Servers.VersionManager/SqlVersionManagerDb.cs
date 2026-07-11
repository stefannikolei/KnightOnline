using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using OpenKO.Core.Protocol;
using OpenKO.Data;
using OpenKO.Data.Models;

namespace OpenKO.Servers.VersionManager;

/// <summary>
/// SqlClient implementation of the CDBProcess queries against the unchanged
/// KN_online schema (tables VERSION, TB_USER, CURRENTUSER, CONCURRENT and the
/// LOAD_PREMIUM_SERVICE_USER stored procedure).
/// </summary>
public sealed class SqlVersionManagerDb(SqlConnectionFactory connectionFactory, ILogger<SqlVersionManagerDb> logger)
    : IVersionManagerDb
{
    public async Task<List<VersionRow>?> LoadVersionListAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await connectionFactory.OpenAsync(cancellationToken);
            await using var command = new SqlCommand(
                "SELECT sVersion, strFileName, strCompressName, sHistoryVersion FROM [VERSION]", connection);

            var list = new List<VersionRow>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                list.Add(new VersionRow(
                    reader.GetInt16(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetInt16(3)));
            }

            return list;
        }
        catch (SqlException ex)
        {
            logger.LogError(ex, "LoadVersionList failed");
            return null;
        }
    }

    public async Task<AuthResult> AccountLoginAsync(string accountId, string password, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await connectionFactory.OpenAsync(cancellationToken);
            await using var command = new SqlCommand(
                "SELECT strPasswd, strAuthority FROM TB_USER WHERE strAccountID = @accountId", connection);
            command.Parameters.Add("@accountId", SqlDbType.VarChar, ProtocolConstants.MaxIdSize).Value = accountId;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                return AuthResult.AUTH_NOT_FOUND;

            string storedPassword = reader.GetString(0);
            byte authority = reader.GetByte(1);

            // Ordinal, case-sensitive compare in the application, like the C++ —
            // and deliberately AUTH_NOT_FOUND (not AUTH_INVALID_PW) so attackers
            // can't identify real accounts.
            if (!string.Equals(storedPassword, password, StringComparison.Ordinal))
                return AuthResult.AUTH_NOT_FOUND;

            if (authority == GlobalConstants.AuthorityBlockUser)
                return AuthResult.AUTH_BANNED;

            return AuthResult.AUTH_OK;
        }
        catch (SqlException ex)
        {
            logger.LogError(ex, "AccountLogin failed");
            return AuthResult.AUTH_FAILED;
        }
    }

    public async Task<CurrentUser?> GetCurrentUserAsync(string accountId, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await connectionFactory.OpenAsync(cancellationToken);
            await using var command = new SqlCommand(
                "SELECT nServerNo, strServerIP FROM CURRENTUSER WHERE strAccountID = @accountId", connection);
            command.Parameters.Add("@accountId", SqlDbType.VarChar, ProtocolConstants.MaxIdSize).Value = accountId;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                return null;

            return new CurrentUser(accountId, reader.GetInt32(0), reader.GetString(1));
        }
        catch (SqlException ex)
        {
            logger.LogError(ex, "IsCurrentUser failed");
            return null;
        }
    }

    public async Task<List<ConcurrentRow>?> LoadUserCountsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await connectionFactory.OpenAsync(cancellationToken);
            await using var command = new SqlCommand(
                "SELECT serverid, zone1_count, zone2_count, zone3_count FROM CONCURRENT", connection);

            var list = new List<ConcurrentRow>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                list.Add(new ConcurrentRow(
                    reader.GetByte(0),
                    reader.GetInt16(1),
                    reader.GetInt16(2),
                    reader.GetInt16(3)));
            }

            return list;
        }
        catch (SqlException ex)
        {
            logger.LogError(ex, "LoadUserCountList failed");
            return null;
        }
    }

    public async Task<short?> LoadPremiumServiceUserAsync(string accountId, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await connectionFactory.OpenAsync(cancellationToken);
            // Positional call like the ODBC reference ("{CALL LOAD_PREMIUM_SERVICE_USER(?,?,?)}");
            // avoids depending on the procedure's parameter names.
            await using var command = new SqlCommand(
                "EXEC LOAD_PREMIUM_SERVICE_USER @accountId, @premiumType OUTPUT, @daysRemaining OUTPUT", connection);
            command.Parameters.Add("@accountId", SqlDbType.VarChar, ProtocolConstants.MaxIdSize).Value = accountId;
            var typeParam = command.Parameters.Add("@premiumType", SqlDbType.Int);
            typeParam.Direction = ParameterDirection.Output;
            var daysParam = command.Parameters.Add("@daysRemaining", SqlDbType.Int);
            daysParam.Direction = ParameterDirection.Output;

            await command.ExecuteNonQueryAsync(cancellationToken);

            int days = daysParam.Value is int d ? d : 0;
            return (short)days;
        }
        catch (SqlException ex)
        {
            logger.LogError(ex, "LoadPremiumServiceUser failed");
            return null;
        }
    }
}

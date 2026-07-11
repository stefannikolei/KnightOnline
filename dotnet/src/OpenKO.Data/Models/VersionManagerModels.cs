namespace OpenKO.Data.Models;

/// <summary>VERSION table (deps/db-models VersionManagerModel: sVersion PK, strFileName, strCompressName, sHistoryVersion).</summary>
public sealed record VersionRow(short Number, string FileName, string CompressName, short HistoryVersion);

/// <summary>TB_USER subset used for login (strAccountID PK, strPasswd, strAuthority).</summary>
public sealed record TbUser(string AccountId, string Password, byte Authority);

/// <summary>CURRENTUSER table (strAccountID PK, nServerNo, strServerIP).</summary>
public sealed record CurrentUser(string AccountId, int ServerId, string ServerIP);

/// <summary>CONCURRENT table (serverid, zone1_count, zone2_count, zone3_count).</summary>
public sealed record ConcurrentRow(byte ServerId, short Zone1Count, short Zone2Count, short Zone3Count);

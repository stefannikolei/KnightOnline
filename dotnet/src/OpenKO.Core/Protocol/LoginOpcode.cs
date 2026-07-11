namespace OpenKO.Core.Protocol;

/// <summary>Port of <c>e_LoginOpcode</c> from <c>shared/packets.h</c> (VersionManager protocol).</summary>
public enum LoginOpcode : byte
{
    LS_VERSION_REQ = 0x01,
    LS_DOWNLOADINFO_REQ = 0x02,
    LS_CRYPTION = 0xF2,
    LS_LOGIN_REQ = 0xF3,
    LS_MGAME_LOGIN = 0xF4, // NOTE: We don't implement this stored procedure.
    LS_SERVERLIST = 0xF5,
    LS_NEWS = 0xF6
}

/// <summary>Port of <c>e_AuthResult</c> from <c>shared/packets.h</c>.</summary>
public enum AuthResult : byte
{
    AUTH_OK = 0x01,
    AUTH_NOT_FOUND = 0x02,
    AUTH_INVALID_PW = 0x03,
    AUTH_BANNED = 0x04,
    AUTH_IN_GAME = 0x05,
    AUTH_ERROR = 0x06,
    AUTH_FAILED = 0xFF
}

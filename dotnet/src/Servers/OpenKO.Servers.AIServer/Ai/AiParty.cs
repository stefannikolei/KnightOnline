namespace OpenKO.Servers.AIServer.Ai;

/// <summary>Port of <c>_PARTY_GROUP</c> (Server/AIServer/Define.h).</summary>
public sealed class PartyGroup
{
    public const int MaxMembers = 8;

    public short Index = -1;

    /// <summary>Member user ids (uid), -1 for empty slots.</summary>
    public readonly short[] Users = new short[MaxMembers];

    public PartyGroup()
    {
        Array.Fill(Users, (short)-1);
    }
}

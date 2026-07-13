using System.Numerics;
using OpenKO.GameData.Math;

namespace OpenKO.Client.Assets;

public enum N3AnimKeyType : uint
{
    Vector3 = 0,
    Quaternion = 1,
    Unknown = 0xFFFFFFFF,
}

/// <summary>
/// Port of <c>CN3AnimKey</c> (Client/N3Base/N3AnimKey.cpp/.h) — one keyframe
/// channel. On disk: [int32 count] and, when count &gt; 0, [type][samplingRate]
/// [raw keys]. The C++ allocates count+1 slots and duplicates the last key so
/// interpolation can always read index+1; the port keeps that layout.
/// No name header (CN3AnimKey derives from CN3Base, not CN3BaseFileAccess).
/// </summary>
public sealed class N3AnimKey
{
    public N3AnimKeyType Type { get; private set; } = N3AnimKeyType.Vector3;

    public int Count { get; private set; }

    public float SamplingRate { get; private set; } = 30f;

    /// <summary>Length Count + 1 (last key duplicated), empty when Count == 0.</summary>
    public Vector3[] Vector3Keys { get; private set; } = [];

    /// <summary>Length Count + 1 (last key duplicated), empty when Count == 0.</summary>
    public Quaternion[] QuaternionKeys { get; private set; } = [];

    public void Load(BinaryReader reader)
    {
        Count = reader.ReadInt32();
        Vector3Keys = [];
        QuaternionKeys = [];

        if (Count <= 0)
            return;

        Type = (N3AnimKeyType)reader.ReadUInt32();
        SamplingRate = reader.ReadSingle();

        if (Type == N3AnimKeyType.Vector3)
        {
            var keys = new Vector3[Count + 1];
            for (int i = 0; i < Count; i++)
                keys[i] = reader.ReadVector3();
            keys[Count] = keys[Count - 1];
            Vector3Keys = keys;
        }
        else if (Type == N3AnimKeyType.Quaternion)
        {
            var keys = new Quaternion[Count + 1];
            for (int i = 0; i < Count; i++)
                keys[i] = reader.ReadQuaternion();
            keys[Count] = keys[Count - 1];
            QuaternionKeys = keys;
        }
    }

    public void Save(BinaryWriter writer)
    {
        writer.Write(Count);
        if (Count <= 0)
            return;

        writer.Write((uint)Type);
        writer.Write(SamplingRate);

        if (Type == N3AnimKeyType.Vector3)
        {
            for (int i = 0; i < Count; i++)
                writer.Write(Vector3Keys[i]);
        }
        else if (Type == N3AnimKeyType.Quaternion)
        {
            for (int i = 0; i < Count; i++)
                writer.Write(QuaternionKeys[i]);
        }
    }

    public void InitializeVector3(Vector3[] keys, float samplingRate = 30f)
    {
        Type = N3AnimKeyType.Vector3;
        Count = keys.Length;
        SamplingRate = samplingRate;
        Vector3Keys = keys.Length > 0 ? [.. keys, keys[^1]] : [];
        QuaternionKeys = [];
    }

    public void InitializeQuaternion(Quaternion[] keys, float samplingRate = 30f)
    {
        Type = N3AnimKeyType.Quaternion;
        Count = keys.Length;
        SamplingRate = samplingRate;
        QuaternionKeys = keys.Length > 0 ? [.. keys, keys[^1]] : [];
        Vector3Keys = [];
    }

    /// <summary>
    /// CN3AnimKey::DataGet(float, __Vector3&amp;) — 30fps frame time mapped
    /// through the sampling rate; linear interpolation between neighbors. The
    /// C++'s exact index bound (index may equal Count) is kept.
    /// </summary>
    public bool TryGetVector3(float frame, ref Vector3 value)
    {
        if (Type != N3AnimKeyType.Vector3 || Count <= 0)
            return false;

        float fD = 30f / SamplingRate;
        int index = (int)(frame * (SamplingRate / 30f));
        if (index < 0 || index > Count)
            return false;

        float delta;
        if (index == Count)
        {
            index = Count - 1;
            delta = 0f;
        }
        else
        {
            delta = (frame - index * fD) / fD;
        }

        value = delta != 0f
            ? Vector3Keys[index] * (1f - delta) + Vector3Keys[index + 1] * delta
            : Vector3Keys[index];
        return true;
    }

    /// <summary>CN3AnimKey::DataGet(float, __Quaternion&amp;) — slerp via the verbatim __Quaternion port.</summary>
    public bool TryGetQuaternion(float frame, ref Quaternion value)
    {
        if (Type != N3AnimKeyType.Quaternion || Count <= 0)
            return false;

        float fD = 30f / SamplingRate;
        int index = (int)(frame * (SamplingRate / 30f));
        if (index < 0 || index > Count)
            return false;

        float delta;
        if (index == Count)
        {
            index = Count - 1;
            delta = 0f;
        }
        else
        {
            delta = (frame - index * fD) / fD;
        }

        value = delta != 0f
            ? KoQuaternion.Slerp(QuaternionKeys[index], QuaternionKeys[index + 1], delta)
            : QuaternionKeys[index];
        return true;
    }
}

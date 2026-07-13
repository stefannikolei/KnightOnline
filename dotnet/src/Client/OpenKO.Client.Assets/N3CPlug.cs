using System.Numerics;

namespace OpenKO.Client.Assets;

public enum N3PlugType : uint
{
    Normal = 0,
    Cloak = 1,
    Max = 10,
    Undefined = 0xFFFFFFFF,
}

/// <summary>
/// Port of <c>CN3CPlugBase</c> (N3Chr.cpp) — an attachment (weapon, shield,
/// cloak) plugged onto a joint: placement, material and the referenced
/// PMesh/texture file names.
/// </summary>
public class N3CPlugBase : N3BaseFile
{
    public N3PlugType PlugType { get; set; }

    public int JointIndex { get; set; }

    public Vector3 Position { get; set; }

    public Matrix4x4 RotationMatrix { get; set; } = Matrix4x4.Identity;

    public Vector3 Scale { get; set; } = Vector3.One;

    public N3Material Material { get; set; }

    public string PMeshFileName { get; set; } = string.Empty;

    public string TexFileName { get; set; } = string.Empty;

    /// <summary>
    /// CN3CPlugBase::GetPlugTypeByFileName — dispatch by the last two
    /// characters of the extension ("..ug" = .n3cplug, "..ak" = cloak).
    /// </summary>
    public static N3PlugType GetPlugTypeByFileName(string fileName)
    {
        if (fileName.Length < 2)
            return N3PlugType.Undefined;

        if (fileName.EndsWith("ug", StringComparison.OrdinalIgnoreCase))
            return N3PlugType.Normal;
        if (fileName.EndsWith("ak", StringComparison.OrdinalIgnoreCase))
            return N3PlugType.Cloak;
        return N3PlugType.Undefined;
    }

    public override void Load(BinaryReader reader)
    {
        base.Load(reader);

        PlugType = (N3PlugType)reader.ReadUInt32();
        if ((uint)PlugType > (uint)N3PlugType.Max)
            PlugType = N3PlugType.Normal; // C++ clamps unknown types

        JointIndex = reader.ReadInt32();
        Position = reader.ReadVector3();
        RotationMatrix = reader.ReadMatrix4x4();
        Scale = reader.ReadVector3();
        Material = reader.ReadStruct<N3Material>();
        PMeshFileName = reader.ReadN3FileName();
        TexFileName = reader.ReadN3FileName();
    }

    public override void Save(BinaryWriter writer)
    {
        base.Save(writer);

        writer.Write((uint)PlugType);
        writer.Write(JointIndex);
        writer.Write(Position);
        writer.Write(RotationMatrix);
        writer.Write(Scale);
        writer.WriteStruct(Material);
        writer.WriteN3FileName(PMeshFileName);
        writer.WriteN3FileName(TexFileName);
    }
}

/// <summary>
/// Port of <c>CN3CPlug</c> — the .n3cplug file: the base plug plus weapon
/// trace parameters and an optional embedded PMesh for FX placement.
/// </summary>
public class N3CPlug : N3CPlugBase
{
    public int TraceStep { get; set; }

    public uint TraceColor { get; set; } = 0xFFFFFFFF;

    public float Trace0 { get; set; }

    public float Trace1 { get; set; }

    /// <summary>The embedded FX PMesh (iUseVMesh != 0), or null.</summary>
    public N3PMesh? FxPMesh { get; set; }

    public override void Load(BinaryReader reader)
    {
        base.Load(reader);

        // Old plug files (Intro/ChrSelect era) end right after the base plug.
        // The C++ File reads past EOF are no-ops, so trace/FX-mesh keep their
        // constructor defaults there — mirrored with these EOF guards.
        if (reader.BaseStream.Position >= reader.BaseStream.Length)
            return;

        TraceStep = reader.ReadInt32();
        if (TraceStep > 0)
        {
            TraceColor = reader.ReadUInt32();
            Trace0 = reader.ReadSingle();
            Trace1 = reader.ReadSingle();
        }
        else
        {
            TraceStep = 0;
        }

        if (reader.BaseStream.Position >= reader.BaseStream.Length)
            return;

        int useVMesh = reader.ReadInt32();
        if (useVMesh != 0)
        {
            FxPMesh = new N3PMesh { FileFormatVersion = FileFormatVersion };
            FxPMesh.Load(reader);
        }
        else
        {
            FxPMesh = null;
        }
    }

    public override void Save(BinaryWriter writer)
    {
        base.Save(writer);

        writer.Write(TraceStep);
        if (TraceStep > 0)
        {
            writer.Write(TraceColor);
            writer.Write(Trace0);
            writer.Write(Trace1);
        }

        writer.Write(FxPMesh != null ? 1 : 0);
        FxPMesh?.Save(writer);
    }
}

/// <summary>Port of <c>CN3CPlug_Cloak</c> — a plain plug; the cloak sim is runtime-only.</summary>
public sealed class N3CPlugCloak : N3CPlugBase
{
}

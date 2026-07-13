using OpenKO.Core.Text;

namespace OpenKO.Client.Assets;

/// <summary>
/// One animation clip definition (__AnimData in N3AnimControl.h): frame
/// ranges plus plug-trace/sound/strike markers and blending parameters.
/// </summary>
public sealed class N3AnimData
{
    public string Name { get; set; } = string.Empty;

    public float FrmStart { get; set; }

    public float FrmEnd { get; set; }

    public float FrmPerSec { get; set; } = 30f;

    public float FrmPlugTraceStart { get; set; }

    public float FrmPlugTraceEnd { get; set; }

    public float FrmSound0 { get; set; }

    public float FrmSound1 { get; set; }

    public float TimeBlend { get; set; }

    public int BlendFlags { get; set; }

    public float FrmStrike0 { get; set; }

    public float FrmStrike1 { get; set; }

    public void Load(BinaryReader reader)
    {
        reader.ReadInt32(); // legacy string-pointer slot, kept for compatibility

        FrmStart = reader.ReadSingle();
        FrmEnd = reader.ReadSingle();
        FrmPerSec = reader.ReadSingle();
        FrmPlugTraceStart = reader.ReadSingle();
        FrmPlugTraceEnd = reader.ReadSingle();
        FrmSound0 = reader.ReadSingle();
        FrmSound1 = reader.ReadSingle();
        TimeBlend = reader.ReadSingle();
        BlendFlags = reader.ReadInt32();
        FrmStrike0 = reader.ReadSingle();
        FrmStrike1 = reader.ReadSingle();

        int nameLength = reader.ReadInt32();
        if (nameLength > 0)
        {
            byte[] bytes = reader.ReadBytes(nameLength);
            if (bytes.Length != nameLength)
                throw new EndOfStreamException("__AnimData name is truncated");
            Name = KoEncoding.Cp949.GetString(bytes);
        }
        else
        {
            Name = string.Empty;
        }
    }

    public void Save(BinaryWriter writer)
    {
        writer.Write(0); // legacy string-pointer slot

        writer.Write(FrmStart);
        writer.Write(FrmEnd);
        writer.Write(FrmPerSec);
        writer.Write(FrmPlugTraceStart);
        writer.Write(FrmPlugTraceEnd);
        writer.Write(FrmSound0);
        writer.Write(FrmSound1);
        writer.Write(TimeBlend);
        writer.Write(BlendFlags);
        writer.Write(FrmStrike0);
        writer.Write(FrmStrike1);

        byte[] bytes = KoEncoding.Cp949.GetBytes(Name);
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }
}

/// <summary>
/// Port of <c>CN3AnimControl</c> (Client/N3Base/N3AnimControl.cpp) — the
/// .n3anim clip table. Quirk kept verbatim: although the class derives from
/// CN3BaseFileAccess, its Load does NOT call the base loader, so the file
/// starts directly with the clip count (no name header).
/// </summary>
public sealed class N3AnimControl : N3BaseFile
{
    public List<N3AnimData> Clips { get; } = [];

    public override void Load(BinaryReader reader)
    {
        // No base.Load — CN3AnimControl::Load skips the name header.
        Clips.Clear();
        int count = reader.ReadInt32();
        for (int i = 0; i < count; i++)
        {
            var data = new N3AnimData();
            data.Load(reader);
            Clips.Add(data);
        }
    }

    public override void Save(BinaryWriter writer)
    {
        // No base.Save — mirrors the loader.
        writer.Write(Clips.Count);
        foreach (N3AnimData clip in Clips)
            clip.Save(writer);
    }
}

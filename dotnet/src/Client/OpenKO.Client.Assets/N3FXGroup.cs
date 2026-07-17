using System.Collections.Generic;

namespace OpenKO.Client.Assets;

/// <summary>
/// Port of <c>CN3FXGroup</c> (Client/N3Base/N3FXGroup.cpp) — a <c>.fxg</c> file:
/// a version int and a list of <see cref="FxbInfo"/> entries, each a raw 268-byte
/// struct.
/// <para>
/// Like the bundle, the group's <c>Load</c> does NOT read a [len][name] header —
/// it reads <c>m_iVersion</c> (int) first — so <see cref="Load"/>/<see cref="Save"/>
/// do not call the base header IO.
/// </para>
/// </summary>
public sealed class N3FXGroup : N3BaseFile
{
    public int Version { get; set; } = 1;

    public List<FxbInfo> Bundles { get; } = [];

    public override void Load(BinaryReader reader)
    {
        // NOTE: no base.Load — the group has no [len][name] header.
        Version = reader.ReadInt32();

        int count = reader.ReadInt32();
        Bundles.Clear();
        for (int i = 0; i < count; i++)
        {
            var info = new FxbInfo();
            info.Load(reader);
            Bundles.Add(info);
        }
    }

    public override void Save(BinaryWriter writer)
    {
        writer.Write(Version);
        writer.Write(Bundles.Count);
        foreach (FxbInfo info in Bundles)
            info.Save(writer);
    }
}

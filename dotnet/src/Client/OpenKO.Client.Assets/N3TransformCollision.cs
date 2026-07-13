namespace OpenKO.Client.Assets;

/// <summary>
/// Port of <c>CN3TransformCollision</c> (Client/N3Base/N3TransformCollision.cpp):
/// a transform plus the names of the collision and climb VMeshes. The C++
/// resolves the names through its resource managers; the asset library keeps
/// the names so the caller can load the referenced .n3vmesh files.
/// </summary>
public class N3TransformCollision : N3Transform
{
    public string CollisionMeshFileName { get; set; } = string.Empty;

    public string ClimbMeshFileName { get; set; } = string.Empty;

    public override void Load(BinaryReader reader)
    {
        base.Load(reader);

        CollisionMeshFileName = reader.ReadN3FileName();
        ClimbMeshFileName = reader.ReadN3FileName();
    }

    public override void Save(BinaryWriter writer)
    {
        base.Save(writer);

        writer.WriteN3FileName(CollisionMeshFileName);
        writer.WriteN3FileName(ClimbMeshFileName);
    }
}

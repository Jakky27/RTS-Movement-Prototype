using Unity.Entities;

[GenerateAuthoringComponent]
public struct AABBPositionHash : IComponentData
{
    public uint MinPosHash;
    public uint MaxPosHash;
}

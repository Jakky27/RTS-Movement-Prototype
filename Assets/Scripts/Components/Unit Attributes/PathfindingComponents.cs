using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

public struct PathfindingIndex : IComponentData
{
    public uint Value;
}

[InternalBufferCapacity(20)]
public struct PathfindingPath : IBufferElementData
{
    public float3 Position;
}
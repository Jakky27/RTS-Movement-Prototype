using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

[Serializable]
[InternalBufferCapacity(125)]
public struct AssignmentSlots : IBufferElementData
{
    float3 Position;
}

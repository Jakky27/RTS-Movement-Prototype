using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;

[GenerateAuthoringComponent]
public struct Velocity : IComponentData
{
    public float3 Value;
}

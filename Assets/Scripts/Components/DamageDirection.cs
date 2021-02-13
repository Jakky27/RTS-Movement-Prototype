using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;

[Serializable]
public struct DamageDirection : IComponentData
{
    public float2 Value;
}
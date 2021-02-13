using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;

[Serializable]
[MaterialProperty("_Color", MaterialPropertyFormat.Float4)]
public struct Template : IComponentData
{
    public float4 Value;
}

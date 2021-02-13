using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;

[Serializable]
public struct Damage : IComponentData
{
    public float Value;
} 
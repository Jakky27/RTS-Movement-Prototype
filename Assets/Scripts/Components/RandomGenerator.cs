using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Random = Unity.Mathematics.Random;

/// <summary>
/// Just a wrapper around mathematics.Random struct
/// </summary>
[Serializable]
[MaterialProperty("_Random", MaterialPropertyFormat.Float)]
public struct RandomGenerator : IComponentData
{
    public float Value;
}

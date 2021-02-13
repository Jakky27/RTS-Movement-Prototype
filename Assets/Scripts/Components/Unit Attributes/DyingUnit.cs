using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

[Serializable]
public struct DyingUnit : IComponentData
{
    public float TimeToExpire;
}

using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;

[Serializable]
[MaterialProperty("_Damage", MaterialPropertyFormat.Float)]
public struct DamagedCooldown : IComponentData
{
    public float Duration;
}
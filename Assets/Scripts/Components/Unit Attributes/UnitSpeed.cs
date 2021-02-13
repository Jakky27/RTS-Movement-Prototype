using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Rendering;

[GenerateAuthoringComponent]
[MaterialProperty("_Speed", MaterialPropertyFormat.Float)]
public struct UnitSpeed : IComponentData
{
    public float Value;
}
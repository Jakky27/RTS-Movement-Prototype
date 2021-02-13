using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

[Serializable]
public struct SelectedFormationSharedComponent : ISharedComponentData
{
    public bool IsSelected;
}
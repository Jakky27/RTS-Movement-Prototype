using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

[Serializable]
public struct AngularVelocity : IComponentData
{
    /// <summary>
    /// Angular velocity in radians
    /// </summary>
    public float Value;
}

using System.Collections;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;


public class AngularVelocityAuthoring : MonoBehaviour, IConvertGameObjectToEntity
{
    /// <summary>
    /// Rotation speed in degrees
    /// </summary>
    public float RotationSpeed;
    
    public void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
    {
        dstManager.AddComponentData(entity, new AngularVelocity {Value = math.radians(RotationSpeed)});
    }
}
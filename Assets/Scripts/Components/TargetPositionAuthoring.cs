using System;
using System.Numerics;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

[DisallowMultipleComponent]
public class TargetPositionAuthoring : MonoBehaviour, IConvertGameObjectToEntity {

    [Header("Initialization ")]
    public bool useGameobjectPosition = false;
    [DrawIf("useGameobjectPosition", false, DrawIfAttribute.DisablingType.ReadOnly)]
    public UnityEngine.Vector3 targetPosition = UnityEngine.Vector3.zero;

    public void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem) {

        if (useGameobjectPosition) {
            dstManager.AddComponentData(entity, new TargetPosition { Value = transform.position });
        } else {
            dstManager.AddComponentData(entity, new TargetPosition { Value = targetPosition });
        }
    }
}
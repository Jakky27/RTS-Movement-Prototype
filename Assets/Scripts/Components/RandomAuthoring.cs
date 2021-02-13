using System;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

[DisallowMultipleComponent]
public class RandomAuthoring : MonoBehaviour, IConvertGameObjectToEntity
{
    private static Unity.Mathematics.Random rnd;
    

    private void Awake()
    {
        rnd.InitState();
    }


    public void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
    {
        
        dstManager.AddComponentData(entity, new RandomGenerator { Value = UnityEngine.Random.Range(0f, 1f) });
        //dstManager.AddComponentData(entity, new RandomGenerator { Value = rnd.NextFloat() });
        
    }
}

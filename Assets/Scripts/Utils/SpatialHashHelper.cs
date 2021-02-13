using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

// TODO float3 for position is not needed (only using x and z components)

[BurstCompile]
public static class SpatialHashHelper {
    
    [BurstCompile]
    public static uint Hash(in float3 position, float conversionFactor, uint width)
    {
        return (uint)(position.x * conversionFactor) + (uint)(position.z * conversionFactor) * width;

        #region Slower version? (with divison and floor) (it shouldn't be slow if you change float accuracy)
        
        //return (uint)(position.x / conversionFactor) + (uint)((position.y / conversionFactor) * width);

        //return (uint)math.floor(position.x / conversionFactor) + (uint)(math.floor(position.y / conversionFactor) * width);
        
        #endregion
    }

}
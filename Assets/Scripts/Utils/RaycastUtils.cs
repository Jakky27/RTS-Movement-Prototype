using System;
using System.Text;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Physics;
using UnityEditor;
using UnityEngine;
using RaycastHit = Unity.Physics.RaycastHit;

public class RaycastUtils { 

    #region Physics Collision Queries
    
    [BurstCompile]
    private struct RaycastJob : IJobParallelFor
    {
        [ReadOnly] public CollisionWorld World;
        [ReadOnly] public NativeArray<RaycastInput> Inputs;
        public NativeArray<RaycastHit> Results;
        
        public unsafe void Execute(int index)
        {
            World.CastRay(Inputs[index], out var hit);
            Results[index] = hit;
        }
    }
    [BurstCompile]
    public static JobHandle ScheduleBatchRayCast(CollisionWorld world, NativeArray<RaycastInput> inputs, NativeArray<RaycastHit> results, JobHandle inputDeps) {
        JobHandle rcj = new RaycastJob {
            Inputs = inputs,
            Results = results,
            World = world,
        }.Schedule(inputs.Length, 4, inputDeps);

        return rcj;
    }
    
    [BurstCompile]
    public static void SingleRayCast(CollisionWorld world, RaycastInput input, ref RaycastHit result, JobHandle inputDeps) {
        NativeArray<RaycastInput> rayCommands = new NativeArray<RaycastInput>(1, Allocator.TempJob);
        NativeArray<RaycastHit> rayResults = new NativeArray<RaycastHit>(1, Allocator.TempJob);
        rayCommands[0] = input;
        var handle = ScheduleBatchRayCast(world, rayCommands, rayResults, inputDeps);
        handle.Complete();
        result = rayResults[0];
        rayCommands.Dispose();
        rayResults.Dispose();
    }

    // Same as RaycastJob but only returns the position of the raycast hit instead of the entire raycast hit
    [BurstCompile]
    private struct RaycastPositionJob : IJobParallelFor {
        [ReadOnly] public CollisionWorld World;
        [ReadOnly] public NativeArray<RaycastInput> Inputs;
        public NativeArray<float3> Results;

        public unsafe void Execute(int index) {
            World.CastRay(Inputs[index], out var hit);
            Results[index] = hit.Position;
        }
    }
    
    [BurstCompile]
    public static JobHandle ScheduleBatchRayCastPosition(CollisionWorld world, NativeArray<RaycastInput> inputs, NativeArray<float3> results, JobHandle inputDeps, out bool containsFailures) {
        var hasFailed = new NativeArray<bool>(1, Allocator.TempJob);

        JobHandle rcj = new RaycastPositionJob {
            Inputs = inputs,
            Results = results,
            World = world

        }.Schedule(inputs.Length, 4, inputDeps);

        containsFailures = hasFailed[0];
        hasFailed.Dispose();

        return rcj;
    }
    
    [BurstCompile]
    private struct AabbCollisionQueryJob : IJobParallelFor {
        [ReadOnly] public CollisionWorld World;
        [ReadOnly] public NativeArray<ColliderCastInput> Inputs;
        public NativeArray<ColliderCastHit> Results;

        public unsafe void Execute(int index) {
            World.CastCollider(Inputs[index], out var hit);
            Results[index] = hit;
        }
    }
    
    [BurstCompile]
    public static JobHandle ScheduleBatchColliderQuery(CollisionWorld world, NativeArray<ColliderCastInput> inputs, NativeArray<ColliderCastHit> results, JobHandle inputDeps) {
        JobHandle rcj = new AabbCollisionQueryJob {
            Inputs = inputs,
            Results = results,
            World = world

        }.Schedule(inputs.Length, 4, inputDeps);

        return rcj;
    }


    
    #endregion

    public static FixedString4096 NativeArrayToString<T>(NativeArray<T> arr) where T : unmanaged
    {
        var str = new FixedString4096();

        foreach (var val in arr)
        {
            str.Append(val.ToString());
            str.Append("\n");
        }

        return str;
    }
    
    [BurstCompile]
    public static void DrawPlane(in float3 position, in float3 offset, in Color color, float duration ) {
 
        var corner0 = position + offset;
        var corner1 = position + new float3(-offset.x, offset.y, offset.z);
        var corner2 = position + new float3(-offset.x, offset.y, -offset.z);;
        var corner3 = position + new float3(offset.x, offset.y, -offset.z);
 
        Debug.DrawLine(corner0, corner1, color, duration);
        Debug.DrawLine(corner1, corner2, color, duration);
        Debug.DrawLine(corner2, corner3, color, duration);
        Debug.DrawLine(corner3, corner0, color, duration);
    }
    
}

#if UNITY_EDITOR

public static class GizmoDrawers
{
    [DrawGizmo(GizmoType.Active | GizmoType.NotInSelectionHierarchy)]
    public static void DrawGizmos(Vector3 center, Vector3 size)
    {
        Gizmos.DrawCube(center, size);
        
    }
}

#endif



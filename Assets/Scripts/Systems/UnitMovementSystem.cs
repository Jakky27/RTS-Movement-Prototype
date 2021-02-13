using System.Threading;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine.Experimental.AI;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Profiling;
using UnityEngine.ResourceManagement.AsyncOperations;

[UpdateInGroup(typeof(SimulationSystemGroup))]
public class UnitMovementSystem : SystemBase
{
    private NativeMultiHashMap<uint, Entity> _buckets;
    private BattleMapSettings _battleMapSettings;

    protected override void OnCreate() {
        base.OnCreate();
        Enabled = false;
        
        Addressables.LoadAssetAsync<BattleMapSettings>("Battle Map Settings")
            .Completed += handle =>
        {
            if (handle.Status == AsyncOperationStatus.Succeeded)
            {
                _battleMapSettings = handle.Result;
                //DebugShowBuckets();
                Enabled = true;
            }
            else
            {
                Debug.LogError("Failed to load battle map settings");
            }
        };
    }
    
    protected override void OnUpdate()
    {
        if (!_buckets.IsCreated) {
            return;
        }

        var buckets = _buckets;

        var deltaTime = Time.DeltaTime;

        // TEMP variables (should be stored elsewhere)
        var unitProximityRangeSq = 1f;
        var avoidSpeed = 2f;

        // Manual dependency because we're sharing a native collection (buckets)
        Dependency = JobHandle.CombineDependencies(Dependency,
            World.GetOrCreateSystem<UnitMovementPreparationSystem>().GetDependency());
        
        var ltwArr = GetComponentDataFromEntity<LocalToWorld>(true);
        
        Entities
            .WithName("UnitBoidJob")
            .WithReadOnly(buckets).WithReadOnly(ltwArr)
            .WithAll<UnitTag>()
            .ForEach((Entity unitEntity, ref Translation pos, ref Velocity vel, ref Rotation rot, in AABBPositionHash aabbPositionHash, in LocalToWorld ltw) =>
            {
                var unitPos = ltw.Position;

                var closestEntity = Entity.Null;
                var closestDistSq = float.MaxValue;
                var closestEntityPos = float3.zero;

                // Iterating through adjacent units, and finding the closest one
                // For AABB, we only need to check 1-4 cells
                uint minPosHash = aabbPositionHash.MinPosHash;
                uint maxPosHash = aabbPositionHash.MaxPosHash;

                
                if (minPosHash == maxPosHash)
                {
                    // Case 1: unit spans 1 cell
                    ProcessCell(buckets, ltwArr, unitProximityRangeSq, minPosHash, unitEntity, unitPos, ref closestEntity, ref closestDistSq, ref closestEntityPos);
                } else if (minPosHash == maxPosHash + 1)
                {
                    // Case 2: unit spans two horizontal cells
                    ProcessCell(buckets, ltwArr, unitProximityRangeSq, minPosHash, unitEntity, unitPos, ref closestEntity, ref closestDistSq, ref closestEntityPos);
                    ProcessCell(buckets, ltwArr, unitProximityRangeSq, maxPosHash, unitEntity, unitPos, ref closestEntity, ref closestDistSq, ref closestEntityPos);
                }
                else
                {
                    // Case 3: unit spans 4 cells
                    ProcessCell(buckets, ltwArr, unitProximityRangeSq, minPosHash, unitEntity, unitPos, ref closestEntity, ref closestDistSq, ref closestEntityPos);
                    ProcessCell(buckets, ltwArr, unitProximityRangeSq, maxPosHash, unitEntity, unitPos, ref closestEntity, ref closestDistSq, ref closestEntityPos);
                    ProcessCell(buckets, ltwArr, unitProximityRangeSq, minPosHash + 1, unitEntity, unitPos, ref closestEntity, ref closestDistSq, ref closestEntityPos);
                    ProcessCell(buckets, ltwArr, unitProximityRangeSq, maxPosHash - 1, unitEntity, unitPos, ref closestEntity, ref closestDistSq, ref closestEntityPos);
                }
                
                if (closestEntity != Entity.Null) {
                    var unitPos2D = new float2(ltw.Position.x, ltw.Position.z);
                    var closestEntityPos2D = new float2(closestEntityPos.x, closestEntityPos.z);

                    // Boid move current unit by its closest other unit's position (since they are inside the proximity range)
                    var direction2D = math.normalize(unitPos2D - closestEntityPos2D);
                    var avoidVelocity2D = direction2D * avoidSpeed * deltaTime;
                    var avoidVelocity3D = new float3(avoidVelocity2D.x, 0f, avoidVelocity2D.y);
                    pos.Value += avoidVelocity3D;
                    vel.Value += avoidVelocity3D;
                }
            }).ScheduleParallel();
        
    }

    /// <summary>
    /// Uses the given hash value to check the grid and returns the closest entity
    /// </summary>
    private static void ProcessCell(NativeMultiHashMap<uint, Entity> buckets, ComponentDataFromEntity<LocalToWorld> ltwArr, float unitProximityRangeSq, uint unitHash, Entity unitEntity, in float3 unitPos, ref Entity currClosestEntity, ref float closestDistSq, ref float3 closestEntityPos)
    {
        if (buckets.TryGetFirstValue(unitHash, out var currOtherUnitEntity, out var iterator))
        {
            do
            {
                if (currOtherUnitEntity != unitEntity)
                {
                    // Found an adjacent unit
                    var otherUnitPos = ltwArr[currOtherUnitEntity].Position;

                    var distApartSq = math.distancesq(unitPos, otherUnitPos);
                    if (distApartSq <= unitProximityRangeSq && distApartSq < closestDistSq)
                    {
                        currClosestEntity = currOtherUnitEntity;
                        closestDistSq = distApartSq;
                        closestEntityPos = otherUnitPos;
                    }
                }
            } while (buckets.TryGetNextValue(out currOtherUnitEntity, ref iterator));
        }
    }
    
    public void SetBucketsBuffer(NativeMultiHashMap<uint, Entity> buckets) {
        _buckets = buckets;
    }

    private void DebugShowBuckets()
    {
        for (int x = 0; x <= _battleMapSettings.MapSize.x; x += 10) {
            Debug.DrawLine(new Vector3(x * _battleMapSettings.CellSize, 0.1f, 0), new Vector3(x * _battleMapSettings.CellSize, 0f, _battleMapSettings.MapSize.x * _battleMapSettings.CellSize), Color.cyan, 50f);
        }
        
        for (int z = 0; z <= _battleMapSettings.MapSize.y; z += 10) {
            Debug.DrawLine(new Vector3(0, 0.1f, z * _battleMapSettings.CellSize), new Vector3(_battleMapSettings.MapSize.y * _battleMapSettings.CellSize, 0f, z * _battleMapSettings.CellSize), Color.cyan, 50f);
        }
    }

}



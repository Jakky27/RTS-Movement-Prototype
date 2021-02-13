using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Profiling;
using UnityEngine.ResourceManagement.AsyncOperations;

/// <summary>
/// Creates the spatial hash map and hash each unit inside of it
/// </summary>
[UpdateInGroup(typeof(InitializationSystemGroup))]
public class UnitMovementPreparationSystem : SystemBase
{
    private NativeMultiHashMap<uint, Entity> _buckets;
    
    private BattleMapSettings _battleMapSettings;
    private static float _conversionFactor;
    private static uint _cellWidth;

    private EntityQuery _unitQuery;

    protected override void OnDestroy() {
        base.OnDestroy();
        if (_buckets.IsCreated) {
            _buckets.Dispose();
        }
    }

    protected override void OnCreate() {
        base.OnCreate();

        Enabled = false;
        
        Addressables.LoadAssetAsync<BattleMapSettings>("Battle Map Settings")
            .Completed += handle =>
        {
            if (handle.Status == AsyncOperationStatus.Succeeded)
            {
                _battleMapSettings = handle.Result;
                _conversionFactor = 1 / _battleMapSettings.CellSize;
                _cellWidth = (uint) (_battleMapSettings.MapSize.x / _battleMapSettings.CellSize);
        
                var numBuckets = _battleMapSettings.MapSize.x * _battleMapSettings.MapSize.y;
                _buckets = new NativeMultiHashMap<uint, Entity>(numBuckets, Allocator.Persistent);
                World.GetOrCreateSystem<UnitMovementSystem>().SetBucketsBuffer(_buckets);
                Enabled = true;

            }
            else
            {
                Debug.LogError("Failed to load battle map settings");
            }
        };
        
    }

    public JobHandle GetDependency()
    {
        return Dependency;
    }

    protected override void OnUpdate()
    {

        // Alterantives between clearing the two buckets in a seperate thread

        /*
        NativeMultiHashMap<uint, Entity>.ParallelWriter buckets;
        JobHandle clearBucketJob;
        if (useFirstBuffer) {
            useFirstBuffer = false;
            var bufferBucket = _buckets2;
            clearBucketJob = Job.WithCode(() => {
                bufferBucket.Clear();
            })
                .WithName("ClearBucketsOneJob")
                .Schedule(Dependency);
            buckets = _buckets1.AsParallelWriter();
            World.GetOrCreateSystem<UnitMovementSystem>().SetBucketsBuffer(_buckets1);
        } else {
            useFirstBuffer = true; // Use the 1st buffer for the next OnUpdate()
            var bufferBucket = _buckets1;
            clearBucketJob = Job.WithCode(() => {
                bufferBucket.Clear();
            })
                .WithName("ClearBucketsTwoJob")
                .Schedule(Dependency);
            buckets = _buckets2.AsParallelWriter();
            World.GetOrCreateSystem<UnitMovementSystem>().SetBucketsBuffer(_buckets2);
        }
        */
        Profiler.BeginSample("Clear buckets");
        _buckets.Clear();
        Profiler.EndSample();
        var buckets = _buckets.AsParallelWriter();
        
        // Resize if needed
        _buckets.Capacity = math.max(_buckets.Capacity, _unitQuery.CalculateEntityCount());
        
        var conversionFactor = _conversionFactor;
        var cellWidth = _cellWidth;
        Entities
            .WithName("HashUnitsJob")
            .WithStoreEntityQueryInField(ref _unitQuery)
            .WithAll<UnitTag>()
            .WithChangeFilter<LocalToWorld>()
            .WithNativeDisableParallelForRestriction(buckets)
            .ForEach((Entity unitEntity, ref AABBPositionHash hash, in UnitDataBlobReference unitAABB, in LocalToWorld ltw) => {
                
                var aabb = unitAABB.Value;
                hash.MinPosHash = SpatialHashHelper.Hash(ltw.Position - aabb.Extents, conversionFactor, cellWidth);
                hash.MaxPosHash = SpatialHashHelper.Hash(ltw.Position + aabb.Extents, conversionFactor, cellWidth);
                var posHash = SpatialHashHelper.Hash(ltw.Position, conversionFactor, cellWidth);
                buckets.Add(posHash, unitEntity);
            }).ScheduleParallel();
    }




    // EFFICACY UNKNOWN
    [BurstCompile]
    private struct ClearBucketJob : IJobParallelFor {

        [NativeDisableParallelForRestriction]
        public NativeMultiHashMap<uint, Entity> Buckets;

        [ReadOnly]
        [DeallocateOnJobCompletion]
        public NativeArray<uint> Hashes;

        public void Execute(int index) {

            var key = Hashes[index];
            Buckets.Remove(key);

        }
    }
}

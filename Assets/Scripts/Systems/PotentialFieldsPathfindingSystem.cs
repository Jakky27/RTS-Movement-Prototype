using Pathfinding;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[AlwaysUpdateSystem]
public class PotentialFieldsPathfindingSystem : SystemBase
{
    private ComputeShader _potentialFieldsComputeShader;
    private ComputeBuffer _unitPositionsBuffer;
    
    private Transform _startNode;
    private Transform _endNode;

    private AStarPathfindingSystem _pathfindingSystem;
    
    protected override void OnCreate()
    {
        base.OnCreate();
        Enabled = false;
        
        Addressables.LoadAssetAsync<ComputeShader>("PotentialFieldsComputeShader")
            .Completed += handle =>
        {
            if (handle.Status == AsyncOperationStatus.Succeeded)
            {
                _potentialFieldsComputeShader = handle.Result;
                Enabled = true;
            }
            else
            {
                Debug.LogError("Failed to load PotentialFieldsComputeShader");
            }
        };

        _startNode = GameObject.Find("Start Node").transform;
        _endNode = GameObject.Find("End Node").transform;
        _pathfindingSystem = World.GetOrCreateSystem<AStarPathfindingSystem>();
        
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        
    }

    protected override void OnUpdate()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            var path = new NativeList<int2>(Allocator.TempJob);
            var handle = _pathfindingSystem.HierarchicalPathfind(ref path, _startNode.position, _endNode.position, Dependency);
            handle.Complete();
            path.Dispose();
        }
    }

    private void DrawPath()
    {
        
    }
}

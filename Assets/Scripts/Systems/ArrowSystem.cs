using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.Systems;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.VFX;
using Random = Unity.Mathematics.Random;
using RaycastHit = Unity.Physics.RaycastHit;

[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
[UpdateAfter(typeof(StepPhysicsWorld))] // Idk just guessing
public class ArrowSystem : SystemBase
{

    private EntityQuery _arrowQuery;

    private VisualEffect _arrowMoveVFX;
    private Texture2D _arrowMovePositionMap;

    private VisualEffect _arrowImpactVFX;
    private Texture2D _arrowImpactPositionMap;

    private int _numArrows;
    
    private uint _projectilesLayer;
    private CollisionFilter _projectilesCollisionFilter;

    private int _positionCountID;
    private int _positionMapID;
    private int _arrowMoveEvent;
    
    private BuildPhysicsWorld _physicsWorldSystem;
    private CollisionWorld _world;
    
    private EndFixedStepSimulationEntityCommandBufferSystem _endSimulationEcbSystem;


    protected override void OnCreate()
    {
        // TODO TEMP, use a more robust way to get a reference to a VFX
        _arrowMoveVFX = GameObject.Find("Arrow Move VFX").GetComponent<VisualEffect>();
        _arrowImpactVFX = GameObject.Find("Arrow Impact VFX").GetComponent<VisualEffect>();
        if (!_arrowMoveVFX || !_arrowImpactVFX)
        {
            Enabled = false;
            return;
        }
        _positionMapID = Shader.PropertyToID("PositionMap");
        _arrowMovePositionMap = (Texture2D) _arrowMoveVFX.GetTexture(_positionMapID);
        _arrowImpactPositionMap = (Texture2D) _arrowMoveVFX.GetTexture(_positionMapID);

        _positionCountID = Shader.PropertyToID("PositionCount");
        _arrowMoveEvent = Shader.PropertyToID("ArrowMoveEvent");
        
        
        _projectilesLayer = 1u << 3;
        _projectilesCollisionFilter = new CollisionFilter
        {
            BelongsTo = _projectilesLayer,
            CollidesWith = ~0u,
            GroupIndex = 0
        };


        _endSimulationEcbSystem = World.GetOrCreateSystem<EndFixedStepSimulationEntityCommandBufferSystem>();
    }

    protected override void OnUpdate()
    {
        var dt = Time.DeltaTime;
        _numArrows = _arrowQuery.CalculateEntityCount();

        PrepareArrowMoveVFX();
        
        Entities
            .WithName("ArrowMoveJob")
            .WithAll<Arrow>()
            .ForEach((ref Translation trans, ref Rotation rot, ref Velocity vel) =>
            {
                trans.Value += vel.Value * dt;
                vel.Value.y -= 9.81f * dt;
                rot.Value = quaternion.LookRotation(math.normalize(vel.Value), math.up());

            }).ScheduleParallel();

        ArrowMoveVFX();
        
        var physicsWorldSystem = World.GetExistingSystem<BuildPhysicsWorld>();
        var collisionWorld = physicsWorldSystem.PhysicsWorld.CollisionWorld;
        var arrowRaycastInputs = new NativeArray<RaycastInput>(_numArrows, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
        var projectilesCollisionFilter = _projectilesCollisionFilter;
        Entities
            .WithName("ArrowPrepareRaycastsJob")
            .WithStoreEntityQueryInField(ref _arrowQuery)
            .WithAll<Arrow>()
            .ForEach((int entityInQueryIndex, in Velocity vel, in Translation trans) =>
            {
                arrowRaycastInputs[entityInQueryIndex] = new RaycastInput
                {
                    Start = trans.Value,
                    End = trans.Value + vel.Value * dt,
                    Filter = projectilesCollisionFilter
                };
                //Debug.DrawLine(trans.Value, trans.Value + vel.Value * dt, Color.blue, 1f);
            }).ScheduleParallel();
        
        var ecb = _endSimulationEcbSystem.CreateCommandBuffer().AsParallelWriter();
        var arrowRaycastResults = new NativeArray<RaycastHit>(_numArrows, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
        var raycastsHandle = RaycastUtils.ScheduleBatchRayCast(collisionWorld, arrowRaycastInputs, arrowRaycastResults, Dependency);
        Dependency = JobHandle.CombineDependencies(raycastsHandle, Dependency);
        
        Entities
            .WithName("ArrowCollisionJob")
            .WithAll<Arrow>()
            .WithReadOnly(arrowRaycastResults)
            .WithDisposeOnCompletion(arrowRaycastResults)
            .ForEach((Entity arrowEntity, int entityInQueryIndex, in Velocity vel) =>
            {
                var raycastResult = arrowRaycastResults[entityInQueryIndex];
                var hitEntity = raycastResult.Entity;
                if (hitEntity != Entity.Null)
                {
                    //Debug.Log(raycastResult.Entity);
                    ecb.DestroyEntity(entityInQueryIndex, arrowEntity);
                    
                    if (HasComponent<UnitTag>(hitEntity))
                    {
                        ecb.AddComponent(entityInQueryIndex, hitEntity, new Damage {Value = 2f} );
                        ecb.AddComponent(entityInQueryIndex, hitEntity, new DamageDirection {Value = math.normalize(vel.Value.xz)} );
                    }
                }
                
            }).ScheduleParallel();

        arrowRaycastInputs.Dispose(Dependency);
        
        _endSimulationEcbSystem.AddJobHandleForProducer(Dependency);
        
        //Debug.Log(numArrows);
        var positionMapArr = _arrowMovePositionMap.GetRawTextureData<half4>();
        for (int i = 0; i < _numArrows; i++)
        {
            
            //Debug.Log(positionMapArr[i * 2]);
            //Debug.Log(positionMapArr[i * 2 + 1]);
            //Debug.Log(math.distance(positionMapArr[i * 2].xyz, positionMapArr[i * 2 + 1].xyz));
        }
    }

    private void PrepareArrowMoveVFX()
    {
        var positionMapArr = _arrowMovePositionMap.GetRawTextureData<half4>();
        Entities
            .WithName("ArrowPrepareVFXJob")
            .WithAll<Arrow>()
            .WithNativeDisableParallelForRestriction(positionMapArr)
            .ForEach((int entityInQueryIndex, in Translation trans) =>
            {
                //positionMapArr[entityInQueryIndex * 2 + 1] = new half4((half3) ltw.Position, (half) 0.5f);
                positionMapArr[entityInQueryIndex * 2 + 1] = new half4((half3) trans.Value, (half) 0.25f);

            }).ScheduleParallel();

    }

    private void ArrowMoveVFX()
    {
        _arrowMoveVFX.SetInt(_positionCountID, _numArrows);
        
        // [curr arrow position, prev arrow position, ... ]
        var positionMapArr = _arrowMovePositionMap.GetRawTextureData<half4>();
        Entities
            .WithName("ArrowMoveVFXJob")
            .WithNativeDisableParallelForRestriction(positionMapArr)
            .WithAll<Arrow>()
            .ForEach((int entityInQueryIndex, in Translation trans) =>
            {
                positionMapArr[entityInQueryIndex * 2] = new half4((half3)trans.Value, (half) 0.5f);

            }).ScheduleParallel();

        Job
            .WithCode(() =>
        {
            _arrowMovePositionMap.Apply();
            _arrowMoveVFX.SetInt(_positionCountID, _numArrows);
            _arrowMoveVFX.SendEvent(_arrowMoveEvent);
        }).WithoutBurst().Run();
    }
}

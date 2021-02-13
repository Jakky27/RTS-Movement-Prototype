using System;
using System.Collections;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Profiling;
using UnityEngine.VFX;

[UpdateAfter(typeof(UnitMovementSystem))]
[UpdateInGroup(typeof(SimulationSystemGroup))]
public class UnitMovingVFXSystem : SystemBase
{

    public VisualEffect WalkVFX;
    private int _numItems; // I think this represents the number of VFX positions we need to spawn per frame

    private float _particleSpawnTime = 0.2f;
    private float _timeLeft = 0.2f;
    private int resolution = 128 * 128;
    
    // R: X position
    // G: Y position
    // B: Z position
    // A: Nothing atm
    private Texture2D _positionsTexture; // 128x128 texture, so it supports 128*128 units

    protected override void OnCreate()
    {
        base.OnCreate();
        
        // TODO TEMP, use a more robust way to get a reference to a VFX
        var walkVFXObj = GameObject.Find("Walk_Smoke");
        if (!walkVFXObj)
        {
            Enabled = false;
            return;
        }
        WalkVFX = walkVFXObj.GetComponent<VisualEffect>();
        _positionsTexture = (Texture2D) WalkVFX.GetTexture("Positions Texture");
        
    }

    protected override void OnUpdate()
    {
        _timeLeft -= Time.DeltaTime;
        if (_timeLeft > 0f)
        {
            return;
        }
        _timeLeft = _particleSpawnTime;
        
        var positionTexture = _positionsTexture;
        var positionsList = new NativeList<half4>(resolution, Allocator.TempJob);
        var writer = positionsList.AsParallelWriter();

        // Walking Smoke VFX
        Entities
            .WithName("FillPositionsTextureArrayJob")
            .WithAll<UnitTag>()
            .ForEach((Entity unitEntity, int entityInQueryIndex, in LocalToWorld ltw, in UnitSpeed unitSpeed) =>
            {
                // If speed meets a certain threshold, then start spawning VFX
                var pos = ltw.Position;

                if (unitSpeed.Value > 1)
                {
                    var a = (half) 1; // TODO make the alpha channel ramp (speed will change how many particles spawn. Right now it's always 1)
                    half4 texColor = new half4((half) pos.x, (half) pos.y,(half) pos.z, a);
                    writer.AddNoResize(texColor);
                }
            }).ScheduleParallel();
        
        
        // Number of units moving fast enough to have smoke puffs
        CompleteDependency();
        _numItems = positionsList.Length;
        
        Profiler.BeginSample("UNITMOVESAMPLE");
        if (_numItems > 0)
        {
            var positions = positionTexture.GetRawTextureData<half4>();
            var handle = new FillPositionsTexture
            {
                PosiionsTextureArray = positions,
                Positions = positionsList
            }.Schedule(_numItems, 128, Dependency);
            handle.Complete();
            
            positionTexture.Apply();
            WalkVFX.SetInt("Num Items", _numItems);
            WalkVFX.SendEvent("Unit Move Event");
        }
        Profiler.EndSample();
        
        positionsList.Dispose();
    }

    [BurstCompile]
    private struct FillPositionsTexture : IJobParallelFor
    {
        [WriteOnly] public NativeArray<half4> PosiionsTextureArray;
        [ReadOnly] public NativeArray<half4> Positions;
        
        public void Execute(int index)
        {
            PosiionsTextureArray[index] = Positions[index];
        }
    }
}


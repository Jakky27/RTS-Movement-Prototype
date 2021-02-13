using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(UnitMovementSystem))]
[DisableAutoCreation]
public class UnitSelectionIndicatorSystem : SystemBase
{
    private EndSimulationEntityCommandBufferSystem _ecbSystem;
    private EntityQuery _selectedIndicatorsQuery;

    protected override void OnCreate()
    {
        base.OnCreate();
        
        _ecbSystem = World.GetOrCreateSystem<EndSimulationEntityCommandBufferSystem>();
        
        _selectedIndicatorsQuery = GetEntityQuery
        (
            ComponentType.ReadOnly(typeof(SelectedIndicatorTag))
        );
    }

    protected override void OnUpdate()
    {        


    }
}

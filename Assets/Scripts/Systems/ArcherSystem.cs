using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

public class ArcherSystem : SystemBase
{
    protected override void OnUpdate()
    {
        
        
        Entities
            .WithAll<UnitTag>()
            .ForEach((ref Translation translation, in Rotation rotation) => {
                
                
        }).Schedule();
    }
}


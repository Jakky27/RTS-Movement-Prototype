using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(SteeringSystem))]
public class MoveSystem : SystemBase
{
    protected override void OnUpdate()
    {        
        float deltaTime = Time.DeltaTime;

        Entities
            .WithName("MoveJob")
            .WithNone<DamagedCooldown, Arrow>()
            .ForEach((ref Translation translation, in Velocity velocity) =>
            {
                translation.Value += velocity.Value * deltaTime;
            }).ScheduleParallel(); 
        
        Entities
            .WithName("UpdateUnitSpeedJob")
            .WithAll<UnitTag>()
            .WithChangeFilter<Velocity>()
            .ForEach((ref UnitSpeed unitSpeed, in Velocity velocity) =>
            {
                unitSpeed.Value = math.length(velocity.Value);
            }).ScheduleParallel(); 
        
        Entities
            .WithName("DamagedPushbackJob")
            .WithAll<UnitTag, DamagedCooldown>()
            .ForEach((ref Translation translation, ref Velocity velocity) =>
            {
                // Adding drag and gravity
                //velocity.Value -= new float3(0f, 9.81f, 0f) * deltaTime;
                
                //translation.Value.y = math.max(translation.Value.y, 0f);

            }).ScheduleParallel(); 


    }
}

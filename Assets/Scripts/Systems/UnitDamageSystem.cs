using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;

[UpdateInGroup(typeof(LateSimulationSystemGroup))]
public class UnitDamageSystem : SystemBase
{
    private EndSimulationEntityCommandBufferSystem _endSimulationEntityCommandBufferSystem;

    protected override void OnCreate()
    {
        base.OnCreate();
        _endSimulationEntityCommandBufferSystem = World.GetOrCreateSystem<EndSimulationEntityCommandBufferSystem>();
    }

    protected override void OnUpdate()
    {
        var ecb = _endSimulationEntityCommandBufferSystem.CreateCommandBuffer().AsParallelWriter();

        var dt = Time.DeltaTime;
        
                
        #region TEMP DEBUG
        if (Input.GetKeyDown(KeyCode.P))
        {
            Entities
                .WithAll<UnitTag>()
                .ForEach((Entity unit, int entityInQueryIndex) =>
                {
                    var damage = new Damage {Value = 10f};
                    ecb.AddComponent(entityInQueryIndex, unit, damage);
                }).ScheduleParallel();
        }
        #endregion
            
        Entities
            .WithName("DamageUnitJob")
            .WithAll<UnitTag>()
            .ForEach((Entity unit, int entityInQueryIndex, ref Health health, ref Damage damage, in DamageDirection damageDirection) =>
            {
                var damageDir = damageDirection.Value;
                ecb.RemoveComponent<Damage>(entityInQueryIndex, unit);
                ecb.AddComponent(entityInQueryIndex, unit, new DamagedCooldown {Duration = 0.25f});
                health.Value -= damage.Value;

            }).ScheduleParallel();

        Entities
            .WithName("DamageCooldownJob")
            .WithAll<UnitTag>()
            .ForEach((Entity unit, int entityInQueryIndex, ref DamagedCooldown damagedCooldown, ref Translation trans, in DamageDirection damageDirection) =>
            {
                damagedCooldown.Duration -= dt;
                var damageDir = damageDirection.Value;
                
                trans.Value += new float3(damageDir.x, 0f, damageDir.y) * dt;
                
                if (damagedCooldown.Duration < 0f)
                {
                    ecb.RemoveComponent<DamagedCooldown>(entityInQueryIndex, unit);
                    ecb.RemoveComponent<DamageDirection>(entityInQueryIndex, unit);
                }
            }).ScheduleParallel();

        Entities
            .WithName("DyingJob")
            .WithAll<UnitTag>()
            .ForEach((Entity unit, int entityInQueryIndex, ref DyingUnit dying) =>
            {

                dying.TimeToExpire -= dt;   

                if (dying.TimeToExpire < 0f)
                {
                    ecb.DestroyEntity(entityInQueryIndex, unit);
                }

            }).ScheduleParallel();
        
        _endSimulationEntityCommandBufferSystem.AddJobHandleForProducer(Dependency);
    }
}

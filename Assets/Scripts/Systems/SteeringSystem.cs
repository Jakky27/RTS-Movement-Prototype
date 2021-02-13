using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;
using Random = Unity.Mathematics.Random;

[UpdateBefore(typeof(MoveSystem))]
public class SteeringSystem : SystemBase
{

    protected override void OnUpdate() {
        float deltaTime = Time.DeltaTime;

        // TODO move these variables elsewhere (probably access it from unit data blob reference)
        float satisfactionRadius = 0.2f;
        
        float maxAcceleration = 3f;
        float maxSpeed = 3f;

        float slowingRadius = maxSpeed * 2; // Slowing distance radius should equal to max speed
        
        Entities
            .WithName("SeekingJob")
            .WithNone<DamagedCooldown>()
            .ForEach((ref Velocity velocity, ref Rotation rotation, in LocalToWorld ltw, in TargetPosition targetPos) =>
            {
                var position = ltw.Position;

                float3 targetDirection = targetPos.Value - position;
                float dist = math.length(targetDirection);
                
                // Reaching target position 
                if (dist < satisfactionRadius && math.length(velocity.Value) < 0.2f)
                {
                    velocity.Value = 0f;
                    return;
                }
                
                // Arrival or go at max speed
                float targetSpeed = math.select(maxSpeed * (dist / slowingRadius), maxSpeed, dist > slowingRadius);

                var targetVelocity = math.normalize(targetDirection) * targetSpeed;
                
                // Get acceleration
                var acceleration = targetVelocity - velocity.Value;
                if (math.length(acceleration) > maxAcceleration)
                {
                    acceleration = math.normalize(acceleration) * maxAcceleration;
                }
                
                velocity.Value += acceleration * deltaTime;
                if (math.length(velocity.Value) > maxSpeed)
                {
                    velocity.Value = math.normalize(velocity.Value);
                    velocity.Value *= maxSpeed;
                }
                
                rotation.Value = quaternion.LookRotation(new float3(velocity.Value.x, 0f, velocity.Value.z), math.up());

                
            }).ScheduleParallel();
    }
    
    /// <summary>
    /// Rotate <paramref name="point"/> around <paramref name="rotationCenter"/> by <paramref name="degree"/>
    /// </summary>
    /// <param name="point">Point for rotation</param>
    /// <param name="rotationCenter">Point around which <paramref name="point"/> will be rotated</param>
    /// <param name="degree">Rotation degrees amount</param>
    /// <returns>Rotated point position</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float2 GetRotatedPoint(in float2 point, in float2 rotationCenter, float degree)
    {
        float radians = math.radians(-degree);
        float2 distance = point - rotationCenter;

        return new float2(math.cos(radians) * distance.x - math.sin(radians) * distance.y + rotationCenter.x,
            math.sin(radians) * distance.x + math.cos(radians) * distance.y + rotationCenter.y);
    }

}

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

public class OrbFormationSystem : SystemBase
{
    const float TWO_PI = 2 * math.PI;
    
    protected override void OnUpdate()
    {
        // TEMP VALUES
        float distanceApart = 1f;

        Entities
            .WithAll<OrbFormationTag>()
            .ForEach((ref TargetPosition targetPos, in FormationIndex formationIndex) => {

                int index = formationIndex.Index;

                var entityCount = 120; // TODO get actual formation unit count
                float radius = distanceApart * entityCount / TWO_PI;
                float radians = TWO_PI * index / entityCount;

                float x = math.sin(radians) * radius;
                float z = math.cos(radians) * radius;

                targetPos.Value = new float3(x, 0, z);

            }).ScheduleParallel();
    }
}

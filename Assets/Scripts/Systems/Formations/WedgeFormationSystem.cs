using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

public class WedgeFormationSystem : SystemBase
{
    protected override void OnUpdate()
    {
        // TEMP VALUES
        float distanceApart = 1f;

        Entities
            .WithAll<WedgeFormationTag>()
            .ForEach((ref TargetPosition targetPos, in FormationIndex formationIndex) => {

                // TODO make the units start in the middle of the formation (imagine if you only had ~3 units)

                int index = formationIndex.Index + 2;
                float z = index / 2 * distanceApart;
                float x = -z + 2 * z * (index % 2);

                targetPos.Value = new float3(x, 0, -z);

            }).ScheduleParallel();
    }
}

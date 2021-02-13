using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

[DisableAutoCreation]
public class TestudoFormationSystem : SystemBase
{
    protected override void OnUpdate()
    {
        // TEMP VALUES
        float distanceApart = .5f;
        float width = 10f;

        // TODO schedule raycasts 

        Entities
            .WithAll<TestudoFormationTag>()
            .ForEach((ref TargetPosition targetPos, in FormationIndex formationIndex) => {

                // TODO make the units start in the middle of the formation (imagine if you only had ~3 units)

                int index = formationIndex.Index;
                float midX = (width -1) / 2f;
                float x = index % width - midX;
                float z = index / width + 1;

                targetPos.Value = new float3(x * distanceApart, 0, -z * distanceApart);
                
        }).ScheduleParallel();


        // Run raycasts to get proper elevation of the units on the terrain

    }
}

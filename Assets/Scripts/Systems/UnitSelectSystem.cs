using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Permissions;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

[UpdateInGroup(typeof(SimulationSystemGroup))]
public class UnitSelectSystem : SystemBase
{
    public NativeArray<bool> IsSelectedBuffer;
    public NativeList<FormationGroup> SelectedFormationGroups;

    private EntityQuery _unitQuery;
    private EntityQuery _selectedIndicatorQuery;

    protected override void OnCreate() {
        base.OnCreate();

        _unitQuery = GetEntityQuery(
            ComponentType.ReadOnly(typeof(UnitTag)),
            ComponentType.ReadOnly(typeof(FormationGroup))
        );

        _selectedIndicatorQuery = GetEntityQuery(
            ComponentType.ReadOnly(typeof(SelectedIndicatorTag)),
            ComponentType.ReadOnly(typeof(FormationGroup))
        );

    }

    protected override void OnDestroy() {
        base.OnDestroy();

        if (IsSelectedBuffer.IsCreated) {
            IsSelectedBuffer.Dispose();
        }
        if (SelectedFormationGroups.IsCreated)
        {
            SelectedFormationGroups.Dispose();
        }
    }

    protected override void OnUpdate()
    {

        if (!IsSelectedBuffer.IsCreated) {
            return;
        }

        // TEMP stupid way of "cancelling" this job early when a unit in the same formation has been selected
        // NOTE: due to running on multiple threads, there is a race condition where the job will write to the array multiple times (but no side effects)
        var formationGroups = new List<FormationGroup>();
        EntityManager.GetAllUniqueSharedComponentData(formationGroups);

        if (SelectedFormationGroups.IsCreated)
        {
            SelectedFormationGroups.Dispose();
        }
        SelectedFormationGroups = new NativeList<FormationGroup>(40, Allocator.TempJob);

        // For all selected units, activate their child selected indicator
        foreach (var formation in formationGroups) {

            // Setting up queries 
            
            _unitQuery.SetSharedComponentFilter(formation); // I saw this in an update loop in Unity documentation
            _selectedIndicatorQuery.SetSharedComponentFilter(formation);

            if (IsSelectedBuffer[formation.ID]) {
                EntityManager.SetSharedComponentData(_unitQuery, new SelectedFormationSharedComponent { IsSelected = true });
                EntityManager.SetSharedComponentData(_selectedIndicatorQuery, new SelectedFormationSharedComponent { IsSelected = true });
                EntityManager.RemoveComponent(_selectedIndicatorQuery, typeof(DisableRendering));
                SelectedFormationGroups.Add(formation);
            } else {
                EntityManager.SetSharedComponentData(_unitQuery, new SelectedFormationSharedComponent { IsSelected = false });
                EntityManager.SetSharedComponentData(_selectedIndicatorQuery, new SelectedFormationSharedComponent { IsSelected = false });
                EntityManager.AddComponent(_selectedIndicatorQuery, typeof(DisableRendering));
            }
        }
        formationGroups.Clear();

        IsSelectedBuffer.Dispose();
    }

    public void SetBuffer(NativeArray<bool> isUnitSelectedArr) {
        if (IsSelectedBuffer.IsCreated) {
            IsSelectedBuffer.Dispose();
        }

        IsSelectedBuffer = isUnitSelectedArr;
    }
}

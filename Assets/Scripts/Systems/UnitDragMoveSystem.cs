using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Entities.UniversalDelegates;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.Systems;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.UIElements;

[UpdateAfter(typeof(UnitSelectSystem))]
[AlwaysUpdateSystem]
public class UnitDragMoveSystem : SystemBase {
    private float3 _dragStartPos;
    private EndSimulationEntityCommandBufferSystem _endSimulationEcbSystem;

    private float _distanceApart = 2f;

    private bool _isDragging = false;
    public bool isValidDrag = false;

    private BuildPhysicsWorld _physicsWorldSystem;
    private CollisionWorld _world;

    private int MAX_FORMATION_WIDTH = 100;
    private int MIN_FORMATION_WIDTH = 5;

    // Array of array, containing all the selected units raycast positions
    private NativeArray<float3> TEMP_SLOT_ASSIGNMENTS;
    
    private float3 _dragForward;
    
    private EntityQuery _unitQuery;
    private EntityQuery _selectedUnitQuery; // Queries for every selected unit
    private EntityQuery _tempSelectedUnitQuery; // Queries for every selected unit within a formation group

    private EntityQuery _selectedFormationUnitQuery; // Queries for a specific selected formation
    private EntityQuery _unitDragIndicatorQuery;
    private EntityQuery _disabledDragIndicatorQuery;
    private EntityQuery _selectedDragIndicatorQuery;
    private EntityQuery _enabledDragIndicatorQuery;

    private uint _walkableLayer;

    private UnitSelectionPrepSystem _unitSelectSystem;
    private int numUnitsPerRow;


    protected override void OnCreate() {
        _endSimulationEcbSystem = World.GetOrCreateSystem<EndSimulationEntityCommandBufferSystem>();
        _unitSelectSystem = World.GetOrCreateSystem<UnitSelectionPrepSystem>();

        _walkableLayer = 1u << 0;

        _unitQuery = GetEntityQuery (
            ComponentType.ReadOnly(typeof(UnitTag)),
            ComponentType.ReadWrite(typeof(TargetPosition)),
            ComponentType.ReadOnly(typeof(FormationIndex)),
            ComponentType.ReadOnly(typeof(Translation))
        );

        _selectedUnitQuery = GetEntityQuery(
            ComponentType.ReadOnly(typeof(UnitTag)),
            ComponentType.ReadOnly(typeof(TargetPosition)),
            ComponentType.ReadOnly(typeof(Translation)),
            ComponentType.ReadOnly(typeof(FormationIndex)),
            ComponentType.ReadOnly(typeof(SelectedFormationSharedComponent)),
            ComponentType.ReadOnly(typeof(FormationGroup))
        );
        _selectedUnitQuery.AddSharedComponentFilter(new SelectedFormationSharedComponent { IsSelected = true });
        
        
        _selectedFormationUnitQuery = GetEntityQuery(
            ComponentType.ReadOnly(typeof(UnitTag)),
            ComponentType.ReadOnly(typeof(FormationIndex)),
            ComponentType.ReadOnly(typeof(SelectedFormationSharedComponent)),
            ComponentType.ReadOnly(typeof(Translation))
        );
        _selectedFormationUnitQuery.AddSharedComponentFilter(new SelectedFormationSharedComponent { IsSelected = true });

        
        _unitDragIndicatorQuery = GetEntityQuery (
            ComponentType.ReadOnly(typeof(DragIndicatorTag))
        );

        _selectedDragIndicatorQuery = GetEntityQuery(
            ComponentType.ReadOnly(typeof(DragIndicatorTag)),
            ComponentType.ReadWrite(typeof(SelectedFormationSharedComponent))
        );
        _selectedDragIndicatorQuery.AddSharedComponentFilter(new SelectedFormationSharedComponent { IsSelected = true });

        var enabledDragIndicatorQueryDesc = new EntityQueryDesc {
            None = new ComponentType[] { typeof(DisableRendering) },
            All = new ComponentType[] { typeof(DragIndicatorTag) }
        };
        _enabledDragIndicatorQuery = GetEntityQuery(enabledDragIndicatorQueryDesc);

        _disabledDragIndicatorQuery = GetEntityQuery(
            ComponentType.ReadOnly(typeof(DragIndicatorTag)),
            ComponentType.ReadWrite(typeof(DisableRendering))
        );
    }

    protected override void OnDestroy() {
        base.OnDestroy();

        if (TEMP_SLOT_ASSIGNMENTS.IsCreated) {
            TEMP_SLOT_ASSIGNMENTS.Dispose();
        }
    }

    private Unity.Physics.RaycastHit SingleRaycast() {
        UnityEngine.Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        RaycastInput raycastInput = new RaycastInput {
            Start = ray.origin,
            End = ray.GetPoint(2000f),
            Filter = new CollisionFilter() {
                BelongsTo = ~0u,
                CollidesWith = ~0u, // all 1s, so all layers, collide with everything
                GroupIndex = 0
            }
        };
        Unity.Physics.RaycastHit hit = new Unity.Physics.RaycastHit();
        RaycastUtils.SingleRayCast(_world, raycastInput, ref hit, this.Dependency);
        return hit;
    }

    private void ShowSelectedDragIndicators() {
        EntityManager.RemoveComponent<DisableRendering>(_selectedDragIndicatorQuery);
    }

    private void ShowAllDragIndicators() {
        EntityManager.RemoveComponent<DisableRendering>(_disabledDragIndicatorQuery);
    }

    private void HideAllDragIndicators() {
        EntityManager.AddComponent<DisableRendering>(_enabledDragIndicatorQuery);
    }

    public struct EaseAndFormatiomIndex : IComparable<EaseAndFormatiomIndex> {

        public float easeOfAssignment;
        public Entity formationIndex;

        public EaseAndFormatiomIndex(float easeOfAssignment, Entity formationIndex) {
            this.easeOfAssignment = easeOfAssignment;
            this.formationIndex = formationIndex;
        }

        public int CompareTo(EaseAndFormatiomIndex other) {
            if (this.easeOfAssignment > other.easeOfAssignment) {
                return 1;
            } else {
                return -1;
            }
        }
    }

    public struct CostAndSlot : IComparable<CostAndSlot> {
        public float cost;
        public int slot;

        public CostAndSlot(float cost, int slot) {
            this.cost = cost;
            this.slot = slot;
        }

        public int CompareTo(CostAndSlot other) {
            if (this.cost > other.cost) {
                return 1;
            } else {
                return -1;
            }
        }
    }

    private NativeList<FormationGroup> GetSelectedFormations() {
        return World.GetOrCreateSystem<UnitSelectSystem>().SelectedFormationGroups;
    }

    private int GetSelectedUnitCount() {
        return _selectedUnitQuery.CalculateEntityCount();
    }
    protected override void OnUpdate()
    {
        _physicsWorldSystem = World.DefaultGameObjectInjectionWorld.GetExistingSystem<BuildPhysicsWorld>();
        _world = _physicsWorldSystem.PhysicsWorld.CollisionWorld;

        if (Input.GetMouseButtonDown(1) && !_isDragging)
        {
            Unity.Physics.RaycastHit hit = SingleRaycast();
            if (hit.Entity != Entity.Null) {
                // TODO this single raycast needs to hit a VALID terrain piece
                ShowAllDragIndicators();
                _dragStartPos = hit.Position;
                _isDragging = true;
            } else {
                HideAllDragIndicators();
                _isDragging = false;
            }
        }
        else if (_isDragging && Input.GetMouseButton(1))
        {
            Unity.Physics.RaycastHit hit = SingleRaycast();
            float3 dragEndPos = hit.Position;
            
            // Ignore dragging if some distance threshold is not met
            // Or if there are no selected formations
            if (hit.Entity == Entity.Null || math.distancesq(_dragStartPos, dragEndPos) < 5f) {
                isValidDrag = false;
                return;
            }

            // Check if its a valid drag for the # of selected formations
            var selectedFormations = GetSelectedFormations();
            if (!selectedFormations.IsCreated || selectedFormations.Length == 0)
            {
                Debug.Log("No formations selected");
                return;
            }
            
            var numSelectedFormations = selectedFormations.Length;
            float dragDist = math.distance(_dragStartPos, dragEndPos);

            // TODO formation width may NOT be evenly split 
            float formationWidth = dragDist / numSelectedFormations; // Width for each formation (evenly split)
            if (formationWidth < MIN_FORMATION_WIDTH)
            {
                return;
            }
            
            // Red line to show the width of drag
            //Debug.DrawLine(dragStartPos, dragEndPos, Color.red, 0f, true);

            // Preparing batch raycasts in between the two drag positions to get curvature of the terrain
            // TODO this can be jobified, right?
            float3 dragDir = math.normalize(dragEndPos - _dragStartPos);
            quaternion rotateFor = quaternion.AxisAngle(new float3(0f, 1f, 0f), math.radians(-90f));
            float3 dragFor = math.mul(rotateFor, dragDir);
            _dragForward = dragFor;
            
            var numSelectedUnits = _selectedFormationUnitQuery.CalculateEntityCount();
            if (!TEMP_SLOT_ASSIGNMENTS.IsCreated) {
                TEMP_SLOT_ASSIGNMENTS = new NativeArray<float3>(numSelectedUnits, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            } else if (TEMP_SLOT_ASSIGNMENTS.Length != numSelectedUnits) {
                // Very common case to have to resize 
                TEMP_SLOT_ASSIGNMENTS.Dispose();
                TEMP_SLOT_ASSIGNMENTS = new NativeArray<float3>(numSelectedUnits, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            }
            
            var raycastInputs = new NativeArray<RaycastInput>(numSelectedUnits, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            var formationUnitIndices = new NativeArray<int>(numSelectedFormations, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            
            // TODO Calculate centroid of each formation. Assign formations accordingly. 

            var currUnitIndex = 0;
            for (int i = 0; i < selectedFormations.Length; i++)
            {
                var currFormation = selectedFormations[i];
                
                // Filters to an existing query to calculate unit count for one formation
                _selectedUnitQuery.SetSharedComponentFilter(currFormation);
                int numUnits = _selectedUnitQuery.CalculateEntityCount();
                
                var raycastInputsSlice = raycastInputs.Slice(currUnitIndex, numUnits);
                
                numUnitsPerRow = math.clamp((int) (formationWidth / _distanceApart), MIN_FORMATION_WIDTH, MAX_FORMATION_WIDTH);
            
                // Draws the forward direction of the formation in white
                //Debug.DrawRay(math.lerp(dragStartPos, dragEndPos, 0.5f), dragFor);

                float3 startPoint = math.lerp(_dragStartPos, dragEndPos, (float)i / selectedFormations.Length);
                
                // TODO not working
                CollisionFilter raycastFilter = new CollisionFilter() {
                    BelongsTo = _walkableLayer,
                    CollidesWith = _walkableLayer,
                    GroupIndex = 0
                };

                
                #region Creating raycast inputs
                int levels = numUnits / numUnitsPerRow;
                float3 forwardOffset;
                for (int currLevel = 0; currLevel < levels; currLevel++) {
                    forwardOffset = dragFor * currLevel * _distanceApart;
                    for (int j = 0; j < numUnitsPerRow; j++) {
                        float3 sideOffset = dragDir * _distanceApart * j;
                        float3 currUnitPos = startPoint + sideOffset - forwardOffset;
                        raycastInputsSlice[j + (numUnitsPerRow * currLevel)] = new RaycastInput {
                            Start = currUnitPos + new float3(0f, 20f, 0f),
                            End = currUnitPos - new float3(0f, 20f, 0f),
                            Filter = raycastFilter
                        };
                        // DEBUG
                        //Debug.DrawLine(currUnitPos, currUnitPos + new float3(0f, 1f, 0f), Color.blue);
                    }
                }
                // For the remainder units in the back
                int calculatedUnits = numUnitsPerRow * levels; // The number of units we have finished calculating
                int remainderUnits = numUnits % numUnitsPerRow;
                float3 middlePos = (startPoint + (dragDir * numUnitsPerRow / 2 * _distanceApart)) - (dragDir * _distanceApart * remainderUnits / 2);
                forwardOffset = dragFor * levels * _distanceApart;
                for (int j = 0; j < remainderUnits; j++) {
                    float3 sideOffset = dragDir * _distanceApart * j;
                    float3 currUnitPos = middlePos + sideOffset - forwardOffset;
                    raycastInputsSlice[calculatedUnits + j] = new RaycastInput {
                        Start = currUnitPos + new float3(0f, 5f, 0f),
                        End = currUnitPos - new float3(0f, 5f, 0f),
                        Filter = raycastFilter
                    };

                    // DEBUG
                    //Debug.DrawLine(currUnitPos, currUnitPos + new float3(0f, 1f, 0f), Color.green);
                }

                formationUnitIndices[i] = currUnitIndex;
                currUnitIndex += numUnits;

                #endregion
            }

            NativeArray<float3> raycastResults = new NativeArray<float3>(numSelectedUnits, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            // Runs the batch of raycasts
            // TODO schedule these raycasts all at once. This is very slow
            var handle = RaycastUtils.ScheduleBatchRayCastPosition(_world, raycastInputs, raycastResults, Dependency, out var hasFailed);
            handle.Complete();
            raycastInputs.Dispose();

            if (hasFailed) {
                Debug.Log("Dragging failed, disposing raycast results");
                raycastResults.Dispose();
                return; 
            }
                
            ShowSelectedDragIndicators();

            var handles = new NativeArray<JobHandle>(selectedFormations.Length, Allocator.TempJob);

            var translations = GetComponentDataFromEntity<Translation>(false);
            var rotations = GetComponentDataFromEntity<Rotation>(false);
            for (int i = 0; i < selectedFormations.Length; i++)
            {
                var currFormation = selectedFormations[i];
                var unitPositionsSlice = raycastResults.Slice(formationUnitIndices[i]);
                
                handles[i] = Entities
                    .WithName("AlignUnitDragIndicators")
                    .WithReadOnly(unitPositionsSlice)
                    .WithAll<DragIndicatorTag, Translation, Rotation>()
                    .WithSharedComponentFilter(currFormation)
                    .WithNativeDisableContainerSafetyRestriction(translations)
                    .WithNativeDisableContainerSafetyRestriction(rotations)
                    .ForEach((Entity entity, int entityInQueryIndex) =>
                    {

                        translations[entity] = new Translation
                            {Value = unitPositionsSlice[entityInQueryIndex] + new float3(0f, 0.1f, 0f)};
                        rotations[entity] = new Rotation {Value = quaternion.LookRotation(dragFor, math.up())};

                    }).ScheduleParallel(Dependency);
            }
            
            new CopyFloat3Job
            {
                Output = TEMP_SLOT_ASSIGNMENTS,
                Input = raycastResults
            }.Schedule(raycastResults.Length, 128, JobHandle.CombineDependencies(handles)).Complete();
            
            isValidDrag = true;
            handles.Dispose();
            formationUnitIndices.Dispose();
        } 
        else if (isValidDrag && Input.GetMouseButtonUp(1)) {
            isValidDrag = false;
            
            EaseOfAssignmentUnitAssignment();
            //SpatialUnitAssignmnet();

        } 
        else {
            _isDragging = false;
            if (Input.GetKey(KeyCode.Space)) {
                ShowAllDragIndicators();
            } else {
                HideAllDragIndicators();
            }

        }
        
        _endSimulationEcbSystem.AddJobHandleForProducer(this.Dependency);
    }
    
    [BurstCompile]
    private struct CopyFloat3Job : IJobParallelFor
    {
        [WriteOnly] public NativeArray<float3> Output;
        [ReadOnly] [DeallocateOnJobCompletion] public NativeArray<float3> Input;
        
        public void Execute(int index)
        {
            Output[index] = Input[index];
        }
    }


    private void MouseHoverFormations()
    {
        if (!Input.GetMouseButton(1) && !_isDragging)
        {
            Unity.Physics.RaycastHit hit = SingleRaycast();
            if (hit.Entity != Entity.Null) {
                
            } else {
                
            }
        }
    }

    [BurstCompile]
    private struct BatchedCalculateAssignmentOrderJob : IJobParallelFor {

        [ReadOnly]
        [DeallocateOnJobCompletion]
        public NativeArray<Entity> unitEntities;

        [ReadOnly]
        [NativeDisableParallelForRestriction]
        [NativeDisableContainerSafetyRestriction]
        public ComponentDataFromEntity<Translation> translations;

        [ReadOnly]
        public NativeSlice<float3> slotPositions;

        [NativeDisableParallelForRestriction]
        public NativeArray<CostAndSlot> sortedCostsAndSlots;

        [NativeDisableParallelForRestriction]
        public NativeArray<EaseAndFormatiomIndex> sortedUnitAssignmentOrder;

        [ReadOnly]
        public int NumUnits;
        
        [WriteOnly]
        public NativeHashMap<Entity, int>.ParallelWriter EntityToIndex;

        
        public void Execute(int index) {
            
            int currLevel = index * NumUnits;
            var currEntity = unitEntities[index];
            EntityToIndex.TryAdd(currEntity, currLevel);
            
            var costAndSlotsSlice = sortedCostsAndSlots.Slice(currLevel, NumUnits);

            // Converts each cost into "ease of assignment", which is essentially the inverse of cost
            // "Ease of Assignment" = 1 / (1 + cost)  
            float easeOfAssignmentSum = 0;
            var currPosition = translations[currEntity].Value;

            for (int j = 0; j < NumUnits; j++) { // Iterating through every slot
                var cost = math.distancesq(currPosition, slotPositions[j]);
                easeOfAssignmentSum += 1 / (1 + cost);
                costAndSlotsSlice[j] = new CostAndSlot(cost, j);
            }
            sortedUnitAssignmentOrder[index] = new EaseAndFormatiomIndex(easeOfAssignmentSum, currEntity);

            // Sort the slice of costs that represents all the costs and slots for 1 unit
            costAndSlotsSlice.Sort(); // TODO explore 
        }
    }

    
    /// <summary>
    /// Fills the targetPositions array with every unit's optimal placement
    /// NOTE: this is ran a single thread the units have to pick their slots in order
    /// </summary>
    [BurstCompile]
    private struct UnitAssignmentJob : IJob {
        
        [WriteOnly]
        [NativeDisableParallelForRestriction]
        [NativeDisableContainerSafetyRestriction]
        public ComponentDataFromEntity<TargetPosition> targetPositions;

        [ReadOnly]
        [DeallocateOnJobCompletion]
        public NativeArray<EaseAndFormatiomIndex> sortedEaseOfassignmentFormationIndexPairs;

        [ReadOnly]
        [DeallocateOnJobCompletion]
        public NativeArray<CostAndSlot> sortedCostsAndSlots;

        [ReadOnly]
        public NativeSlice<float3> slotPositions;

        [DeallocateOnJobCompletion]
        [NativeDisableParallelForRestriction]
        public NativeArray<bool> filledSlots;

        [ReadOnly]
        public NativeHashMap<Entity, int> EntityToIndex;

        public void Execute() {

            var numUnits = sortedEaseOfassignmentFormationIndexPairs.Length;

            // Iterating through each formation index, sorted by ease of assignment
            for (int i = 0; i < numUnits; i++) {

                Entity currFormationIndex = sortedEaseOfassignmentFormationIndexPairs[i].formationIndex;
                
                //var currCostsAndSlots = sortedCostsAndSlots.Slice(numUnits * i, numUnits);
                var currCostsAndSlots = sortedCostsAndSlots.Slice(EntityToIndex[currFormationIndex], numUnits);

                for (int j = 0; j < currCostsAndSlots.Length; j++) {
                    var costAndSlot = currCostsAndSlots[j];
                    if (!filledSlots[costAndSlot.slot]) {
                        targetPositions[currFormationIndex] = new TargetPosition { Value = slotPositions[costAndSlot.slot] };
                        filledSlots[costAndSlot.slot] = true;
                        break;
                    }
                }
            }
        }
    }

    // Unit assignment based on the heuristic that the unit that should go first are the 
    // ones with the hardest difficulty in getting a good slot 
    private void EaseOfAssignmentUnitAssignment() 
    {
            var selectedFormations = GetSelectedFormations();
            var numSelectedFormations = selectedFormations.Length;

            var currUnitIndex = 0;
            //var assignmentHandles = new NativeArray<JobHandle>(numSelectedFormations, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            for (int i = 0; i < numSelectedFormations; i++)
            {
                var currFormation = selectedFormations[i];
                _selectedUnitQuery.SetSharedComponentFilter(currFormation);
                var numUnits = _selectedUnitQuery.CalculateEntityCount();
                
                // Indexing for each slot x for each y unit: (NUM_UNITS * y + x)
                var sortedCostsAndSlots = new NativeArray<CostAndSlot>(numUnits * numUnits, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

                // Sorted by ease of assignment (lower values are first to access most constraint units first)
                var sortedUnitAssignmentOrder = new NativeArray<EaseAndFormatiomIndex>(numUnits, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

                var slotPositions = TEMP_SLOT_ASSIGNMENTS.Slice(currUnitIndex, numUnits);
                currUnitIndex += numUnits;
                
                var translations = GetComponentDataFromEntity<Translation>(true);
                var entityArray = _selectedUnitQuery.ToEntityArray(Allocator.TempJob);
                var entityToIndex = new NativeHashMap<Entity, int>(numUnits, Allocator.TempJob);
                var handle = new BatchedCalculateAssignmentOrderJob {
                    EntityToIndex = entityToIndex.AsParallelWriter(),
                    unitEntities = entityArray,
                    translations = translations,
                    slotPositions = slotPositions,
                    sortedCostsAndSlots = sortedCostsAndSlots,
                    sortedUnitAssignmentOrder = sortedUnitAssignmentOrder,
                    NumUnits = numUnits

                }.Schedule(numUnits, 32, Dependency); // TODO adjust innerLoopBatchCount for something optimal?

                /*
                var handle = Entities
                    .WithName("BatchedCalculateAssignmentOrderJob")
                    .WithAll<UnitTag>()
                    .WithReadOnly(slotPositions)
                    .WithNativeDisableParallelForRestriction(sortedCostsAndSlots)
                    .WithReadOnly(translations)
                    .WithNativeDisableParallelForRestriction(translations)
                    .WithNativeDisableContainerSafetyRestriction(translations)
                    .WithSharedComponentFilter(currFormation)
                    .ForEach((Entity unitEnity, int entityInQueryIndex) =>
                    {
                        // Cost and slots for ONE unit
                        var costAndSlotsSlice = sortedCostsAndSlots.Slice(entityInQueryIndex * numUnits, numUnits);

                        // Converts each cost into "ease of assignment", which is essentially the inverse of cost
                        // "Ease of Assignment" = 1 / (1 + cost)  
                        float easeOfAssignmentSum = 0;
                        var currPosition = translations[unitEnity].Value;
                        for (int j = 0; j < numUnits; j++)
                        {
                            // Iterating through every slot
                            var cost = math.distancesq(currPosition, slotPositions[j]);
                            easeOfAssignmentSum += 1 / (1 + cost);
                            costAndSlotsSlice[j] = new CostAndSlot(cost, j);
                        }
                        
                        sortedUnitAssignmentOrder[entityInQueryIndex] = new EaseAndFormatiomIndex(easeOfAssignmentSum, unitEnity);

                        // Sort the slice of costs that represents all the costs and slots for 1 unit
                        costAndSlotsSlice.Sort(); // TODO explore 

                    }).ScheduleParallel(this.Dependency);
                    */

                // False -> slot not yet filled
                var filledSlots = new NativeArray<bool>(numUnits, Allocator.TempJob, NativeArrayOptions.ClearMemory);
                
                var sortingHandle = sortedUnitAssignmentOrder.Sort(handle);
                
                var unitAssignmentHandle = new UnitAssignmentJob
                {
                    EntityToIndex = entityToIndex,
                    targetPositions = GetComponentDataFromEntity<TargetPosition>(false),
                    sortedEaseOfassignmentFormationIndexPairs = sortedUnitAssignmentOrder,
                    sortedCostsAndSlots = sortedCostsAndSlots,
                    slotPositions = slotPositions,
                    filledSlots = filledSlots,
                }.Schedule(sortingHandle);
                entityToIndex.Dispose(unitAssignmentHandle);

                /*var targetPositions = GetComponentDataFromEntity<TargetPosition>(true);
                Entities
                    .WithName("UnitAssignmentJob")
                    .WithAll<UnitTag>()
                    .WithReadOnly(slotPositions)
                    .WithNativeDisableParallelForRestriction(sortedCostsAndSlots)
                    .WithNativeDisableParallelForRestriction(targetPositions)
                    .WithNativeDisableContainerSafetyRestriction(targetPositions)
                    .WithReadOnly(sortedUnitAssignmentOrder)
                    .WithDisposeOnCompletion(sortedUnitAssignmentOrder)
                    .WithReadOnly(slotPositions)
                    .WithDisposeOnCompletion(filledSlots)
                    .WithNativeDisableParallelForRestriction(filledSlots)
                    
                    .WithSharedComponentFilter(currFormation)
                    .ForEach((Entity unitEnity, int entityInQueryIndex) =>
                    {
                        Entity currFormationIndex = sortedUnitAssignmentOrder[entityInQueryIndex].formationIndex;
            
                        var currCostsAndSlots = sortedCostsAndSlots.Slice(entityInQueryIndex * numUnits, numUnits);

                        for (int j = 0; j < currCostsAndSlots.Length; j++) {
                            var costAndSlot = currCostsAndSlots[j];
                            if (!filledSlots[costAndSlot.slot]) {
                                targetPositions[currFormationIndex] = new TargetPosition { Value = slotPositions[costAndSlot.slot] };
                                filledSlots[costAndSlot.slot] = true;
                                break;
                            }
                        }

                    }).Schedule(sortingHandle);*/

            }
    }

    /// <summary>
    /// Simple unit assignment [The Simplest AI Trick in the Book, Mars]
    /// </summary>
    private void SpatialUnitAssignmnet()
    {
        // Context: TEMP_UNIT_ASSIGMENTS should already be sorted top to bottom, left to right
        // Context: _dragFor is the target orientation of the formation
        
        var selectedFormations = GetSelectedFormations();
        var numSelectedFormations = selectedFormations.Length;
            
        var currUnitIndex = 0;
        for (int i = 0; i < numSelectedFormations; i++)
        {
            var currFormation = selectedFormations[i];

            // Calculate centroid 
            var centroidArr = new NativeArray<float3>(1, Allocator.TempJob, NativeArrayOptions.ClearMemory);
            Entities
                .WithName("CalculateCentroid")
                .WithAll<UnitTag>()
                .WithSharedComponentFilter(currFormation)
                .ForEach((in Translation trans) =>
                {
                    centroidArr[0] += trans.Value;
                }).Schedule(this.Dependency).Complete();
            
            _selectedUnitQuery.SetSharedComponentFilter(currFormation);
            var numUnits = _selectedUnitQuery.CalculateEntityCount();
            var centroid = centroidArr[0] / numUnits;
            
            var slotPositions = TEMP_SLOT_ASSIGNMENTS.Slice(currUnitIndex, numUnits);
            currUnitIndex += numUnits;
            
            Debug.DrawLine(centroid, centroid + new float3(0f, 10f, 0f), Color.magenta, 3f);

            var dir = _dragForward;
            int layersCount;
            int layersSize;
            
            // 
            //Entities
            //    .WithAll<UnitTag>()
            //    .WithSharedComponentFilter(currFormation)
            //    .ForEach((in Translation trans) =>
            //    {
            //        
            //    }).ScheduleParallel();




        }
    }
}

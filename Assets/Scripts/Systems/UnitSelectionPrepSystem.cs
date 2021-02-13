using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;


[UpdateInGroup(typeof(InitializationSystemGroup))]
public class UnitSelectionPrepSystem : SystemBase
{

    private EntityQuery _unitQuery;
    private EntityQuery _dragIndicatorQuery;

    private const int MAX_FORMATIONS = 40;

    private NativeArray<bool> _isFormationsHovered;
    private NativeArray<bool> _isFormationsSelected;

    private Camera _cameraMain;
    
    private UnitSelectSystem _unitSelectSystem;
    private EndInitializationEntityCommandBufferSystem _ecbSystem;

    private float3 _dragStartPos;

    protected override void OnCreate() {

        _cameraMain = Camera.main;
        
        _unitSelectSystem = World.GetOrCreateSystem<UnitSelectSystem>();
        _ecbSystem = World.GetOrCreateSystem<EndInitializationEntityCommandBufferSystem>();

        _isFormationsHovered = new NativeArray<bool>(MAX_FORMATIONS, Allocator.Persistent);
        _isFormationsSelected = new NativeArray<bool>(MAX_FORMATIONS, Allocator.Persistent);

        _unitQuery = GetEntityQuery(
            ComponentType.ReadOnly(typeof(FormationIndex)),
            ComponentType.ReadOnly(typeof(Translation))
        );

        _dragIndicatorQuery = GetEntityQuery(
            ComponentType.ReadOnly(typeof(DragIndicatorTag)),
            ComponentType.ReadOnly(typeof(FormationGroup))
        );

    }

    protected override void OnDestroy() {
        base.OnDestroy();
        _isFormationsHovered.Dispose();
        _isFormationsSelected.Dispose();
    }

    protected override void OnUpdate()
    {
        var ecb = _ecbSystem.CreateCommandBuffer().AsParallelWriter();
        
        if (Input.GetMouseButtonDown(0)) {

            // Deselect everything
            EntityManager.SetSharedComponentData(_unitQuery, new SelectedFormationSharedComponent { IsSelected = false });
            EntityManager.SetSharedComponentData(_dragIndicatorQuery, new SelectedFormationSharedComponent { IsSelected = false });

            Entities
                .WithName("Deselect_All_Unit_Indicator_Job")
                .WithAll<SelectedIndicatorTag>()
                .ForEach((Entity unitEntity, int entityInQueryIndex) => {
            
                    ecb.AddComponent<DisableRendering>(entityInQueryIndex, unitEntity);
            
                }).ScheduleParallel();

            _dragStartPos = Input.mousePosition;
        }

        if (Input.GetMouseButton(0)) {
            float3 currDragPos = Input.mousePosition;
        }

        if (Input.GetMouseButtonUp(0)) {
            float3 dragEndPos = Input.mousePosition;

            float2 bottomLeftCornor = new float2(math.min(_dragStartPos.x, dragEndPos.x), math.min(_dragStartPos.y, dragEndPos.y));
            float2 topRightCornor = new float2(math.max(_dragStartPos.x, dragEndPos.x), math.max(_dragStartPos.y, dragEndPos.y));
            
            var cameraPos = _cameraMain.transform.position;
            var cameraTransform = _cameraMain.transform;
            
            #region Method 1

            // For using static methods below
            var camProjMatrix = _cameraMain.projectionMatrix;
            var camUp = cameraTransform.up;
            var camRight = cameraTransform.right;
            var camForward = cameraTransform.forward;
            var pixelWidth = Screen.width;
            var pixelHeight = Screen.height;
            var scaleFactor = GameController.instance.dragRectCanvas.scaleFactor;
            
            // TEMP stupid way of "cancelling" this job early when a unit in the same formation has been selected
            // NOTE: due to running on multiple threads, there is a race condition where the job will write to the array multiple times (but no side effects)
            var formationGroups = new List<FormationGroup>();
            EntityManager.GetAllUniqueSharedComponentData(formationGroups);
            
            var isSelected = new NativeArray<bool>(40, Allocator.TempJob); // TODO capacity should be the number of formations player has
            _unitSelectSystem.SetBuffer(isSelected);

            // Iterate through every formation and check if they're selected
            foreach (var formation in formationGroups) {
                Entities
                    .WithAll<UnitTag>()
                    .WithSharedComponentFilter(formation)
                    .ForEach((Entity unitEntity, int entityInQueryIndex, ref Translation translation) => {

                        if (isSelected[formation.ID]) {
                            return;
                        }

                        // Convert entity to view space before converting it to frustum space
                        float2 entityPos2D = ConvertWorldToScreenCoordinates(translation.Value, cameraPos, camProjMatrix, camUp, camRight, camForward, pixelWidth, pixelHeight, scaleFactor);

                        if (entityPos2D.x > bottomLeftCornor.x &&
                            entityPos2D.y > bottomLeftCornor.y &&
                            entityPos2D.x < topRightCornor.x &&
                            entityPos2D.y < topRightCornor.y) {

                            isSelected[formation.ID] = true;
                        }
                    })
                    .WithNativeDisableContainerSafetyRestriction(isSelected)
                    .ScheduleParallel();
            }

            #endregion

            #region Method 2

/*            // For all selected units, give them the SelectedFormationTag
            var ecb = m_EndSimulationEcbSystem.CreateCommandBuffer().AsParallelWriter();

            var pixelWidth = Screen.width;
            var pixelHeight = Screen.height;
            var fullViewProjMatrix = _cameraMain.projectionMatrix * _cameraMain.transform.localToWorldMatrix;

            // TEMP stupid way of "cancelling" this job early when a unit in the same formation has been selected
            // NOTE: due to running on multiple threads, there is a race condition where the job will write to the array multiple times (but no side effects)
            var formationGroups = new List<FormationGroup>();
            EntityManager.GetAllUniqueSharedComponentData(formationGroups);
            var isSelected = new NativeArray<bool>(40, Allocator.TempJob); // TODO max of 40 formations

            foreach (var formation in formationGroups) {
                Entities
                    .WithAll<UnitTag>()
                    .ForEach((Entity unitEntity, int entityInQueryIndex, ref Translation translation, in LocalToWorld localToWorld) => {

                        if (isSelected[formation.ID]) {
                            ecb.AddComponent<SelectedFormationTag>(entityInQueryIndex, unitEntity); // TODO replace with chunk component, shared component, or something else
                            ecb.DestroyEntity(entityInQueryIndex, unitEntity);
                            return;
                        }

                        // Converting world point to screen point
                        var position4 = new float4(translation.Value.x, translation.Value.y, translation.Value.z, 1);
                        var viewPos = math.mul(fullViewProjMatrix, position4); // Gets view position 
                        var viewportPoint = viewPos / -viewPos.w; // Takes away depth? Puts everything in a 2D place?
                        
                        var screenCord = new float2(viewportPoint.x, viewportPoint.y) / 2f;
                        screenCord.x = screenCord.x * pixelWidth;
                        screenCord.y = screenCord.y * pixelHeight;

                        if (screenCord.x > bottomLeftCornor.x &&
                            screenCord.y > bottomLeftCornor.y &&
                            screenCord.x < topRightCornor.x &&
                            screenCord.y < topRightCornor.y) {

                            isSelected[formation.ID] = true;
                            ecb.AddComponent<SelectedFormationTag>(entityInQueryIndex, unitEntity); // TODO replace with chunk component, shared component, or something else
                        }

                    })
                    .WithNativeDisableContainerSafetyRestriction(isSelected)
                    .ScheduleParallel();
            }

*/
            #endregion

        }
        
        _ecbSystem.AddJobHandleForProducer(this.Dependency);
    }

    /// <summary>
    /// Convert point from world space to screen space
    /// </summary>
    /// <param name="point">Point in World Space</param>
    /// <param name="cameraPos">Camera position in World Space</param>
    /// <param name="camProjMatrix">Camera.projectionMatrix</param>
    /// <param name="camUp">Camera.transform.up</param>
    /// <param name="camRight">Camera.transform.right</param>
    /// <param name="camForward">Camera.transform.forward</param>
    /// <param name="pixelWidth">Camera.pixelWidth</param>
    /// <param name="pixelHeight">Camera.pixelHeight</param>
    /// <param name="scaleFactor">Canvas.scaleFactor</param>
    /// <returns></returns>
    public static float2 ConvertWorldToScreenCoordinates(float3 point, float3 cameraPos, float4x4 camProjMatrix, float3 camUp, float3 camRight, float3 camForward, float pixelWidth, float pixelHeight, float scaleFactor) {
        /*
        * 1 convert P_world to P_camera
        */
        float4 pointInCameraCoodinates = ConvertWorldToCameraCoordinates(point, cameraPos, camUp, camRight, camForward);


        /*
        * 2 convert P_camera to P_clipped
        */
        float4 pointInClipCoordinates = math.mul(camProjMatrix, pointInCameraCoodinates);

        /*
        * 3 convert P_clipped to P_ndc
        * Normalized Device Coordinates
        */
        float4 pointInNdc = pointInClipCoordinates / pointInClipCoordinates.w;


        /*
        * 4 convert P_ndc to P_screen
        */
        float2 pointInScreenCoordinates;
        pointInScreenCoordinates.x = pixelWidth / 2.0f * (pointInNdc.x + 1);
        pointInScreenCoordinates.y = pixelHeight / 2.0f * (pointInNdc.y + 1);


        // return screencoordinates with canvas scale factor (if canvas coords required)
        return pointInScreenCoordinates / scaleFactor;
    }

    private static float4 ConvertWorldToCameraCoordinates(float3 point, float3 cameraPos, float3 camUp, float3 camRight, float3 camForward) {
        // translate the point by the negative camera-offset
        //and convert to Vector4
        float4 translatedPoint = new float4(point - cameraPos, 1f);

        // create transformation matrix
        float4x4 transformationMatrix = float4x4.identity;
        transformationMatrix.c0 = new float4(camRight.x, camUp.x, -camForward.x, 0);
        transformationMatrix.c1 = new float4(camRight.y, camUp.y, -camForward.y, 0);
        transformationMatrix.c2 = new float4(camRight.z, camUp.z, -camForward.z, 0);

        float4 transformedPoint = math.mul(transformationMatrix, translatedPoint);

        return transformedPoint;
    }

}

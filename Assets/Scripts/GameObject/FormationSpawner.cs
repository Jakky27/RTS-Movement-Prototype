using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;

public enum FORMATIONS {
    Testudo = 0,
    Orb = 1,
    Wedge = 2
}


public class FormationSpawner : MonoBehaviour
{
    public FORMATIONS formationType = FORMATIONS.Testudo;

    public UnitData UnitData;
    
    public GameObject unitPrefab;
    public GameObject dragIndicatorPrefab;
    public GameObject selectedIndicatorPrefab;
    
    public bool isFriendly = true;
    public int numUnits = 100;
    
    public int TEMP_FORMATION_ID = -1;
    
    private BlobAssetStore blobAsset;
    private EntityQuery _selectedIndicatorsQuery;


    // Start is called before the first frame update
    void Start()
    {
        var formationPositions = new NativeArray<float3>(numUnits, Allocator.Temp);

        Type _formationType;
        switch (formationType) {
            case FORMATIONS.Testudo:
            _formationType = typeof(TestudoFormationTag);
            GetTestudoPositions(formationPositions);
            break;

            case FORMATIONS.Orb:
            _formationType = typeof(OrbFormationTag);
            break;

            case FORMATIONS.Wedge:
            _formationType = typeof(WedgeFormationTag);
            break;

            default:
            return; 
        }

        World world = World.DefaultGameObjectInjectionWorld;
        EntityManager entityManager = world.EntityManager;
        blobAsset = new BlobAssetStore();

        _selectedIndicatorsQuery = entityManager.CreateEntityQuery(
            ComponentType.Exclude(typeof(FormationGroup)),
            ComponentType.ReadOnly(typeof(SelectedIndicatorTag))
        );
        
        var conversionSetting = new GameObjectConversionSettings(world, GameObjectConversionUtility.ConversionFlags.AssignName, blobAsset);

        Entity unitEntity = GameObjectConversionUtility.ConvertGameObjectHierarchy(unitPrefab, conversionSetting);
        Entity dragIndicatorEntity = GameObjectConversionUtility.ConvertGameObjectHierarchy(dragIndicatorPrefab, conversionSetting);

        NativeArray<Entity> unitsToSpawn = new NativeArray<Entity>(numUnits, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
        NativeArray<Entity> dragIndicatorsToSpawn = new NativeArray<Entity>(numUnits, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

        entityManager.Instantiate(unitEntity, unitsToSpawn);
        entityManager.Instantiate(dragIndicatorEntity, dragIndicatorsToSpawn);

        entityManager.DestroyEntity(unitEntity);
        entityManager.DestroyEntity(dragIndicatorEntity);
        
        var formationGroup = new FormationGroup { ID = TEMP_FORMATION_ID }; // TEMP
        var selectedFormationGroup = new SelectedFormationSharedComponent { IsSelected = false };
        
        // Initialize blob asset (aabb) because all the units share them
        BlobAssetReference<AABBBlobAsset> blobAssetReference;
        using (BlobBuilder blobBuilder = new BlobBuilder(Allocator.Temp))
        {
            ref AABBBlobAsset aabbBlobAsset = ref blobBuilder.ConstructRoot<AABBBlobAsset>();
            blobBuilder.Allocate(ref aabbBlobAsset.Ptr) = UnitData.aabb;

            blobAssetReference = blobBuilder.CreateBlobAssetReference<AABBBlobAsset>(Allocator.Persistent);
        }
        
        

        for (int i = 0; i < numUnits; i++) {
            Entity unit = unitsToSpawn[i];
            Entity dragIndicator = dragIndicatorsToSpawn[i];
            
            entityManager.SetComponentData(unit, new Translation { Value = formationPositions[i] });
            entityManager.AddSharedComponentData(unit, formationGroup);
            entityManager.AddSharedComponentData(unit, new SelectedFormationSharedComponent { IsSelected = false });
            
            // Very interesting to think about why formationType isn't a shared component type
            // (maybe it's because unit groups can change formations on the fly, which may lead to it being moved to different chunks for no reason?)
            // (remember that changing a shared component type...
            entityManager.AddComponent(unit, _formationType); 

            entityManager.AddComponentData(unit, new TargetPosition { Value = formationPositions[i] });
            entityManager.AddComponentData(unit, new FormationIndex { Index = i });
            entityManager.AddComponentData(unit, new Health { Value = UnitData.Health });
            entityManager.AddComponentData(unit, new Velocity { Value = float3.zero });
            
            // TEMP?
            entityManager.AddComponentData(unit, new RandomGenerator { Value = UnityEngine.Random.Range(0f, 10f) });

            
            // Linking the unit to the drag indicator AND selected indicator
            // So when the unit is culled, so will the other two entities
            //var linkedBuffer = entityManager.AddBuffer<LinkedEntityGroup>(unit);
            //linkedBuffer.Add(dragIndicator);

            entityManager.AddComponentData(unit, new UnitDataBlobReference(blobAssetReference));

            // Drag indicator
            entityManager.AddSharedComponentData(dragIndicator, formationGroup);
            entityManager.AddSharedComponentData(dragIndicator, selectedFormationGroup);
            entityManager.AddComponent(dragIndicator, typeof(DisableRendering));
            
        }
        entityManager.AddSharedComponentData(_selectedIndicatorsQuery, formationGroup);
        entityManager.AddComponent(_selectedIndicatorsQuery, typeof(DisableRendering));

        unitsToSpawn.Dispose();
        dragIndicatorsToSpawn.Dispose();
        formationPositions.Dispose();
    }
    
    
    
    /// <summary>
    /// Creates a list of positions relative to the GameObject transform
    /// </summary>
    /// <param name="positions"></param>
    private void GetTestudoPositions(NativeArray<float3> positions) {

        float width = 10f;
        float distanceApart = 2f;
        float midX = (width - 1) / 2f;
        
        var position = transform.position;

        for (int i = 0; i < numUnits; i++) {

            float x = i % width - midX;
            float z = i / width + 1;

            positions[i] = new float3(position.x + x * distanceApart, 0f, position.z - z * distanceApart);

        }
    }

    private void OnDestroy() {
        blobAsset.Dispose();
    }
}

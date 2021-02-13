using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class DebugEcsSpawner : MonoBehaviour
{
    [SerializeField] GameObject prefab;
    [SerializeField] int spawnCount = 100;
    [SerializeField] bool disableAtStart = false;
    [SerializeField] string name = "";

    private BlobAssetStore blobAsset;

    // Start is called before the first frame update
    void Start()
    {
        World world = World.DefaultGameObjectInjectionWorld;

        blobAsset = new BlobAssetStore();

        var conversionSetting = new GameObjectConversionSettings(world, GameObjectConversionUtility.ConversionFlags.AssignName, blobAsset);

        Entity unitEntity = GameObjectConversionUtility.ConvertGameObjectHierarchy(prefab, conversionSetting);
        EntityManager entityManager = world.EntityManager;

        NativeArray<Entity> unitsToSpawn = new NativeArray<Entity>(spawnCount, Allocator.Temp);

        entityManager.Instantiate(unitEntity, unitsToSpawn);

        for (int i = 0; i < spawnCount; i++)
        {

            Entity entity = unitsToSpawn[i];
            //entityManager.SetName(entity, name);

            if (disableAtStart)
            {
                entityManager.AddComponent(entity, typeof(Disabled));
            }
        }

        unitsToSpawn.Dispose();
    }

    private void OnDestroy() {
        blobAsset.Dispose();
    }
}

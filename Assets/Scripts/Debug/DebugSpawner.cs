using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

public class DebugSpawner : MonoBehaviour
{

    [SerializeField] GameObject prefabECS;
    [SerializeField] GameObject prefabGameObject;
    public int x, y, z;
    public float distanceApart = 2f;

    public bool isECS = true;

    public bool isRepeating = false;

    private BlobAssetStore _blobAssetStore;

    private void OnEnable()
    {
        if (isRepeating)
        {
            InvokeRepeating(nameof(Start), 1f, 1f);
        }

        _blobAssetStore = new BlobAssetStore();
    }

    private void OnDisable()
    {
        CancelInvoke(nameof(Start));
        _blobAssetStore.Dispose();
    }

    // Start is called before the first frame update
    void Start()
    {
        // This is how it's done for GameObjects, not entities
        if (!isECS) {
            for (int i = 0; i < x; i++) {
                for (int j = 0; j < y; j++) {
                    for (int k = 0; k < z; k++) {

                        Instantiate(prefabGameObject, new Vector3(i * distanceApart, j * distanceApart + 1f, k * distanceApart), Quaternion.identity);
                        
                    }
                }
            }
        } else {
            // Spawning for entities
            World world = World.DefaultGameObjectInjectionWorld;
            Entity entity = GameObjectConversionUtility.ConvertGameObjectHierarchy(prefabECS, new GameObjectConversionSettings(world, 0, _blobAssetStore));
            EntityManager entityManager = world.EntityManager;


            NativeArray<Entity> entitiesToSpawn = new NativeArray<Entity>(x * y * z, Allocator.Temp);
            entityManager.Instantiate(prefabECS, entitiesToSpawn);

            for (int i = 0; i < x; i++) {
                for (int j = 0; j < y; j++) {
                    for (int k = 0; k < z; k++) {

                        float3 position = (float3)transform.position + new float3(i * distanceApart, j * distanceApart + 1f, k * distanceApart);
                        Entity newEntity = entityManager.Instantiate(entity);
                        entityManager.SetComponentData(newEntity, new Translation { Value = position });
                    }
                }
            }
        }
    }
}

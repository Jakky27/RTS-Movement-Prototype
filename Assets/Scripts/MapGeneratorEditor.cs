using System.Collections;
using System.Collections.Generic;
using System.Security.AccessControl;
using Unity.Entities;
using UnityEditor;
using UnityEngine;

[ExecuteAlways]
public class MapGeneratorEditor : MonoBehaviour
{
    private MapGeneratorSystem _mapGeneratorSystem;

    public MapGeneratorSettings mapSettings;

    public GameObject mapPrefab; // I have to use a GameObejct b/c I don't know how to spawn an entity with pure ECS 

    public Mesh debugMesh;
    public Material debugMaterial;

    private BlobAssetStore blobAsset;

    private void Start() {
    }

    public void OnEnable() {
        if (_mapGeneratorSystem != null) return;

        DefaultWorldInitialization.DefaultLazyEditModeInitialize();//boots the ECS EditorWorld

        _mapGeneratorSystem = World.DefaultGameObjectInjectionWorld.GetOrCreateSystem<MapGeneratorSystem>();


        //EditorApplication.update += () => _mapGeneratorSystem.Update();//makes the system tick properly, not every 2 seconds !
    }

    public void OnDestroy() {
        //extra safety against post-compilation problems (typeLoadException) and spamming the console with failed updates
        //if (!EditorApplication.isPlaying) {
        //    EditorApplication.update -= () => _mapGeneratorSystem.Update();
        //}
    }

    public void GenerateMap() {
        _mapGeneratorSystem = World.DefaultGameObjectInjectionWorld.GetOrCreateSystem<MapGeneratorSystem>();

        World world = World.DefaultGameObjectInjectionWorld;
        blobAsset = new BlobAssetStore();
        var conversionSetting = new GameObjectConversionSettings(world, GameObjectConversionUtility.ConversionFlags.AssignName, blobAsset);
        Entity mapEntity = GameObjectConversionUtility.ConvertGameObjectHierarchy(mapPrefab, conversionSetting);

       _mapGeneratorSystem.mapPrefab = Instantiate(mapPrefab);
        
        _mapGeneratorSystem.mapEntity = mapEntity;
        _mapGeneratorSystem.mapGenSettings = mapSettings;
        _mapGeneratorSystem.debugMaterial = debugMaterial;


        _mapGeneratorSystem.GenerateMap();

        blobAsset.Dispose();

    }

    public void GenerateMap2() {
        _mapGeneratorSystem = World.DefaultGameObjectInjectionWorld.GetOrCreateSystem<MapGeneratorSystem>();

        World world = World.DefaultGameObjectInjectionWorld;
        blobAsset = new BlobAssetStore();
        var conversionSetting = new GameObjectConversionSettings(world, GameObjectConversionUtility.ConversionFlags.AssignName, blobAsset);
        Entity mapEntity = GameObjectConversionUtility.ConvertGameObjectHierarchy(mapPrefab, conversionSetting);

        _mapGeneratorSystem.mapPrefab = mapPrefab;

        _mapGeneratorSystem.mapEntity = mapEntity;
        _mapGeneratorSystem.mapGenSettings = mapSettings;
        _mapGeneratorSystem.debugMaterial = debugMaterial;


        _mapGeneratorSystem.GenerateMapMatchingSquares();

        blobAsset.Dispose();

    }
}


#if UNITY_EDITOR

[CustomEditor(typeof(MapGeneratorEditor))]
public class GenerateMapEditor : Editor {
    public override void OnInspectorGUI() {
        DrawDefaultInspector();

        MapGeneratorEditor myScript = (MapGeneratorEditor)target;
        if (GUILayout.Button("Generate Map")) {
            myScript.GenerateMap();
        }

        if (GUILayout.Button("Generate Map (Matching Squares)")) {
            myScript.GenerateMap2();
        }
    }
}

#endif
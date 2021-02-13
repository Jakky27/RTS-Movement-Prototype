using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "Map Generater Settings", menuName = "Settings/MapGeneratorSettings", order = 1)]
public class MapGeneratorSettings : ScriptableObject {
    public Texture2D heightMap;
    public int MapChunkSize;
    public float cellSize;
    public float heightMultiplier;
    public AnimationCurve meshHeightCurve;
}
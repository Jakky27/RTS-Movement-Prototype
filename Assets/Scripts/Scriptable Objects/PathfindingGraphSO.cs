using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

[CreateAssetMenu(fileName = "Pathfinding Graph", menuName = "Settings/PathfindingGraph", order = 1)]
public class PathfindingGraphSO : ScriptableObject
{
    public int NumLevels;
    public float CellSize;
    public int2 MapSize;

    [Header("Debug")] 
    public GameObject OpenCellPrefab;
    public GameObject BlockedCellPrefab;

}
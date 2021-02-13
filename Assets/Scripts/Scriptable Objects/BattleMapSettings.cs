using System.Collections;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

[CreateAssetMenu(fileName = "Battle Map Settings", menuName = "Settings/BattleMapSettings", order = 1)]
public class BattleMapSettings : ScriptableObject
{
    public int NumLevels;
    public float CellSize;
    public int2 MapSize;

    [Header("Debug")]
    public GameObject OpenCellPrefab;
    public GameObject BlockedCellPrefab;

}
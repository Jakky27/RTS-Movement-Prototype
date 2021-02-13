using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "Pathfinding Graph Debug Settings", menuName = "Settings/PathfindingGraphDebugSettings", order = 1)]
public class PathfindingGraphDebugSettingsSO : ScriptableObject
{
    
    
    public bool ShowCellGrid;
    public bool ShowBlockedCells;
    
    public bool ShowRegionGrid;
    public bool ShowRegionEdges;

    public bool ShowAdjacentRegions;
}

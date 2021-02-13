using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public class BattleDebugPanel : MonoBehaviour
{
    public PathfindingGraphDebugSettingsSO PathfindingGraphDebugSettings;

    private UIDocument _uiDocument;
    
    // Buttons
    private Toggle _showCellGridToggle;
    private Toggle _showBlockedCellsToggle;
    private Toggle _showRegionGridToggle;
    private Toggle _showRegionEdgesToggle;
    private Toggle _showAdjacentRegionsToggle;

    private void Awake()
    {
        _uiDocument = GetComponent<UIDocument>();
        var rootVisualElement = _uiDocument.rootVisualElement;
        
        _showCellGridToggle = rootVisualElement.Q<Toggle>("ShowCellGridToggle");
        _showBlockedCellsToggle = rootVisualElement.Q<Toggle>("ShowBlockedCellsToggle");
        _showRegionGridToggle = rootVisualElement.Q<Toggle>("ShowRegionGridToggle");
        _showRegionEdgesToggle = rootVisualElement.Q<Toggle>("ShowRegionEdgesToggle");
        _showAdjacentRegionsToggle = rootVisualElement.Q<Toggle>("ShowAdjacentRegionsToggle");
    }

    // Start is called before the first frame update
    void Start()
    {
        _showCellGridToggle.RegisterCallback<ClickEvent>(evt =>
        {
            PathfindingGraphDebugSettings.ShowCellGrid = _showCellGridToggle.value;
        });
        
        _showBlockedCellsToggle.RegisterCallback<ClickEvent>(evt =>
        {
            PathfindingGraphDebugSettings.ShowBlockedCells = _showBlockedCellsToggle.value;
        });
        
        _showRegionGridToggle.RegisterCallback<ClickEvent>(evt =>
        {
            PathfindingGraphDebugSettings.ShowRegionGrid = _showRegionGridToggle.value;
        });
        
        _showRegionEdgesToggle.RegisterCallback<ClickEvent>(evt =>
        {
            PathfindingGraphDebugSettings.ShowRegionEdges = _showRegionEdgesToggle.value;
        });
        
        _showAdjacentRegionsToggle.RegisterCallback<ClickEvent>(evt =>
        {
            PathfindingGraphDebugSettings.ShowAdjacentRegions = _showAdjacentRegionsToggle.value;
        });
    }

    private void OnEnable()
    {
        _showCellGridToggle.value = PathfindingGraphDebugSettings.ShowCellGrid;
        _showBlockedCellsToggle.value = PathfindingGraphDebugSettings.ShowBlockedCells;
        _showRegionGridToggle.value = PathfindingGraphDebugSettings.ShowRegionGrid;
        _showRegionEdgesToggle.value = PathfindingGraphDebugSettings.ShowRegionEdges;
        _showAdjacentRegionsToggle.value = PathfindingGraphDebugSettings.ShowAdjacentRegions;
    }
}

using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class GameController : MonoBehaviour
{
    public static GameController instance;

    public Image mouseDragRect;

    public Canvas dragRectCanvas;

    float3 dragStartPos;
    float3 currDragPos;

    private void Awake() {
        instance = this;
    }

    private void Update() {

        if (Input.GetMouseButtonDown(0)) {
            mouseDragRect.enabled = true;
            dragStartPos = Input.mousePosition;
        }

        if (Input.GetMouseButton(0)) {
            currDragPos = Input.mousePosition;

            float width = currDragPos.x - dragStartPos.x;
            float height = currDragPos.y - dragStartPos.y;
            mouseDragRect.rectTransform.sizeDelta = new float2(Mathf.Abs(width), Mathf.Abs(height));
            mouseDragRect.rectTransform.anchoredPosition = dragStartPos.xy + new float2(width / 2, height / 2);

        }

        if (Input.GetMouseButtonUp(0)) {
            mouseDragRect.enabled = false;
        }
    }
}

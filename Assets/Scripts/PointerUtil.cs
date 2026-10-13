using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public static class PointerUtil
{
    private static readonly List<RaycastResult> results = new List<RaycastResult>();

    // EventSystem.IsPointerOverGameObject() also returns true over 3D devices because the
    // camera has a PhysicsRaycaster, so only count hits from UI graphic raycasters.
    public static bool IsOverUI(Vector2 screenPosition)
    {
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem == null) return false;

        PointerEventData data = new PointerEventData(eventSystem) { position = screenPosition };
        results.Clear();
        eventSystem.RaycastAll(data, results);

        foreach (RaycastResult result in results)
        {
            if (result.module is GraphicRaycaster) return true;
        }
        return false;
    }

    // True while walk mode has locked the cursor and the crosshair is doing the aiming
    public static bool CursorCaptured => LabViewMode.Instance != null && LabViewMode.Instance.CursorCaptured;

    // Where the player is pointing: the crosshair in captured walk mode, otherwise the mouse
    public static Vector2 ScreenPoint
    {
        get
        {
            if (CursorCaptured) return new Vector2(Screen.width / 2f, Screen.height / 2f);
            return Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
        }
    }

    public static bool IsTyping()
    {
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem == null || eventSystem.currentSelectedGameObject == null) return false;

        GameObject selected = eventSystem.currentSelectedGameObject;
        TMP_InputField tmpField = selected.GetComponent<TMP_InputField>();
        if (tmpField != null && tmpField.isFocused) return true;

        InputField legacyField = selected.GetComponent<InputField>();
        return legacyField != null && legacyField.isFocused;
    }
}

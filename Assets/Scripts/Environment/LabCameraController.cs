using UnityEngine;
using UnityEngine.InputSystem;

// Orbit camera for inspecting the lab.
// Right-drag: orbit · Middle-drag / WASD: pan · Scroll: zoom · F: frame all devices · Home: reset view
// The camera keeps its scene pose until the user first interacts with it.
[RequireComponent(typeof(Camera))]
public class LabCameraController : MonoBehaviour
{
    public float orbitSensitivity = 0.25f;
    public float panSensitivity = 0.0025f;
    public float keyboardPanSpeed = 8f;
    public float zoomStep = 0.12f;
    public float minDistance = 2.5f;
    public float maxDistance = 32f;
    public float minPitch = 5f;
    public float maxPitch = 85f;
    public float smoothing = 12f;
    public float roomLimit = 13f;

    private Vector3 pivot, targetPivot;
    private float yaw, pitch, distance;
    private float targetYaw, targetPitch, targetDistance;

    private Vector3 startPosition;
    private Quaternion startRotation;
    private bool userControlled = false;
    private bool orbiting = false;
    private bool panning = false;

    void Start()
    {
        CaptureCurrentPose();

        // A near-horizontal start view turns the floor grid into a flat smear, so start from an overview
        if (pitch < 15f)
        {
            pivot = targetPivot = new Vector3(0f, 0f, 1f);
            pitch = targetPitch = 38f;
            yaw = targetYaw = 0f;
            distance = targetDistance = 15f;
            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            transform.SetPositionAndRotation(pivot - rotation * Vector3.forward * distance, rotation);
        }

        startPosition = transform.position;
        startRotation = transform.rotation;
    }

    void CaptureCurrentPose()
    {
        Vector3 forward = transform.forward;
        Plane floor = new Plane(Vector3.up, Vector3.zero);
        Ray ray = new Ray(transform.position, forward);

        if (floor.Raycast(ray, out float hit) && hit < 40f) pivot = ray.GetPoint(hit);
        else
        {
            pivot = transform.position + forward * 10f;
            pivot.y = 0f;
        }

        Vector3 offset = transform.position - pivot;
        distance = Mathf.Clamp(offset.magnitude, minDistance, maxDistance);
        pitch = Mathf.Clamp(Mathf.Asin(Mathf.Clamp(offset.y / Mathf.Max(offset.magnitude, 0.001f), -1f, 1f)) * Mathf.Rad2Deg, minPitch, maxPitch);
        yaw = Mathf.Atan2(-offset.x, -offset.z) * Mathf.Rad2Deg;

        targetPivot = pivot;
        targetYaw = yaw;
        targetPitch = pitch;
        targetDistance = distance;
    }

    void Update()
    {
        Mouse mouse = Mouse.current;
        Keyboard keyboard = Keyboard.current;
        if (mouse == null) return;

        Vector2 pointer = mouse.position.ReadValue();
        bool overUI = PointerUtil.IsOverUI(pointer);

        if (mouse.rightButton.wasPressedThisFrame && !overUI) orbiting = true;
        if (mouse.middleButton.wasPressedThisFrame && !overUI) panning = true;
        if (!mouse.rightButton.isPressed) orbiting = false;
        if (!mouse.middleButton.isPressed) panning = false;

        Vector2 delta = mouse.delta.ReadValue();

        if (orbiting && delta.sqrMagnitude > 0f)
        {
            TakeControl();
            targetYaw += delta.x * orbitSensitivity;
            targetPitch = Mathf.Clamp(targetPitch - delta.y * orbitSensitivity, minPitch, maxPitch);
        }

        if (panning && delta.sqrMagnitude > 0f)
        {
            TakeControl();
            Pan(-delta * panSensitivity * targetDistance);
        }

        float scroll = mouse.scroll.ReadValue().y;
        if (Mathf.Abs(scroll) > 0.01f && !overUI)
        {
            TakeControl();
            // Scroll units differ between platforms/Input System versions, so zoom in fixed steps
            targetDistance = Mathf.Clamp(targetDistance * (scroll > 0f ? 1f - zoomStep : 1f + zoomStep), minDistance, maxDistance);
        }

        if (keyboard != null && !PointerUtil.IsTyping()) HandleKeyboard(keyboard);

        if (!userControlled) return;

        float t = 1f - Mathf.Exp(-smoothing * Time.deltaTime);
        yaw = Mathf.LerpAngle(yaw, targetYaw, t);
        pitch = Mathf.Lerp(pitch, targetPitch, t);
        distance = Mathf.Lerp(distance, targetDistance, t);
        pivot = Vector3.Lerp(pivot, targetPivot, t);

        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        transform.SetPositionAndRotation(pivot - rotation * Vector3.forward * distance, rotation);
    }

    void HandleKeyboard(Keyboard keyboard)
    {
        Vector2 move = Vector2.zero;
        if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) move.y += 1f;
        if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) move.y -= 1f;
        if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) move.x += 1f;
        if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) move.x -= 1f;

        if (move != Vector2.zero)
        {
            TakeControl();
            Pan(move.normalized * keyboardPanSpeed * Time.deltaTime * Mathf.Max(1f, targetDistance / 10f));
        }

        if (keyboard.fKey.wasPressedThisFrame) FrameDevices();

        if (keyboard.homeKey.wasPressedThisFrame)
        {
            userControlled = false;
            transform.SetPositionAndRotation(startPosition, startRotation);
            CaptureCurrentPose();
        }
    }

    void Pan(Vector2 amount)
    {
        Quaternion flatRotation = Quaternion.Euler(0f, targetYaw, 0f);
        Vector3 move = flatRotation * new Vector3(amount.x, 0f, amount.y);
        targetPivot += move;
        targetPivot.x = Mathf.Clamp(targetPivot.x, -roomLimit, roomLimit);
        targetPivot.z = Mathf.Clamp(targetPivot.z, -roomLimit, roomLimit);
    }

    void TakeControl()
    {
        userControlled = true;
    }

    public Vector3 Pivot => targetPivot;
    public float Yaw => targetYaw;

    // Picks up from wherever another controller left the camera and glides back to the orbit pose
    public void ResumeFromCurrentTransform()
    {
        Vector3 offset = transform.position - targetPivot;
        float length = Mathf.Max(offset.magnitude, 0.01f);

        pivot = targetPivot;
        distance = length;
        pitch = Mathf.Asin(Mathf.Clamp(offset.y / length, -1f, 1f)) * Mathf.Rad2Deg;
        yaw = Mathf.Atan2(-offset.x, -offset.z) * Mathf.Rad2Deg;
        orbiting = panning = false;
        userControlled = true;
    }

    public void FrameDevices()
    {
        bool any = false;
        Bounds bounds = new Bounds();

        foreach (NetworkDevice device in NetworkDevice.All)
        {
            if (device == null || device.isPreviewModel) continue;

            if (!any) bounds = new Bounds(device.transform.position, Vector3.one);
            else bounds.Encapsulate(device.transform.position);
            any = true;
        }

        if (!any) return;

        TakeControl();
        targetPivot = new Vector3(bounds.center.x, 0f, bounds.center.z);
        float radius = Mathf.Max(bounds.extents.x, bounds.extents.z, 2f);
        targetDistance = Mathf.Clamp(radius * 1.9f, minDistance, maxDistance);
        targetPitch = Mathf.Clamp(Mathf.Max(targetPitch, 40f), minPitch, maxPitch);
    }
}

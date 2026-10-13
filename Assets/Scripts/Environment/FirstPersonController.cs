using UnityEngine;
using UnityEngine.InputSystem;

// Walk-around camera at eye height.
// WASD: walk · Shift: run · Mouse: look (cursor captured by LabViewMode; hold Alt to free it).
[RequireComponent(typeof(Camera))]
public class FirstPersonController : MonoBehaviour
{
    public float eyeHeight = 1.65f;
    public float walkSpeed = 2.2f;
    public float runSpeed = 4.5f;
    public float acceleration = 10f;
    public float lookSensitivity = 0.18f;
    public float bodyRadius = 0.3f;
    public float fieldOfView = 68f;
    public float headBobAmount = 0.03f;

    public float yaw;
    public float pitch;

    // 0 when standing still, 1 at walking speed and above; drives head-bob and hand sway
    public float MoveAmount { get; private set; }
    public float BobPhase { get; private set; }

    private Vector3 feet;
    private Vector3 velocity;
    private bool looking;
    private Camera cam;
    private float previousFov, previousNear;
    private LabEnvironment environment;
    private DevicePlacementManager placement;

    public void Enter(Vector3 feetPosition, float startYaw)
    {
        feet = new Vector3(feetPosition.x, FloorHeight, feetPosition.z);
        yaw = startYaw;
        pitch = 8f;
        velocity = Vector3.zero;
        ResolveCollisions(ref feet);
        ApplyTransform(0f);
    }

    float FloorHeight => placement != null ? placement.floorHeight : 0f;

    void OnEnable()
    {
        cam = GetComponent<Camera>();
        previousFov = cam.fieldOfView;
        previousNear = cam.nearClipPlane;
        cam.fieldOfView = fieldOfView;
        cam.nearClipPlane = 0.05f;

        environment = FindAnyObjectByType<LabEnvironment>();
        placement = FindAnyObjectByType<DevicePlacementManager>();
    }

    void OnDisable()
    {
        if (cam == null) return;
        cam.fieldOfView = previousFov;
        cam.nearClipPlane = previousNear;
        looking = false;
    }

    void Update()
    {
        Mouse mouse = Mouse.current;
        Keyboard keyboard = Keyboard.current;

        if (mouse != null)
        {
            // Free cursor (Alt held / panel open): right-drag still looks around
            if (mouse.rightButton.wasPressedThisFrame && !PointerUtil.IsOverUI(mouse.position.ReadValue())) looking = true;
            if (!mouse.rightButton.isPressed) looking = false;

            if (looking || PointerUtil.CursorCaptured)
            {
                Vector2 delta = mouse.delta.ReadValue();
                yaw += delta.x * lookSensitivity;
                pitch = Mathf.Clamp(pitch - delta.y * lookSensitivity, -80f, 80f);
            }
        }

        Vector3 wish = Vector3.zero;
        bool running = false;

        if (keyboard != null && !PointerUtil.IsTyping())
        {
            if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) wish.z += 1f;
            if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) wish.z -= 1f;
            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) wish.x += 1f;
            if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) wish.x -= 1f;
            running = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
        }

        Vector3 target = Quaternion.Euler(0f, yaw, 0f) * Vector3.ClampMagnitude(wish, 1f) * (running ? runSpeed : walkSpeed);
        velocity = Vector3.MoveTowards(velocity, target, acceleration * Time.deltaTime);

        Vector3 next = feet + velocity * Time.deltaTime;
        ResolveCollisions(ref next);
        velocity = (next - feet) / Mathf.Max(Time.deltaTime, 0.0001f);
        feet = next;

        float speed = new Vector3(velocity.x, 0f, velocity.z).magnitude;
        MoveAmount = Mathf.Clamp01(speed / walkSpeed);
        BobPhase += Time.deltaTime * Mathf.Lerp(0f, 9f, MoveAmount) * (running ? 1.35f : 1f);

        ApplyTransform(MoveAmount);
    }

    void ApplyTransform(float moveAmount)
    {
        Vector3 bob = new Vector3(Mathf.Cos(BobPhase) * headBobAmount * 0.5f, Mathf.Abs(Mathf.Sin(BobPhase)) * headBobAmount, 0f) * moveAmount;
        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        transform.SetPositionAndRotation(feet + Vector3.up * eyeHeight + Quaternion.Euler(0f, yaw, 0f) * bob, rotation);
    }

    // Keep the character inside the room and out of racks, furniture and devices
    void ResolveCollisions(ref Vector3 position)
    {
        if (environment != null)
        {
            foreach (Bounds obstacle in environment.Obstacles) PushOutOfBox(ref position, obstacle);

            Rect area = environment.WalkableArea;
            position.x = Mathf.Clamp(position.x, area.xMin + bodyRadius, area.xMax - bodyRadius);
            position.z = Mathf.Clamp(position.z, area.yMin + bodyRadius, area.yMax - bodyRadius);
        }

        foreach (NetworkDevice device in NetworkDevice.All)
        {
            if (device == null || device.isPreviewModel) continue;

            Vector3 center = device.transform.position;
            Vector3 offset = new Vector3(position.x - center.x, 0f, position.z - center.z);
            float minimum = device.FootprintRadius + bodyRadius;

            if (offset.sqrMagnitude < minimum * minimum)
            {
                Vector3 direction = offset.sqrMagnitude > 0.0001f ? offset.normalized : Vector3.back;
                position = new Vector3(center.x, position.y, center.z) + direction * minimum;
            }
        }
    }

    void PushOutOfBox(ref Vector3 position, Bounds box)
    {
        float minX = box.min.x - bodyRadius, maxX = box.max.x + bodyRadius;
        float minZ = box.min.z - bodyRadius, maxZ = box.max.z + bodyRadius;
        if (position.x <= minX || position.x >= maxX || position.z <= minZ || position.z >= maxZ) return;

        // Exit through the nearest side
        float left = position.x - minX, right = maxX - position.x;
        float near = position.z - minZ, far = maxZ - position.z;
        float smallest = Mathf.Min(Mathf.Min(left, right), Mathf.Min(near, far));

        if (smallest == left) position.x = minX;
        else if (smallest == right) position.x = maxX;
        else if (smallest == near) position.z = minZ;
        else position.z = maxZ;
    }
}

using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

public class DevicePlacementManager : MonoBehaviour
{
    public GameObject pcPrefab;
    public GameObject switchPrefab;
    public GameObject routerPrefab;
    public GameObject serverPrefab;

    public Camera mainCamera;

    [Header("Device Size")]
    [Tooltip("Size used for every device when Real World Scale is off")]
    public float targetDeviceSize = 1.2f;

    [Tooltip("Size each device type to its real-world dimensions and give it a desk or cabinet")]
    public bool realWorldScale = true;
    public float pcSize = 0.85f;
    public float switchSize = 0.55f;
    public float routerSize = 0.45f;
    public float serverSize = 2.0f;

    [Header("Spacing")]
    public float minimumSpacing = 0.5f;

    [Header("Ghost Preview")]
    public Material validGhostMaterial;
    public Material invalidGhostMaterial;

    [Header("Floor")]
    [Tooltip("Height of the floor devices rest on")]
    public float floorHeight = 0f;

    private GameObject selectedPrefab;
    private GameObject ghostObject;
    private Vector2 rightPressPosition;

    // Right-drag orbits the camera, so only a right *click* cancels placement
    private const float CancelClickTolerance = 6f;

    public bool IsPlacing => selectedPrefab != null;
    public GameObject SelectedPrefab => selectedPrefab;

    // Raised after a device has been placed (used by the first-person hands)
    public event System.Action<GameObject> DevicePlaced;

    void Start()
    {
        if (mainCamera == null)
            mainCamera = Camera.main;
    }

    void Update()
    {
        if (selectedPrefab == null)
            return;

        UpdateGhostPosition();

        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            CancelPlacement();
            return;
        }

        Mouse mouse = Mouse.current;
        if (mouse == null)
            return;

        Vector2 pointer = PointerUtil.ScreenPoint;

        if (mouse.leftButton.wasPressedThisFrame && (PointerUtil.CursorCaptured || !PointerUtil.IsOverUI(pointer)))
        {
            PlaceDevice();
        }

        if (mouse.rightButton.wasPressedThisFrame)
        {
            rightPressPosition = pointer;
        }

        if (mouse.rightButton.wasReleasedThisFrame &&
            Vector2.Distance(pointer, rightPressPosition) < CancelClickTolerance)
        {
            CancelPlacement();
        }
    }

    public void SelectPC()
    {
        StartPlacement(pcPrefab);
        Debug.Log("PC placement selected");
    }

    public void SelectSwitch()
    {
        StartPlacement(switchPrefab);
        Debug.Log("Switch placement selected");
    }

    public void SelectRouter()
    {
        StartPlacement(routerPrefab);
        Debug.Log("Router placement selected");
    }

    public void SelectServer()
    {
        StartPlacement(serverPrefab);
        Debug.Log("Server placement selected");
    }

    void StartPlacement(GameObject prefab)
    {
        CancelPlacement();

        selectedPrefab = prefab;

        if (selectedPrefab == null)
        {
            Debug.LogError("Selected prefab is missing.");
            return;
        }

        ghostObject = Instantiate(selectedPrefab);

        ghostObject.name = "PlacementPreview";

        // Keep the ghost out of device lists, naming and click handling
        NetworkDevice ghostDevice = ghostObject.GetComponent<NetworkDevice>();
        if (ghostDevice != null) ghostDevice.isPreviewModel = true;
        SetLayerRecursively(ghostObject, LayerMask.NameToLayer("Ignore Raycast"));

        NormalizeSize(ghostObject);
        if (realWorldScale) DeviceFurniture.Attach(ghostObject);

        DisablePhysics(ghostObject);

        SetGhostMaterial(validGhostMaterial);
    }

    void UpdateGhostPosition()
    {
        if (ghostObject == null)
            return;

        if (mainCamera == null)
            return;

        if (Mouse.current == null)
            return;

        Vector2 mousePosition = PointerUtil.ScreenPoint;

        Ray ray = mainCamera.ScreenPointToRay(mousePosition);

        Plane placementPlane = new Plane(Vector3.up, new Vector3(0f, floorHeight, 0f));

        if (!placementPlane.Raycast(ray, out float distance))
            return;

        Vector3 position = ray.GetPoint(distance);
        position.y = floorHeight;

        ghostObject.transform.position = position;
        RestOnFloor(ghostObject);

        bool valid = IsValidPlacement(ghostObject);

        if (valid)
            SetGhostMaterial(validGhostMaterial);
        else
            SetGhostMaterial(invalidGhostMaterial);
    }

    void PlaceDevice()
    {
        if (ghostObject == null)
            return;

        if (!IsValidPlacement(ghostObject))
        {
            Debug.LogWarning("Cannot place device here. Too close to another device.");
            return;
        }

        GameObject device = CreateDevice(selectedPrefab, ghostObject.transform.position, null);

        Destroy(ghostObject);

        ghostObject = null;
        selectedPrefab = null;

        NetworkDevice.MarkTopologyChanged();
        DevicePlaced?.Invoke(device);
        LabSounds.Play(LabSound.Place);

        if (NetworkVisualizer.Instance != null)
            NetworkVisualizer.Instance.Pulse(device.transform.position, NetworkVisualizer.Instance.hopColor, 1.4f);
    }

    GameObject CreateDevice(GameObject prefab, Vector3 position, string deviceName)
    {
        position.y = floorHeight;
        GameObject device = Instantiate(prefab, position, Quaternion.identity);

        NormalizeSize(device);
        if (realWorldScale) DeviceFurniture.Attach(device);
        RestOnFloor(device);

        if (string.IsNullOrEmpty(deviceName)) AssignDeviceName(device);
        else
        {
            device.name = deviceName;
            NetworkDevice network = device.GetComponent<NetworkDevice>();
            if (network != null) network.deviceName = deviceName;
        }

        return device;
    }

    public GameObject PrefabFor(string deviceType)
    {
        switch ((deviceType ?? "").ToLowerInvariant())
        {
            case "pc": return pcPrefab;
            case "switch": return switchPrefab;
            case "router": return routerPrefab;
            case "server": return serverPrefab;
            default: return null;
        }
    }

    // Creates a device directly (used when loading saved labs and missions)
    public NetworkDevice SpawnDevice(string deviceType, Vector3 position, string deviceName = null)
    {
        GameObject prefab = PrefabFor(deviceType);
        if (prefab == null)
        {
            Debug.LogError("SpawnDevice: unknown device type " + deviceType);
            return null;
        }

        GameObject device = CreateDevice(prefab, position, deviceName);
        NetworkDevice.MarkTopologyChanged();
        return device.GetComponent<NetworkDevice>();
    }

    public void CancelPlacement()
    {
        if (ghostObject != null)
        {
            Destroy(ghostObject);
            ghostObject = null;
        }

        selectedPrefab = null;
    }

    // Lift the model so the bottom of its bounds sits on the floor, whatever its pivot
    void RestOnFloor(GameObject device)
    {
        Renderer[] renderers = device.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
            return;

        float lowest = float.MaxValue;
        foreach (Renderer renderer in renderers)
        {
            if (renderer is ParticleSystemRenderer || renderer is TrailRenderer || renderer is LineRenderer)
                continue;

            lowest = Mathf.Min(lowest, renderer.bounds.min.y);
        }

        if (lowest == float.MaxValue)
            return;

        device.transform.position += Vector3.up * (floorHeight - lowest);
    }

    static void SetLayerRecursively(GameObject target, int layer)
    {
        if (layer < 0)
            return;

        foreach (Transform child in target.GetComponentsInChildren<Transform>(true))
        {
            child.gameObject.layer = layer;
        }
    }

    float TargetSize(GameObject device)
    {
        if (!realWorldScale) return targetDeviceSize;
        if (device.CompareTag("PC")) return pcSize;
        if (device.CompareTag("Switch")) return switchSize;
        if (device.CompareTag("Router")) return routerSize;
        if (device.CompareTag("Server")) return serverSize;
        return targetDeviceSize;
    }

    void NormalizeSize(GameObject device)
    {
        // Measure the model only, so the floating label doesn't shrink the device
        Bounds bounds = DeviceFurniture.ModelBounds(device);

        float largestDimension = Mathf.Max(
            bounds.size.x,
            bounds.size.y,
            bounds.size.z
        );

        if (largestDimension <= 0.001f)
            return;

        float scaleMultiplier = TargetSize(device) / largestDimension;

        device.transform.localScale *= scaleMultiplier;

        // Keep name labels readable however small or large the device is
        float labelCompensation = targetDeviceSize / TargetSize(device);
        foreach (FloatingLabel label in device.GetComponentsInChildren<FloatingLabel>(true))
        {
            label.transform.localScale *= labelCompensation;
        }
    }

    bool IsValidPlacement(GameObject device)
    {
        Renderer[] renderers = device.GetComponentsInChildren<Renderer>();

        if (renderers.Length == 0)
            return true;

        Bounds newBounds = renderers[0].bounds;

        for (int i = 1; i < renderers.Length; i++)
        {
            newBounds.Encapsulate(renderers[i].bounds);
        }

        newBounds.Expand(minimumSpacing);

        GameObject[] devices;

        devices = GameObject.FindGameObjectsWithTag("PC");
        if (IsOverlapping(newBounds, devices, device)) return false;

        devices = GameObject.FindGameObjectsWithTag("Switch");
        if (IsOverlapping(newBounds, devices, device)) return false;

        devices = GameObject.FindGameObjectsWithTag("Router");
        if (IsOverlapping(newBounds, devices, device)) return false;

        devices = GameObject.FindGameObjectsWithTag("Server");
        if (IsOverlapping(newBounds, devices, device)) return false;

        return true;
    }

    bool IsOverlapping(Bounds newBounds, GameObject[] existingDevices, GameObject currentDevice)
    {
        foreach (GameObject device in existingDevices)
        {
            if (device == null)
                continue;

            if (device == currentDevice)
                continue;

            NetworkDevice netDeviceScript = device.GetComponent<NetworkDevice>();
            if (netDeviceScript != null && netDeviceScript.isPreviewModel)
            {
                continue;
            }

            Renderer[] renderers = device.GetComponentsInChildren<Renderer>();

            if (renderers.Length == 0)
                continue;

            Bounds existingBounds = renderers[0].bounds;

            for (int i = 1; i < renderers.Length; i++)
            {
                existingBounds.Encapsulate(renderers[i].bounds);
            }

            if (newBounds.Intersects(existingBounds))
                return true;
        }

        return false;
    }

    void DisablePhysics(GameObject ghost)
    {
        Collider[] colliders = ghost.GetComponentsInChildren<Collider>();

        foreach (Collider collider in colliders)
        {
            collider.enabled = false;
        }

        Rigidbody[] rigidbodies = ghost.GetComponentsInChildren<Rigidbody>();

        foreach (Rigidbody rigidbody in rigidbodies)
        {
            rigidbody.isKinematic = true;
            rigidbody.useGravity = false;
        }
    }

    void SetGhostMaterial(Material material)
    {
        if (material == null)
            return;

        Renderer[] renderers = ghostObject.GetComponentsInChildren<Renderer>();

        foreach (Renderer renderer in renderers)
        {
            Material[] materials = renderer.materials;

            for (int i = 0; i < materials.Length; i++)
            {
                materials[i] = material;
            }

            renderer.materials = materials;
        }
    }

    private string GetNextAvailableName(string prefix)
    {
        HashSet<int> usedNumbers = new HashSet<int>();

        foreach (NetworkDevice device in NetworkDevice.All)
        {
            if (device != null && !string.IsNullOrEmpty(device.deviceName) && device.deviceName.StartsWith(prefix) && !device.isPreviewModel)
            {
                string numberPart = device.deviceName.Substring(prefix.Length);
                if (int.TryParse(numberPart, out int num))
                {
                    usedNumbers.Add(num);
                }
            }
        }

        int nextNumber = 1;
        while (usedNumbers.Contains(nextNumber))
        {
            nextNumber++;
        }

        return prefix + nextNumber;
    }

    void AssignDeviceName(GameObject device)
    {
        string prefix = "Device";

        if (device.CompareTag("PC")) prefix = "PC";
        else if (device.CompareTag("Switch")) prefix = "Switch";
        else if (device.CompareTag("Router")) prefix = "Router";
        else if (device.CompareTag("Server")) prefix = "Server";
        else Debug.LogWarning("Created device has no correct tag: " + device.name);

        string finalName = GetNextAvailableName(prefix);
        device.name = finalName;

        NetworkDevice netDeviceScript = device.GetComponent<NetworkDevice>();
        if (netDeviceScript != null)
        {
            netDeviceScript.deviceName = finalName;
        }
    }
}
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// The character's forearms and hands in walk mode. They rest at the bottom of the view, sway while
// walking, lift a miniature of the device being placed, and push forward when it is put down.
// Drawn by an overlay camera on their own layer so they never clip into walls or furniture.
[RequireComponent(typeof(Camera))]
public class FirstPersonHands : MonoBehaviour
{
    [Tooltip("Unused layer reserved for the first-person rig")]
    public int handsLayer = 30;
    public Color skinColor = new Color(0.87f, 0.67f, 0.53f);
    public Color sleeveColor = new Color(0.13f, 0.2f, 0.33f);
    public float heldDeviceSize = 0.2f;

    private Camera mainCamera;
    private Camera handsCamera;
    private Transform rig;
    private Transform leftHand, rightHand;
    private Transform heldDevice;
    private GameObject heldPrefab;
    private int previousCullingMask;
    private float placeKick;
    private float raise;

    private FirstPersonController walker;
    private DevicePlacementManager placement;

    // Pose = local position and Euler rotation relative to the camera
    struct Pose
    {
        public Vector3 position;
        public Vector3 euler;
        public Pose(Vector3 p, Vector3 e) { position = p; euler = e; }
    }

    // Idle hands rest just below the view; they come up only while carrying a device
    static readonly Pose LeftIdle = new Pose(new Vector3(-0.26f, -0.62f, 0.38f), new Vector3(10f, 18f, -12f));
    static readonly Pose RightIdle = new Pose(new Vector3(0.26f, -0.62f, 0.4f), new Vector3(10f, -18f, 12f));
    static readonly Pose LeftHold = new Pose(new Vector3(-0.13f, -0.135f, 0.41f), new Vector3(-20f, 32f, -55f));
    static readonly Pose RightHold = new Pose(new Vector3(0.13f, -0.135f, 0.41f), new Vector3(-20f, -32f, 55f));

    void Awake()
    {
        mainCamera = GetComponent<Camera>();
        walker = GetComponent<FirstPersonController>();
    }

    void OnEnable()
    {
        if (rig == null) BuildRig();

        placement = FindAnyObjectByType<DevicePlacementManager>();
        if (placement != null) placement.DevicePlaced += OnDevicePlaced;

        previousCullingMask = mainCamera.cullingMask;
        mainCamera.cullingMask &= ~(1 << handsLayer);

        UniversalAdditionalCameraData baseData = mainCamera.GetUniversalAdditionalCameraData();
        if (!baseData.cameraStack.Contains(handsCamera)) baseData.cameraStack.Add(handsCamera);

        handsCamera.enabled = true;
        rig.gameObject.SetActive(true);
    }

    void OnDisable()
    {
        if (placement != null) placement.DevicePlaced -= OnDevicePlaced;
        if (rig == null) return;

        mainCamera.cullingMask = previousCullingMask;
        UniversalAdditionalCameraData baseData = mainCamera.GetUniversalAdditionalCameraData();
        baseData.cameraStack.Remove(handsCamera);

        handsCamera.enabled = false;
        rig.gameObject.SetActive(false);
    }

    void OnDevicePlaced(GameObject device)
    {
        placeKick = 1f;
    }

    void LateUpdate()
    {
        handsCamera.fieldOfView = mainCamera.fieldOfView;

        GameObject selected = placement != null ? placement.SelectedPrefab : null;
        if (selected != heldPrefab) SetHeldDevice(selected);

        bool holding = heldDevice != null;
        raise = Mathf.MoveTowards(raise, holding ? 1f : 0f, Time.deltaTime * 4f);
        placeKick = Mathf.MoveTowards(placeKick, 0f, Time.deltaTime * 3.5f);

        float move = walker != null ? walker.MoveAmount : 0f;
        float phase = walker != null ? walker.BobPhase : 0f;
        Vector3 sway = new Vector3(Mathf.Cos(phase) * 0.012f, Mathf.Abs(Mathf.Sin(phase)) * 0.014f, 0f) * move;
        Vector3 breathe = new Vector3(0f, Mathf.Sin(Time.time * 1.6f) * 0.004f, 0f);

        // Quick forward push and dip right after placing a device
        float kick = Mathf.Sin(placeKick * Mathf.PI);
        Vector3 push = new Vector3(0f, -0.03f * kick, 0.12f * kick);

        float t = Mathf.SmoothStep(0f, 1f, raise);
        bool visible = raise > 0.001f || placeKick > 0.001f;
        leftHand.gameObject.SetActive(visible);
        rightHand.gameObject.SetActive(visible);
        ApplyPose(leftHand, LeftIdle, LeftHold, t, sway + breathe + push);
        ApplyPose(rightHand, RightIdle, RightHold, t, new Vector3(-sway.x, sway.y, 0f) + breathe + push);

        if (heldDevice != null)
        {
            heldDevice.localPosition = Vector3.Lerp(new Vector3(0f, -0.6f, 0.42f), new Vector3(0f, -0.085f, 0.44f), t) + sway * 0.8f + breathe + push;
            heldDevice.localRotation = Quaternion.Euler(-12f, 180f + Mathf.Sin(Time.time * 0.8f) * 6f, 0f);
        }
    }

    static void ApplyPose(Transform hand, Pose from, Pose to, float t, Vector3 offset)
    {
        hand.localPosition = Vector3.Lerp(from.position, to.position, t) + offset;
        hand.localRotation = Quaternion.Slerp(Quaternion.Euler(from.euler), Quaternion.Euler(to.euler), t);
    }

    // ---------- held device ----------

    void SetHeldDevice(GameObject prefab)
    {
        heldPrefab = prefab;
        if (heldDevice != null) Destroy(heldDevice.gameObject);
        heldDevice = null;

        if (prefab == null) return;

        heldDevice = VisualCopy(prefab, heldDeviceSize).transform;
        heldDevice.SetParent(rig, false);
        SetLayer(heldDevice.gameObject, handsLayer);
    }

    // Mesh-only copy of a prefab: no scripts, colliders or labels, scaled to fit in the hands
    static GameObject VisualCopy(GameObject prefab, float size)
    {
        GameObject root = new GameObject("HeldDevice");
        Transform content = new GameObject("Model").transform;
        content.SetParent(root.transform, false);

        Matrix4x4 toPrefab = prefab.transform.worldToLocalMatrix;

        foreach (Renderer source in prefab.GetComponentsInChildren<Renderer>(true))
        {
            if (source.GetComponentInParent<Canvas>(true) != null) continue;

            Mesh mesh = null;
            if (source is MeshRenderer && source.TryGetComponent(out MeshFilter filter)) mesh = filter.sharedMesh;
            else if (source is SkinnedMeshRenderer skinned) mesh = skinned.sharedMesh;
            if (mesh == null) continue;

            GameObject part = new GameObject(source.name, typeof(MeshFilter), typeof(MeshRenderer));
            part.transform.SetParent(content, false);

            Matrix4x4 local = toPrefab * source.transform.localToWorldMatrix;
            part.transform.localPosition = local.GetColumn(3);
            part.transform.localRotation = local.rotation;
            part.transform.localScale = local.lossyScale;

            part.GetComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = part.GetComponent<MeshRenderer>();
            renderer.sharedMaterials = source.sharedMaterials;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        // Centre the model and scale it to the requested size
        Renderer[] renderers = content.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer renderer in renderers) bounds.Encapsulate(renderer.bounds);

            content.localPosition = -bounds.center;
            float largest = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            if (largest > 0.0001f) root.transform.localScale = Vector3.one * (size / largest);
        }

        return root;
    }

    // ---------- rig construction ----------

    void BuildRig()
    {
        GameObject cameraObject = new GameObject("HandsCamera");
        cameraObject.transform.SetParent(transform, false);
        handsCamera = cameraObject.AddComponent<Camera>();
        handsCamera.cullingMask = 1 << handsLayer;
        handsCamera.nearClipPlane = 0.01f;
        handsCamera.farClipPlane = 3f;
        handsCamera.fieldOfView = mainCamera.fieldOfView;

        UniversalAdditionalCameraData overlay = handsCamera.GetUniversalAdditionalCameraData();
        overlay.renderType = CameraRenderType.Overlay;
        overlay.renderShadows = false;
        overlay.renderPostProcessing = false;

        rig = new GameObject("FirstPersonHands").transform;
        rig.SetParent(transform, false);

        Material skin = LabMaterials.Opaque(skinColor, Color.black, 0.35f);
        Material sleeve = LabMaterials.Opaque(sleeveColor, Color.black, 0.15f);
        Material cuff = LabMaterials.Opaque(sleeveColor * 0.7f, Color.black, 0.1f);
        Material watchBand = LabMaterials.Opaque(new Color(0.08f, 0.08f, 0.09f), Color.black, 0.5f);
        Material watchFace = LabMaterials.Opaque(new Color(0.02f, 0.03f, 0.05f), new Color(0.1f, 0.3f, 0.5f) * 0.35f, 0.9f);

        leftHand = BuildArm("LeftArm", -1f, skin, sleeve, cuff);
        rightHand = BuildArm("RightArm", 1f, skin, sleeve, cuff);

        // Smartwatch on the left wrist
        Part(leftHand, "WatchBand", PrimitiveType.Cylinder, new Vector3(0f, 0f, -0.035f), new Vector3(90f, 0f, 0f), new Vector3(0.068f, 0.012f, 0.058f), watchBand);
        Part(leftHand, "WatchFace", PrimitiveType.Cube, new Vector3(0f, 0.03f, -0.035f), Vector3.zero, new Vector3(0.026f, 0.006f, 0.03f), watchFace);

        SetLayer(rig.gameObject, handsLayer);
        rig.gameObject.SetActive(false);
    }

    // Arm built along +Z (towards the fingertips); side = -1 for left, +1 for right
    Transform BuildArm(string armName, float side, Material skin, Material sleeve, Material cuff)
    {
        Transform hand = new GameObject(armName).transform;
        hand.SetParent(rig, false);

        // Forearm in a sleeve reaching back out of view
        Part(hand, "Sleeve", PrimitiveType.Capsule, new Vector3(0f, 0f, -0.22f), new Vector3(90f, 0f, 0f), new Vector3(0.085f, 0.16f, 0.08f), sleeve);
        Part(hand, "Cuff", PrimitiveType.Cylinder, new Vector3(0f, 0f, -0.07f), new Vector3(90f, 0f, 0f), new Vector3(0.088f, 0.012f, 0.082f), cuff);
        Part(hand, "Wrist", PrimitiveType.Capsule, new Vector3(0f, 0f, -0.03f), new Vector3(90f, 0f, 0f), new Vector3(0.058f, 0.045f, 0.048f), skin);

        // Palm
        Part(hand, "Palm", PrimitiveType.Sphere, new Vector3(0f, 0f, 0.035f), Vector3.zero, new Vector3(0.085f, 0.032f, 0.095f), skin);

        // Four fingers, slightly curled
        for (int i = 0; i < 4; i++)
        {
            float x = (i - 1.5f) * 0.019f;
            float length = i == 1 || i == 2 ? 0.072f : 0.062f;
            if (i == 3) length = 0.052f;
            Part(hand, "Finger", PrimitiveType.Capsule, new Vector3(x, -0.004f, 0.08f + length * 0.3f), new Vector3(100f, 0f, 0f), new Vector3(0.017f, length / 2f, 0.016f), skin);
        }

        // Thumb on the inner side
        Part(hand, "Thumb", PrimitiveType.Capsule, new Vector3(-side * 0.045f, -0.006f, 0.045f), new Vector3(90f, -side * 38f, 0f), new Vector3(0.02f, 0.032f, 0.019f), skin);

        return hand;
    }

    static void Part(Transform parent, string partName, PrimitiveType type, Vector3 position, Vector3 euler, Vector3 scale, Material material)
    {
        GameObject part = GameObject.CreatePrimitive(type);
        part.name = partName;
        Destroy(part.GetComponent<Collider>());
        part.transform.SetParent(parent, false);
        part.transform.localPosition = position;
        part.transform.localRotation = Quaternion.Euler(euler);
        part.transform.localScale = scale;

        Renderer renderer = part.GetComponent<Renderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    static void SetLayer(GameObject target, int layer)
    {
        foreach (Transform child in target.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = layer;
    }
}

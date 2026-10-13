using TMPro;
using UnityEngine;

// Marks props (desk, cabinet) that hold up a device. Device-size and cable maths ignore them.
public class DeviceFurniture : MonoBehaviour
{
    private static Material wood, metal, darkMetal, fabric, glass;

    static void EnsureMaterials()
    {
        if (wood != null) return;
        wood = LabMaterials.Opaque(new Color(0.55f, 0.42f, 0.3f), Color.black, 0.35f);
        metal = LabMaterials.Opaque(new Color(0.62f, 0.64f, 0.66f), Color.black, 0.55f, 0.8f);
        darkMetal = LabMaterials.Opaque(new Color(0.12f, 0.13f, 0.14f), Color.black, 0.45f, 0.6f);
        fabric = LabMaterials.Opaque(new Color(0.16f, 0.18f, 0.22f), Color.black, 0.1f);
        glass = LabMaterials.Opaque(new Color(0.05f, 0.07f, 0.09f), new Color(0.02f, 0.05f, 0.08f), 0.9f);
    }

    // World-space bounds of the device model only (no furniture, labels or effects)
    public static Bounds ModelBounds(GameObject root, bool includeFurniture = false)
    {
        bool found = false;
        Bounds bounds = new Bounds(root.transform.position, Vector3.zero);

        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>())
        {
            if (renderer is LineRenderer || renderer is TrailRenderer || renderer is ParticleSystemRenderer) continue;
            if (renderer.GetComponent<TMP_Text>() != null) continue;
            if (!includeFurniture && renderer.GetComponentInParent<DeviceFurniture>() != null) continue;

            if (!found) bounds = renderer.bounds;
            else bounds.Encapsulate(renderer.bounds);
            found = true;
        }
        return bounds;
    }

    // Builds a desk (PCs) or a small network cabinet (switches/routers) under the device.
    // Call after the device has been scaled and before it is rested on the floor.
    public static void Attach(GameObject device)
    {
        NetworkDevice network = device.GetComponent<NetworkDevice>();
        if (network == null || device.GetComponentInChildren<DeviceFurniture>() != null) return;

        bool isPC = device.CompareTag("PC");
        bool isNetworkGear = network.IsSwitch || network.IsRouter;
        if (!isPC && !isNetworkGear) return;

        EnsureMaterials();
        Bounds model = ModelBounds(device);

        GameObject root = new GameObject(isPC ? "Desk" : "NetworkCabinet");
        root.AddComponent<DeviceFurniture>();

        // Build in world space, then parent so the device's scale doesn't distort the props
        root.transform.position = new Vector3(model.center.x, model.min.y, model.center.z);

        if (isPC) BuildDesk(root.transform, model);
        else BuildCabinet(root.transform, model);

        root.transform.SetParent(device.transform, true);
    }

    static void BuildDesk(Transform root, Bounds model)
    {
        float width = Mathf.Max(1.4f, model.size.x + 0.3f);
        float depth = Mathf.Max(0.75f, model.size.z + 0.25f);
        const float height = 0.74f;
        const float top = 0.04f;

        Part(root, "Top", new Vector3(0f, -top / 2f, 0f), new Vector3(width, top, depth), wood);

        float legX = width / 2f - 0.05f, legZ = depth / 2f - 0.05f;
        foreach (Vector2 corner in new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(-1, 1), new Vector2(1, 1) })
        {
            Part(root, "Leg", new Vector3(corner.x * legX, -top - (height - top) / 2f, corner.y * legZ), new Vector3(0.05f, height - top, 0.05f), metal);
        }
        Part(root, "ModestyPanel", new Vector3(0f, -0.38f, depth / 2f - 0.05f), new Vector3(width - 0.12f, 0.45f, 0.02f), metal);

        // Office chair in front of the desk
        Transform chair = new GameObject("Chair").transform;
        chair.SetParent(root, false);
        chair.localPosition = new Vector3(0f, -height, -depth / 2f - 0.45f);
        chair.localRotation = Quaternion.Euler(0f, Random.Range(-20f, 20f), 0f);

        Part(chair, "Base", new Vector3(0f, 0.05f, 0f), new Vector3(0.55f, 0.04f, 0.55f), darkMetal, PrimitiveType.Cylinder);
        Part(chair, "Column", new Vector3(0f, 0.25f, 0f), new Vector3(0.06f, 0.2f, 0.06f), metal, PrimitiveType.Cylinder);
        Part(chair, "Seat", new Vector3(0f, 0.47f, 0f), new Vector3(0.48f, 0.08f, 0.46f), fabric);
        Part(chair, "Back", new Vector3(0f, 0.8f, -0.22f), new Vector3(0.45f, 0.55f, 0.06f), fabric);

        // Mouse pad and a coffee mug make it read as a workstation
        Part(root, "MousePad", new Vector3(width / 2f - 0.35f, 0.002f, -depth / 2f + 0.25f), new Vector3(0.25f, 0.004f, 0.2f), fabric);
        Part(root, "Mug", new Vector3(-width / 2f + 0.2f, 0.05f, -depth / 2f + 0.2f), new Vector3(0.08f, 0.05f, 0.08f), glass, PrimitiveType.Cylinder);
    }

    static void BuildCabinet(Transform root, Bounds model)
    {
        float width = Mathf.Max(0.6f, model.size.x + 0.12f);
        float depth = Mathf.Max(0.6f, model.size.z + 0.12f);
        const float height = 0.8f;

        Part(root, "Body", new Vector3(0f, -height / 2f, 0f), new Vector3(width, height, depth), darkMetal);
        Part(root, "GlassDoor", new Vector3(0f, -height / 2f, -depth / 2f - 0.005f), new Vector3(width - 0.08f, height - 0.1f, 0.01f), glass);
        Part(root, "Handle", new Vector3(width / 2f - 0.08f, -height / 2f, -depth / 2f - 0.02f), new Vector3(0.02f, 0.18f, 0.02f), metal);

        // Patch panel inside the cabinet with a few port lights
        Part(root, "PatchPanel", new Vector3(0f, -0.2f, -depth / 2f + 0.06f), new Vector3(width - 0.14f, 0.05f, 0.02f), metal);
        for (int i = 0; i < 6; i++)
        {
            Material led = LabMaterials.Opaque(Color.black, (i % 3 == 0 ? new Color(1f, 0.6f, 0.1f) : new Color(0.2f, 1f, 0.3f)) * 2f, 0.8f);
            Part(root, "PortLed", new Vector3(-width / 2f + 0.12f + i * (width - 0.24f) / 5f, -0.2f, -depth / 2f + 0.045f), new Vector3(0.015f, 0.015f, 0.005f), led);
        }

        foreach (float x in new[] { -width / 2f + 0.05f, width / 2f - 0.05f })
        {
            Part(root, "Foot", new Vector3(x, -height - 0.02f, 0f), new Vector3(0.06f, 0.04f, depth - 0.1f), metal);
        }
    }

    static GameObject Part(Transform parent, string partName, Vector3 localPosition, Vector3 size, Material material, PrimitiveType type = PrimitiveType.Cube)
    {
        GameObject part = GameObject.CreatePrimitive(type);
        part.name = partName;
        part.transform.SetParent(parent, false);
        part.transform.localPosition = localPosition;
        // Unity's cylinder primitive is 2 units tall
        part.transform.localScale = type == PrimitiveType.Cylinder ? new Vector3(size.x, size.y / 2f, size.z) : size;
        part.GetComponent<Renderer>().sharedMaterial = material;
        return part;
    }
}

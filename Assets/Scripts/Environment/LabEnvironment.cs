using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Procedurally builds a realistic network-operations room around the simulator:
// raised-floor tiles, painted walls with windows and a door, a server-rack row with cable tray
// and cooling unit, an office corner, daylight and post-processing. Walls between the camera
// and the room are cut away (dollhouse view). Added automatically by LabBootstrap.
public class LabEnvironment : MonoBehaviour
{
    [Header("Room")]
    public float roomWidth = 28f;
    public float roomDepth = 22f;
    public float roomCenterZ = 2f;
    public float wallHeight = 3.2f;

    [Header("Look")]
    public Color floorColor = new Color(0.6f, 0.62f, 0.65f);
    public Color wallColor = new Color(0.8f, 0.79f, 0.76f);
    public bool applyLighting = true;
    public bool postProcessing = true;
    [Tooltip("Hide walls that stand between the camera and the room")]
    public bool cutawayWalls = true;
    public int serverRacks = 8;

    private Material paint, baseboard, darkMetal, metal, rackDoor, wood, glassDaylight, whiteboard, plastic, screen, plantGreen, terracotta, red, yellowTape, aluminium;

    private readonly List<Material> ledMaterials = new List<Material>();
    private readonly List<Color> ledColors = new List<Color>();
    private readonly List<float> ledPhases = new List<float>();
    private readonly List<WallGroup> walls = new List<WallGroup>();

    // Footprints the first-person character can't walk through (racks, desks, plants...)
    public readonly List<Bounds> Obstacles = new List<Bounds>();

    // Walkable floor area inside the walls
    public Rect WalkableArea => Rect.MinMaxRect(Left + 0.35f, Front + 0.35f, Right - 0.35f, Back - 0.35f);
    private Camera mainCamera;

    class WallGroup
    {
        public Transform root;
        public Vector3 outward;
        public Renderer[] renderers;
        public bool hidden;
    }

    float Floor
    {
        get
        {
            DevicePlacementManager placement = FindAnyObjectByType<DevicePlacementManager>();
            return placement != null ? placement.floorHeight : 0f;
        }
    }

    float Left => -roomWidth / 2f;
    float Right => roomWidth / 2f;
    float Back => roomCenterZ + roomDepth / 2f;
    float Front => roomCenterZ - roomDepth / 2f;

    void Start()
    {
        mainCamera = Camera.main;
        transform.position = new Vector3(0f, Floor, 0f);

        CreateMaterials();
        BuildFloor();
        BuildWalls();
        BuildServerRow();
        BuildOfficeCorner();

        if (applyLighting) ApplyLighting();
        if (postProcessing) SetupPostProcessing();
    }

    // ---------- materials & textures ----------

    void CreateMaterials()
    {
        paint = LabMaterials.Opaque(wallColor, Color.black, 0.15f);
        LabMaterials.SetTexture(paint, NoiseTexture(128, 0.03f), new Vector2(6f, 1.5f), false);

        baseboard = LabMaterials.Opaque(new Color(0.22f, 0.22f, 0.23f), Color.black, 0.3f);
        darkMetal = LabMaterials.Opaque(new Color(0.09f, 0.095f, 0.1f), Color.black, 0.5f, 0.7f);
        metal = LabMaterials.Opaque(new Color(0.6f, 0.62f, 0.64f), Color.black, 0.6f, 0.85f);
        aluminium = LabMaterials.Opaque(new Color(0.75f, 0.76f, 0.78f), Color.black, 0.55f, 0.9f);
        wood = LabMaterials.Opaque(new Color(0.5f, 0.36f, 0.24f), Color.black, 0.35f);
        whiteboard = LabMaterials.Opaque(new Color(0.95f, 0.95f, 0.96f), Color.black, 0.85f);
        plastic = LabMaterials.Opaque(new Color(0.15f, 0.15f, 0.16f), Color.black, 0.35f);
        screen = LabMaterials.Opaque(new Color(0.02f, 0.03f, 0.05f), new Color(0.08f, 0.16f, 0.3f), 0.9f);
        plantGreen = LabMaterials.Opaque(new Color(0.16f, 0.36f, 0.14f), Color.black, 0.2f);
        terracotta = LabMaterials.Opaque(new Color(0.6f, 0.33f, 0.2f), Color.black, 0.2f);
        red = LabMaterials.Opaque(new Color(0.75f, 0.06f, 0.05f), Color.black, 0.6f);
        yellowTape = LabMaterials.Opaque(new Color(0.95f, 0.78f, 0.05f), Color.black, 0.3f);

        // Bright, slightly blue panes read as daylight coming through the windows
        glassDaylight = LabMaterials.Opaque(new Color(0.75f, 0.85f, 0.95f), new Color(0.75f, 0.88f, 1f) * 1.3f, 0.95f);

        rackDoor = LabMaterials.Opaque(Color.white, Color.black, 0.4f, 0.6f);
        LabMaterials.SetTexture(rackDoor, PerforationTexture(), new Vector2(6f, 20f), false);

        Color[] ledPalette = { new Color(0.2f, 1f, 0.35f), new Color(0.2f, 1f, 0.35f), new Color(1f, 0.6f, 0.1f), new Color(0.3f, 0.6f, 1f) };
        for (int i = 0; i < 8; i++)
        {
            Color color = ledPalette[i % ledPalette.Length] * 3f;
            ledMaterials.Add(LabMaterials.Opaque(Color.black, color, 0.8f));
            ledColors.Add(color);
            ledPhases.Add(Random.Range(0f, 100f));
        }
    }

    static Texture2D NoiseTexture(int size, float amount)
    {
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, name = "PaintNoise" };
        System.Random random = new System.Random(7);
        Color[] pixels = new Color[size * size];
        for (int i = 0; i < pixels.Length; i++)
        {
            float v = 1f - amount + (float)random.NextDouble() * amount;
            pixels[i] = new Color(v, v, v, 1f);
        }
        texture.SetPixels(pixels);
        texture.Apply(true, true);
        return texture;
    }

    // 8×8 raised-floor tiles (60 cm each) with seams, per-tile tint, speckle and perforated vent tiles
    static Texture2D FloorTileTexture(Color baseColor)
    {
        const int size = 1024, tiles = 8;
        int tilePixels = size / tiles;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, true)
        {
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Trilinear,
            anisoLevel = 8,
            name = "RaisedFloor"
        };

        System.Random random = new System.Random(11);
        float[] tint = new float[tiles * tiles];
        for (int i = 0; i < tint.Length; i++) tint[i] = 0.96f + (float)random.NextDouble() * 0.07f;

        Color[] pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int tx = x / tilePixels, ty = y / tilePixels;
                int lx = x % tilePixels, ly = y % tilePixels;
                float shade = tint[ty * tiles + tx] * (0.985f + (float)random.NextDouble() * 0.03f);

                bool seam = lx < 2 || ly < 2 || lx >= tilePixels - 1 || ly >= tilePixels - 1;
                bool bevel = lx < 4 || ly < 4;
                bool vent = (tx * 3 + ty * 5) % 13 == 0;

                if (seam) shade *= 0.55f;
                else if (bevel) shade *= 0.93f;
                else if (vent && lx > 10 && ly > 10 && lx < tilePixels - 10 && ly < tilePixels - 10 && lx % 8 < 4 && ly % 8 < 4) shade *= 0.45f;

                Color c = baseColor * shade;
                c.a = 1f;
                pixels[y * size + x] = c;
            }
        }

        texture.SetPixels(pixels);
        texture.Apply(true, true);
        return texture;
    }

    static Texture2D PerforationTexture()
    {
        const int size = 64;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, name = "RackPerforation" };
        Color[] pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int ox = (y / 8) % 2 == 0 ? 0 : 4;
                int dx = (x + ox) % 8 - 4, dy = y % 8 - 4;
                float v = dx * dx + dy * dy < 6 ? 0.015f : 0.11f;
                pixels[y * size + x] = new Color(v, v, v * 1.1f, 1f);
            }
        }
        texture.SetPixels(pixels);
        texture.Apply(true, true);
        return texture;
    }

    // ---------- room ----------

    void BuildFloor()
    {
        GameObject floor = Part(transform, "RaisedFloor", new Vector3(0f, 0f, roomCenterZ), new Vector3(roomWidth / 10f, 1f, roomDepth / 10f), null, PrimitiveType.Plane);
        Material material = LabMaterials.Opaque(Color.white, Color.black, 0.45f);
        LabMaterials.SetTexture(material, FloorTileTexture(floorColor), new Vector2(roomWidth / 4.8f, roomDepth / 4.8f), false);
        floor.GetComponent<Renderer>().sharedMaterial = material;
        floor.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
    }

    void BuildWalls()
    {
        const float thickness = 0.2f;

        // Back wall: sign above the rack row
        Transform back = WallRoot("BackWall", new Vector3(0f, 0f, Back), Vector3.forward, new Vector3(roomWidth + thickness * 2f, wallHeight, thickness));
        Sign(back, "NETWORK OPERATIONS LAB", new Vector3(0f, 2.8f, -thickness / 2f - 0.01f), Quaternion.identity, 2.4f, new Color(0.18f, 0.2f, 0.24f));
        Part(back, "SignBar", new Vector3(0f, 2.55f, -thickness / 2f - 0.01f), new Vector3(6.5f, 0.03f, 0.01f), yellowTape);

        // Front wall (usually cut away): windows
        Transform front = WallRoot("FrontWall", new Vector3(0f, 0f, Front), Vector3.back, new Vector3(roomWidth + thickness * 2f, wallHeight, thickness));
        for (int i = -1; i <= 1; i++) Window(front, new Vector3(i * 7f, 1.65f, thickness / 2f + 0.01f), Quaternion.Euler(0f, 180f, 0f));

        // Left wall: a row of daylight windows
        Transform left = WallRoot("LeftWall", new Vector3(Left, 0f, roomCenterZ), Vector3.left, new Vector3(thickness, wallHeight, roomDepth));
        for (int i = -1; i <= 1; i++) Window(left, new Vector3(thickness / 2f + 0.01f, 1.65f, i * 6f), Quaternion.Euler(0f, -90f, 0f));

        // Right wall: door, whiteboard and fire extinguisher
        Transform right = WallRoot("RightWall", new Vector3(Right, 0f, roomCenterZ), Vector3.right, new Vector3(thickness, wallHeight, roomDepth));
        Door(right, new Vector3(-thickness / 2f - 0.01f, 0f, -roomDepth / 2f + 2.5f));
        Whiteboard(right, new Vector3(-thickness / 2f - 0.02f, 1.5f, 1.5f));
        Part(right, "Extinguisher", new Vector3(-thickness / 2f - 0.12f, 0.45f, -roomDepth / 2f + 4f), new Vector3(0.16f, 0.5f, 0.16f), red, PrimitiveType.Cylinder);
        Part(right, "ExtinguisherTop", new Vector3(-thickness / 2f - 0.12f, 0.98f, -roomDepth / 2f + 4f), new Vector3(0.07f, 0.06f, 0.07f), plastic, PrimitiveType.Cylinder);
    }

    Transform WallRoot(string wallName, Vector3 basePosition, Vector3 outward, Vector3 size)
    {
        Transform root = new GameObject(wallName).transform;
        root.SetParent(transform, false);
        root.localPosition = basePosition;
        root.localRotation = Quaternion.identity;

        Part(root, "Wall", new Vector3(0f, size.y / 2f, 0f), size, paint);

        // Baseboard on the room side
        bool alongZ = Mathf.Abs(outward.x) > 0.5f;
        Vector3 inward = -outward;
        Vector3 boardSize = new Vector3(alongZ ? 0.02f : size.x, 0.1f, alongZ ? size.z : 0.02f);
        float halfThickness = (alongZ ? size.x : size.z) / 2f;
        Part(root, "Baseboard", inward * (halfThickness + 0.01f) + Vector3.up * 0.05f, boardSize, baseboard);

        walls.Add(new WallGroup { root = root, outward = outward });
        return root;
    }

    void Window(Transform wall, Vector3 localPosition, Quaternion rotation)
    {
        Transform window = new GameObject("Window").transform;
        window.SetParent(wall, false);
        window.localPosition = localPosition;
        window.localRotation = rotation;

        const float width = 2.2f, height = 1.5f;
        Part(window, "Glass", Vector3.zero, new Vector3(width, height, 0.01f), glassDaylight);
        Part(window, "FrameTop", new Vector3(0f, height / 2f, -0.02f), new Vector3(width + 0.1f, 0.06f, 0.05f), aluminium);
        Part(window, "FrameBottom", new Vector3(0f, -height / 2f, -0.02f), new Vector3(width + 0.1f, 0.06f, 0.05f), aluminium);
        Part(window, "FrameLeft", new Vector3(-width / 2f, 0f, -0.02f), new Vector3(0.06f, height, 0.05f), aluminium);
        Part(window, "FrameRight", new Vector3(width / 2f, 0f, -0.02f), new Vector3(0.06f, height, 0.05f), aluminium);
        Part(window, "Mullion", new Vector3(0f, 0f, -0.02f), new Vector3(0.04f, height, 0.04f), aluminium);
        Part(window, "Sill", new Vector3(0f, -height / 2f - 0.05f, -0.08f), new Vector3(width + 0.2f, 0.04f, 0.18f), whiteboard);
    }

    void Door(Transform wall, Vector3 localPosition)
    {
        Transform door = new GameObject("Door").transform;
        door.SetParent(wall, false);
        door.localPosition = localPosition;
        door.localRotation = Quaternion.Euler(0f, 90f, 0f);

        Part(door, "Slab", new Vector3(0f, 1.05f, 0f), new Vector3(1.0f, 2.1f, 0.04f), wood);
        Part(door, "FrameTop", new Vector3(0f, 2.13f, -0.01f), new Vector3(1.16f, 0.08f, 0.06f), baseboard);
        Part(door, "FrameLeft", new Vector3(-0.54f, 1.05f, -0.01f), new Vector3(0.08f, 2.1f, 0.06f), baseboard);
        Part(door, "FrameRight", new Vector3(0.54f, 1.05f, -0.01f), new Vector3(0.08f, 2.1f, 0.06f), baseboard);
        Part(door, "Handle", new Vector3(0.38f, 1.0f, -0.05f), new Vector3(0.14f, 0.025f, 0.025f), aluminium);
        Part(door, "Plate", new Vector3(0f, 1.55f, -0.025f), new Vector3(0.3f, 0.1f, 0.005f), aluminium);
        Sign(door, "SERVER ROOM", new Vector3(0f, 1.55f, -0.03f), Quaternion.identity, 0.35f, new Color(0.15f, 0.15f, 0.17f));
    }

    void Whiteboard(Transform wall, Vector3 localPosition)
    {
        Transform board = new GameObject("Whiteboard").transform;
        board.SetParent(wall, false);
        board.localPosition = localPosition;
        board.localRotation = Quaternion.Euler(0f, 90f, 0f);

        Part(board, "Board", Vector3.zero, new Vector3(2.4f, 1.2f, 0.02f), whiteboard);
        Part(board, "Frame", new Vector3(0f, 0f, 0.01f), new Vector3(2.48f, 1.28f, 0.015f), aluminium);
        Part(board, "Tray", new Vector3(0f, -0.64f, -0.05f), new Vector3(1.2f, 0.03f, 0.08f), aluminium);

        TextMeshPro notes = Sign(board, "VLAN 10  Staff     192.168.1.0/24\nVLAN 20  Servers  192.168.2.0/24\nGateway .1     DNS .53", new Vector3(0f, 0.05f, -0.015f), Quaternion.identity, 0.9f, new Color(0.1f, 0.25f, 0.65f));
        notes.alignment = TextAlignmentOptions.Left;
        notes.rectTransform.sizeDelta = new Vector2(2.2f, 1f);
    }

    void BuildServerRow()
    {
        if (serverRacks <= 0) return;

        const float rackWidth = 0.6f, rackDepth = 1.1f, rackHeight = 2.0f;
        float rowZ = Back - 0.1f - rackDepth / 2f - 0.3f;
        float startX = -(serverRacks - 1) * rackWidth / 2f;
        System.Random random = new System.Random(42);

        for (int r = 0; r < serverRacks; r++)
        {
            Transform rack = new GameObject("Rack").transform;
            rack.SetParent(transform, false);
            rack.localPosition = new Vector3(startX + r * rackWidth, 0f, rowZ);

            Part(rack, "Body", new Vector3(0f, rackHeight / 2f, 0f), new Vector3(rackWidth - 0.01f, rackHeight, rackDepth), darkMetal);
            Part(rack, "Door", new Vector3(0f, rackHeight / 2f + 0.02f, -rackDepth / 2f - 0.006f), new Vector3(rackWidth - 0.05f, rackHeight - 0.12f, 0.01f), rackDoor);
            Part(rack, "Handle", new Vector3(rackWidth / 2f - 0.07f, 1.1f, -rackDepth / 2f - 0.02f), new Vector3(0.02f, 0.22f, 0.02f), metal);
            Part(rack, "Plinth", new Vector3(0f, 0.025f, 0f), new Vector3(rackWidth - 0.02f, 0.05f, rackDepth - 0.02f), plastic);
            Sign(rack, "RACK A" + (r + 1).ToString("00"), new Vector3(0f, rackHeight - 0.06f, -rackDepth / 2f - 0.012f), Quaternion.identity, 0.32f, new Color(0.85f, 0.87f, 0.9f));

            // Equipment lights glinting through the perforated door
            for (int i = 0; i < 26; i++)
            {
                float y = 0.2f + (float)random.NextDouble() * 1.55f;
                float x = -0.2f + (float)random.NextDouble() * 0.4f;
                Part(rack, "Led", new Vector3(x, y, -rackDepth / 2f - 0.012f), new Vector3(0.012f, 0.012f, 0.004f), ledMaterials[random.Next(ledMaterials.Count)]);
            }
        }

        float rowLength = serverRacks * rackWidth;
        AddObstacle(new Vector3(0f, 0f, rowZ), new Vector3(rowLength + 0.1f, 2f, rackDepth + 0.1f));

        // Overhead ladder cable tray running along the row
        const float trayY = 2.45f;
        Part(transform, "TrayRailFront", new Vector3(0f, trayY, rowZ - 0.2f), new Vector3(rowLength + 1.6f, 0.06f, 0.025f), metal);
        Part(transform, "TrayRailBack", new Vector3(0f, trayY, rowZ + 0.2f), new Vector3(rowLength + 1.6f, 0.06f, 0.025f), metal);
        for (float x = -rowLength / 2f - 0.75f; x <= rowLength / 2f + 0.75f; x += 0.3f)
        {
            Part(transform, "TrayRung", new Vector3(x, trayY - 0.02f, rowZ), new Vector3(0.025f, 0.02f, 0.4f), metal);
        }
        foreach (float x in new[] { -rowLength / 2f - 0.6f, rowLength / 2f + 0.6f })
        {
            Part(transform, "TrayHanger", new Vector3(x, (trayY + wallHeight) / 2f, rowZ), new Vector3(0.02f, wallHeight - trayY, 0.02f), metal);
        }

        // Bundled cables lying in the tray
        Material[] bundle = { LabMaterials.Opaque(new Color(0.12f, 0.32f, 0.85f), Color.black, 0.4f), LabMaterials.Opaque(new Color(0.95f, 0.75f, 0.1f), Color.black, 0.4f) };
        for (int i = 0; i < 4; i++)
        {
            Part(transform, "TrayCable", new Vector3(0f, trayY + 0.015f, rowZ - 0.12f + i * 0.07f), new Vector3(rowLength + 1.4f, 0.03f, 0.03f), bundle[i % 2]);
        }

        // Precision cooling unit at the end of the row
        Transform crac = new GameObject("CoolingUnit").transform;
        crac.SetParent(transform, false);
        crac.localPosition = new Vector3(rowLength / 2f + 0.75f, 0f, rowZ);
        AddObstacle(crac.localPosition, new Vector3(1.05f, 2f, 0.95f));
        Material cracBody = LabMaterials.Opaque(new Color(0.82f, 0.83f, 0.84f), Color.black, 0.35f);
        Part(crac, "Body", new Vector3(0f, 0.97f, 0f), new Vector3(1.0f, 1.94f, 0.9f), cracBody);
        for (int i = 0; i < 9; i++)
        {
            Part(crac, "Grille", new Vector3(0f, 0.25f + i * 0.08f, -0.455f), new Vector3(0.85f, 0.025f, 0.01f), darkMetal);
        }
        Part(crac, "Display", new Vector3(0.25f, 1.55f, -0.455f), new Vector3(0.22f, 0.12f, 0.01f), screen);
        Sign(crac, "21.5°C", new Vector3(0.25f, 1.55f, -0.462f), Quaternion.identity, 0.45f, new Color(0.5f, 0.85f, 1f));

        // Yellow safety tape marking the aisle in front of the racks
        float tapeFront = rowZ - rackDepth / 2f - 1.2f;
        float tapeBack = rowZ - rackDepth / 2f - 0.05f;
        float halfX = rowLength / 2f + 0.3f;
        Part(transform, "Tape", new Vector3(0f, 0.002f, tapeFront), new Vector3(halfX * 2f, 0.003f, 0.06f), yellowTape);
        Part(transform, "Tape", new Vector3(-halfX, 0.002f, (tapeFront + tapeBack) / 2f), new Vector3(0.06f, 0.003f, tapeBack - tapeFront), yellowTape);
        Part(transform, "Tape", new Vector3(halfX, 0.002f, (tapeFront + tapeBack) / 2f), new Vector3(0.06f, 0.003f, tapeBack - tapeFront), yellowTape);
    }

    void BuildOfficeCorner()
    {
        // Two staff desks by the windows plus some plants
        for (int i = 0; i < 2; i++)
        {
            Transform desk = new GameObject("OfficeDesk").transform;
            desk.SetParent(transform, false);
            desk.localPosition = new Vector3(Left + 1.1f, 0f, roomCenterZ - 5f + i * 2.2f);
            desk.localRotation = Quaternion.Euler(0f, -90f, 0f);
            AddObstacle(desk.localPosition + new Vector3(0.25f, 0f, 0f), new Vector3(1.4f, 1f, 1.7f));

            Part(desk, "Top", new Vector3(0f, 0.74f, 0f), new Vector3(1.6f, 0.04f, 0.8f), wood);
            foreach (float x in new[] { -0.75f, 0.75f })
            {
                Part(desk, "Side", new Vector3(x, 0.36f, 0f), new Vector3(0.04f, 0.72f, 0.75f), metal);
            }
            Part(desk, "MonitorStand", new Vector3(0f, 0.77f, 0.2f), new Vector3(0.2f, 0.02f, 0.15f), plastic);
            Part(desk, "MonitorNeck", new Vector3(0f, 0.89f, 0.22f), new Vector3(0.04f, 0.22f, 0.03f), plastic);
            Part(desk, "Monitor", new Vector3(0f, 1.1f, 0.2f), new Vector3(0.62f, 0.38f, 0.03f), plastic);
            Part(desk, "Screen", new Vector3(0f, 1.1f, 0.184f), new Vector3(0.58f, 0.34f, 0.002f), screen);
            Part(desk, "Keyboard", new Vector3(0f, 0.765f, -0.1f), new Vector3(0.44f, 0.015f, 0.14f), plastic);

            Transform chair = new GameObject("Chair").transform;
            chair.SetParent(desk, false);
            chair.localPosition = new Vector3(0.1f, 0f, -0.75f);
            chair.localRotation = Quaternion.Euler(0f, 15f - i * 25f, 0f);
            Part(chair, "Base", new Vector3(0f, 0.05f, 0f), new Vector3(0.55f, 0.04f, 0.55f), darkMetal, PrimitiveType.Cylinder);
            Part(chair, "Column", new Vector3(0f, 0.25f, 0f), new Vector3(0.06f, 0.2f, 0.06f), metal, PrimitiveType.Cylinder);
            Part(chair, "Seat", new Vector3(0f, 0.47f, 0f), new Vector3(0.48f, 0.08f, 0.46f), plastic);
            Part(chair, "Back", new Vector3(0f, 0.8f, -0.22f), new Vector3(0.45f, 0.55f, 0.06f), plastic);
        }

        Plant(new Vector3(Left + 0.6f, 0f, roomCenterZ - 7.5f));
        Plant(new Vector3(Right - 0.6f, 0f, Front + 0.8f));
        Plant(new Vector3(Left + 0.6f, 0f, Back - 0.8f));
    }

    void Plant(Vector3 position)
    {
        Transform plant = new GameObject("Plant").transform;
        plant.SetParent(transform, false);
        plant.localPosition = position;
        AddObstacle(position, new Vector3(0.6f, 1f, 0.6f));

        Part(plant, "Pot", new Vector3(0f, 0.22f, 0f), new Vector3(0.4f, 0.44f, 0.4f), terracotta, PrimitiveType.Cylinder);
        Part(plant, "Leaves", new Vector3(0f, 0.75f, 0f), new Vector3(0.7f, 0.7f, 0.7f), plantGreen, PrimitiveType.Sphere);
        Part(plant, "Leaves", new Vector3(0.12f, 1.05f, 0.05f), new Vector3(0.45f, 0.5f, 0.45f), plantGreen, PrimitiveType.Sphere);
        Part(plant, "Leaves", new Vector3(-0.1f, 0.95f, -0.1f), new Vector3(0.4f, 0.45f, 0.4f), plantGreen, PrimitiveType.Sphere);
    }

    // ---------- lighting ----------

    void ApplyLighting()
    {
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.55f, 0.58f, 0.63f);
        RenderSettings.ambientEquatorColor = new Color(0.42f, 0.42f, 0.42f);
        RenderSettings.ambientGroundColor = new Color(0.24f, 0.23f, 0.22f);
        RenderSettings.fog = false;

        foreach (Light light in FindObjectsByType<Light>())
        {
            if (light.type != LightType.Directional) continue;

            // Late-afternoon sun slanting in through the left-hand windows
            light.transform.rotation = Quaternion.Euler(48f, 62f, 0f);
            light.color = new Color(1f, 0.97f, 0.93f);
            light.intensity = 1.5f;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.8f;
        }
    }

    void SetupPostProcessing()
    {
        if (mainCamera != null)
        {
            UniversalAdditionalCameraData cameraData = mainCamera.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = true;
            cameraData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            cameraData.renderShadows = true;
        }

        VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();

        Tonemapping tonemapping = profile.Add<Tonemapping>();
        tonemapping.mode.Override(TonemappingMode.ACES);

        Bloom bloom = profile.Add<Bloom>();
        bloom.threshold.Override(1.05f);
        bloom.intensity.Override(0.35f);
        bloom.scatter.Override(0.6f);

        ColorAdjustments color = profile.Add<ColorAdjustments>();
        color.postExposure.Override(0.2f);
        color.contrast.Override(10f);
        color.saturation.Override(-4f);

        Vignette vignette = profile.Add<Vignette>();
        vignette.intensity.Override(0.2f);
        vignette.smoothness.Override(0.45f);

        GameObject volumeObject = new GameObject("LabPostProcessing");
        volumeObject.transform.SetParent(transform, false);
        Volume volume = volumeObject.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 50f;
        volume.sharedProfile = profile;
    }

    // ---------- per frame ----------

    void LateUpdate()
    {
        for (int i = 0; i < ledMaterials.Count; i++)
        {
            float flicker = Mathf.PerlinNoise(Time.time * 3f, ledPhases[i]);
            ledMaterials[i].SetColor("_EmissionColor", ledColors[i] * (flicker > 0.38f ? 1f : 0.1f));
        }

        if (!cutawayWalls || mainCamera == null) return;

        Vector3 cameraPosition = mainCamera.transform.position;
        foreach (WallGroup wall in walls)
        {
            if (wall.renderers == null) wall.renderers = wall.root.GetComponentsInChildren<Renderer>();

            bool hide = Vector3.Dot(cameraPosition - wall.root.position, wall.outward) > 0f;
            if (hide == wall.hidden) continue;

            wall.hidden = hide;
            foreach (Renderer renderer in wall.renderers)
            {
                // Hidden walls still cast shadows so the light in the room stays believable
                renderer.shadowCastingMode = hide ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
            }
        }
    }

    // ---------- helpers ----------

    void AddObstacle(Vector3 localCenter, Vector3 size)
    {
        Obstacles.Add(new Bounds(transform.TransformPoint(localCenter), size));
    }

    TextMeshPro Sign(Transform parent, string text, Vector3 localPosition, Quaternion localRotation, float fontSize, Color color)
    {
        GameObject signObject = new GameObject("Text");
        signObject.transform.SetParent(parent, false);
        signObject.transform.localPosition = localPosition;
        signObject.transform.localRotation = localRotation;

        TextMeshPro tmp = signObject.AddComponent<TextMeshPro>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.rectTransform.sizeDelta = new Vector2(8f, 1f);
        return tmp;
    }

    GameObject Part(Transform parent, string partName, Vector3 localPosition, Vector3 size, Material material, PrimitiveType type = PrimitiveType.Cube)
    {
        GameObject part = GameObject.CreatePrimitive(type);
        part.name = partName;
        part.transform.SetParent(parent, false);
        part.transform.localPosition = localPosition;
        part.transform.localScale = type == PrimitiveType.Cylinder ? new Vector3(size.x, size.y / 2f, size.z) : size;
        if (material != null) part.GetComponent<Renderer>().sharedMaterial = material;

        // Decorative only: keep it out of physics and click raycasts
        Collider collider = part.GetComponent<Collider>();
        if (collider != null) Destroy(collider);

        return part;
    }
}

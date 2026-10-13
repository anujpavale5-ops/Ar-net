using UnityEngine;
using UnityEngine.Rendering;

// Runtime materials built on URP/Lit, which the project's own materials already pull into builds.
public static class LabMaterials
{
    private static Shader litShader;

    static Shader Lit
    {
        get
        {
            if (litShader == null) litShader = Shader.Find("Universal Render Pipeline/Lit");
            if (litShader == null) litShader = Shader.Find("Standard");
            return litShader;
        }
    }

    public static Material Opaque(Color baseColor, Color emission, float smoothness = 0.3f, float metallic = 0f)
    {
        Material material = new Material(Lit);
        material.SetFloat("_Smoothness", smoothness);
        material.SetFloat("_Glossiness", smoothness);
        material.SetFloat("_Metallic", metallic);
        SetColors(material, baseColor, emission);
        return material;
    }

    public static Material Transparent(Color baseColor, Color emission)
    {
        Material material = Opaque(baseColor, emission, 0f);

        material.SetFloat("_Surface", 1f);
        material.SetFloat("_Blend", 0f);
        material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_ZWrite", 0f);
        material.SetFloat("_ReceiveShadows", 0f);
        material.SetOverrideTag("RenderType", "Transparent");
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.DisableKeyword("_ALPHATEST_ON");
        material.renderQueue = (int)RenderQueue.Transparent;
        material.SetShaderPassEnabled("ShadowCaster", false);
        return material;
    }

    public static void SetColors(Material material, Color baseColor, Color emission)
    {
        material.SetColor("_BaseColor", baseColor);
        material.SetColor("_Color", baseColor);

        bool emissive = emission.maxColorComponent > 0.0001f;
        if (emissive) material.EnableKeyword("_EMISSION");
        else material.DisableKeyword("_EMISSION");

        material.SetColor("_EmissionColor", emission);
        material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
    }

    public static void SetTexture(Material material, Texture texture, Vector2 tiling, bool alsoEmission)
    {
        material.SetTexture("_BaseMap", texture);
        material.SetTexture("_MainTex", texture);
        material.SetTextureScale("_BaseMap", tiling);
        material.SetTextureScale("_MainTex", tiling);

        if (alsoEmission) material.SetTexture("_EmissionMap", texture);
    }

    public static void SetEmissionMap(Material material, Texture texture)
    {
        material.SetTexture("_EmissionMap", texture);
    }

    public static void SetOffset(Material material, Vector2 offset)
    {
        material.SetTextureOffset("_BaseMap", offset);
        material.SetTextureOffset("_MainTex", offset);
    }

    // Seamless grid: thin minor lines every cell, thicker major lines on the tile edge
    public static Texture2D GridTexture(int size, int cells, Color background, Color minor, Color major, int minorWidth = 2, int majorWidth = 5)
    {
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, true)
        {
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Trilinear,
            anisoLevel = 8,
            name = "LabGrid"
        };

        Color[] pixels = new Color[size * size];
        float cellSize = size / (float)cells;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Color color = background;
                if (IsLine(x, size, size, majorWidth) || IsLine(y, size, size, majorWidth)) color = major;
                else if (IsLine(x, cellSize, size, minorWidth) || IsLine(y, cellSize, size, minorWidth)) color = minor;
                pixels[y * size + x] = color;
            }
        }

        texture.SetPixels(pixels);
        texture.Apply(true, true);
        return texture;
    }

    static bool IsLine(int coordinate, float spacing, int size, int width)
    {
        float position = coordinate % spacing;
        float half = width * 0.5f;
        return position < half || position >= spacing - half;
    }

    // Horizontal dashes with transparent gaps, for animated flow lines
    public static Texture2D DashTexture(int width = 64, float fill = 0.55f)
    {
        Texture2D texture = new Texture2D(width, 4, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Bilinear,
            name = "LabDash"
        };

        Color[] pixels = new Color[width * 4];
        for (int x = 0; x < width; x++)
        {
            Color color = x < width * fill ? Color.white : new Color(1f, 1f, 1f, 0f);
            for (int y = 0; y < 4; y++) pixels[y * width + x] = color;
        }

        texture.SetPixels(pixels);
        texture.Apply(false, true);
        return texture;
    }
}

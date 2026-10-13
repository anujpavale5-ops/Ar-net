using UnityEngine;
using System.Collections;

public class DeviceLED : MonoBehaviour
{
    [Header("Colors")]
    [ColorUsage(true, true)] public Color idleColor = Color.green; // Normal color
    [ColorUsage(true, true)] public Color activeColor = Color.white; // Flash color (Data moving)
    [ColorUsage(true, true)] public Color errorColor = Color.red;    // Flash color (Error)

    private Material ledMaterial;

    void Awake()
    {
        // --- THE FIX ---
        // Check if the parent device is a UI preview model. If so, hide the LED!
        NetworkDevice parentDevice = GetComponentInParent<NetworkDevice>();
        if (parentDevice != null && parentDevice.isPreviewModel)
        {
            gameObject.SetActive(false); // Turn off the LED object entirely
            return; // Stop running the rest of this method
        }

        // Normal setup for actual devices
        Renderer rend = GetComponent<Renderer>();
        if (rend != null)
        {
            ledMaterial = rend.material;
            ledMaterial.EnableKeyword("_EMISSION");
            SetColor(idleColor);
        }
    }

    private bool powered = true;

    public void SetPowered(bool on)
    {
        powered = on;
        StopAllCoroutines();
        SetColor(on ? idleColor : Color.black);
    }

    public void BlinkData()
    {
        if (!powered) return;
        StopAllCoroutines();
        StartCoroutine(BlinkRoutine(activeColor));
    }

    public void BlinkError()
    {
        if (!powered) return;
        StopAllCoroutines();
        StartCoroutine(BlinkRoutine(errorColor));
    }

    private IEnumerator BlinkRoutine(Color flashColor)
    {
        if (ledMaterial == null) yield break;

        // Flash bright instantly
        SetColor(flashColor);
        
        // Wait a tiny fraction of a second
        yield return new WaitForSeconds(0.15f);

        // Fade smoothly back to idle
        float fadeTime = 0.5f;
        float timer = 0;
        
        while (timer < fadeTime)
        {
            timer += Time.deltaTime;
            Color lerpedColor = Color.Lerp(flashColor, idleColor, timer / fadeTime);
            SetColor(lerpedColor);
            yield return null;
        }

        SetColor(idleColor);
    }

    private void SetColor(Color color)
    {
        if (ledMaterial != null)
        {
            ledMaterial.SetColor("_EmissionColor", color);
        }
    }
}
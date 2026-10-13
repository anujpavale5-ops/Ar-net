using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class NetworkStatisticsUI : MonoBehaviour
{
    public TMP_Text packetsSentText;
    public TMP_Text packetsDeliveredText;
    public TMP_Text packetsFailedText;
    public TMP_Text successRateText;
    public TMP_Text totalHopsText;

    [Tooltip("Draw a coloured bar under the success rate")]
    public bool showSuccessBar = true;

    private RectTransform successFill;
    private Image successFillImage;
    private float shownRate;

    private void Start()
    {
        if (showSuccessBar) BuildSuccessBar();
        Refresh();
    }

    private void Update()
    {
        Refresh();
    }

    void BuildSuccessBar()
    {
        if (successRateText == null) return;

        // Thin bar hugging the bottom edge of the statistics panel
        Transform panel = successRateText.transform.parent != null ? successRateText.transform.parent : successRateText.transform;

        GameObject track = new GameObject("SuccessBar", typeof(RectTransform), typeof(Image));
        track.transform.SetParent(panel, false);

        RectTransform trackRect = track.GetComponent<RectTransform>();
        trackRect.anchorMin = new Vector2(0f, 0f);
        trackRect.anchorMax = new Vector2(1f, 0f);
        trackRect.pivot = new Vector2(0.5f, 0f);
        trackRect.anchoredPosition = new Vector2(0f, 5f);
        trackRect.sizeDelta = new Vector2(-20f, 5f);
        Image trackImage = track.GetComponent<Image>();
        trackImage.color = new Color(1f, 1f, 1f, 0.08f);
        trackImage.raycastTarget = false;

        GameObject fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fill.transform.SetParent(track.transform, false);

        successFill = fill.GetComponent<RectTransform>();
        successFill.anchorMin = Vector2.zero;
        successFill.anchorMax = new Vector2(0f, 1f);
        successFill.offsetMin = Vector2.zero;
        successFill.offsetMax = Vector2.zero;
        successFillImage = fill.GetComponent<Image>();
        successFillImage.raycastTarget = false;
    }

    public void Refresh()
    {
        if (NetworkStatistics.Instance == null)
            return;

        NetworkStatistics stats = NetworkStatistics.Instance;
        int sent = stats.packetsSent;
        int delivered = stats.packetsDelivered;
        int failed = stats.packetsFailed;
        int hops = stats.totalHops;
        float successRate = sent > 0 ? (float)delivered / sent * 100f : 0f;

        Color rateColor = sent == 0 ? UITheme.TextSecondary
            : successRate >= 80f ? UITheme.Success
            : successRate >= 50f ? UITheme.Warning
            : UITheme.Failure;

        SetLine(packetsSentText, "Packets Sent", sent.ToString(), UITheme.TextPrimary);
        SetLine(packetsDeliveredText, "Delivered", delivered.ToString(), delivered > 0 ? UITheme.Success : UITheme.TextPrimary);
        SetLine(packetsFailedText, "Failed", failed.ToString(), failed > 0 ? UITheme.Failure : UITheme.TextPrimary);
        SetLine(successRateText, "Success Rate", sent > 0 ? successRate.ToString("F1") + "%" : "—", rateColor);
        SetLine(totalHopsText, "Total Hops", hops.ToString(), UITheme.Accent);

        if (successFill != null)
        {
            shownRate = Mathf.MoveTowards(shownRate, successRate / 100f, Time.unscaledDeltaTime * 1.5f);
            successFill.anchorMax = new Vector2(shownRate, 1f);
            successFillImage.color = rateColor;
        }
    }

    static void SetLine(TMP_Text text, string label, string value, Color valueColor)
    {
        if (text == null) return;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        text.text = $"<color={UITheme.Hex(UITheme.TextSecondary)}>{label}</color>  <b><color={UITheme.Hex(valueColor)}>{value}</color></b>";
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class NetworkDevice : MonoBehaviour, IPointerClickHandler
{
    public static readonly List<NetworkDevice> All = new List<NetworkDevice>();

    // Bumped whenever devices, cables or addressing change so UI/visuals can refresh lazily
    public static int TopologyVersion { get; private set; }
    public static void MarkTopologyChanged() => TopologyVersion++;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        All.Clear();
        TopologyVersion = 0;
    }

    public string deviceName;
    public string deviceType;
    public string macAddress;

    [Header("Network Info")]
    [Tooltip("Routers may list several interface addresses separated by commas")]
    public string ipAddress = "";
    public string subnetMask = "";
    public string gateway = "";

    public bool isOnline = true;
    public bool isPreviewModel = false;

    [Header("Router Access List")]
    [Tooltip("Checked top to bottom on routers; first match wins, unmatched traffic is permitted")]
    public List<AclRule> accessList = new List<AclRule>();

    public bool IsSwitch => string.Equals(deviceType, "switch", System.StringComparison.OrdinalIgnoreCase);
    public bool IsRouter => string.Equals(deviceType, "router", System.StringComparison.OrdinalIgnoreCase);
    public bool CanForward => IsSwitch || IsRouter;

    public List<uint> Addresses => IpUtil.ParseList(ipAddress);

    public bool TryGetMask(out uint mask)
    {
        return IpUtil.TryParse(subnetMask, out mask) && IpUtil.IsValidMask(subnetMask);
    }

    public bool IsConfigured => Addresses.Count > 0 && TryGetMask(out _);

    private Bounds modelBounds, fullBounds;
    private Vector3 boundsCachedAt;
    private Vector3 boundsCachedScale;
    private bool boundsValid;

    void RefreshBounds()
    {
        if (boundsValid && transform.position == boundsCachedAt && transform.lossyScale == boundsCachedScale) return;

        modelBounds = DeviceFurniture.ModelBounds(gameObject);
        fullBounds = DeviceFurniture.ModelBounds(gameObject, true);
        boundsCachedAt = transform.position;
        boundsCachedScale = transform.lossyScale;
        boundsValid = true;
    }

    // Centre of the device model (without desk/cabinet), used for aim assist
    public Vector3 ModelCenter
    {
        get
        {
            RefreshBounds();
            return modelBounds.center;
        }
    }

    // Where cables plug in: low on the device body, above any desk or cabinet
    public Vector3 PortPosition
    {
        get
        {
            RefreshBounds();
            Vector3 center = modelBounds.center;
            return new Vector3(center.x, modelBounds.min.y + Mathf.Min(0.12f, modelBounds.size.y * 0.25f), center.z);
        }
    }

    // Horizontal radius of the device including its furniture
    public float FootprintRadius
    {
        get
        {
            RefreshBounds();
            return Mathf.Max(fullBounds.extents.x, fullBounds.extents.z);
        }
    }

    protected virtual void OnEnable()
    {
        All.Add(this);
        MarkTopologyChanged();
    }

    protected virtual void OnDisable()
    {
        All.Remove(this);
        MarkTopologyChanged();
    }

    public void SetNetworkConfiguration(string ip, string subnet, string gatewayAddress)
    {
        ipAddress = ip;
        subnetMask = subnet;
        gateway = gatewayAddress;
        MarkTopologyChanged();
    }

    public void SetStatus(bool status)
    {
        isOnline = status;
        MarkTopologyChanged();

        DeviceLED led = GetComponentInChildren<DeviceLED>();
        if (led != null) led.SetPowered(status);

        FloatingLabel label = GetComponentInChildren<FloatingLabel>();
        if (label != null) label.RefreshText();
    }

    // Router firewall check for traffic passing through this device
    public bool Blocks(uint source, uint destination, out AclRule matchedRule)
    {
        matchedRule = null;
        if (!IsRouter) return false;

        foreach (AclRule rule in accessList)
        {
            if (rule != null && rule.Matches(source, destination))
            {
                matchedRule = rule;
                return !rule.permit;
            }
        }
        return false;
    }

    public bool HasAddress(uint address)
    {
        return Addresses.Contains(address);
    }

    public bool IsInSameSubnet(string targetIP)
    {
        return IpUtil.TryParse(targetIP, out uint target) && IsInSameSubnet(target);
    }

    public bool IsInSameSubnet(uint target)
    {
        if (!TryGetMask(out uint mask)) return false;

        foreach (uint own in Addresses)
        {
            if (IpUtil.SameSubnet(own, target, mask)) return true;
        }
        return false;
    }

    public bool SharesSubnetWith(NetworkDevice other)
    {
        if (other == null) return false;

        foreach (uint address in other.Addresses)
        {
            if (IsInSameSubnet(address)) return true;
        }
        return false;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) return;

        // In captured walk mode LabViewMode handles clicks from the crosshair instead
        if (PointerUtil.CursorCaptured) return;

        HandleClick();
    }

    // Left-click on this device: delete, connect, send or open its terminal depending on the active tool
    public void HandleClick()
    {
        if (isPreviewModel) return;

        // The command console works on whichever device was clicked last
        if (LabConsole.Instance != null) LabConsole.Instance.SetDevice(this);

        DevicePlacementManager placement = FindAnyObjectByType<DevicePlacementManager>();
        if (placement != null && placement.IsPlacing) return;

        DeviceDeletionManager deleteManager = FindAnyObjectByType<DeviceDeletionManager>();
        if (deleteManager != null && deleteManager.isDeletionMode)
        {
            deleteManager.DeleteDevice(this);
            return;
        }

        DeviceConnector connector = FindAnyObjectByType<DeviceConnector>();
        if (connector != null && connector.isConnectingMode)
        {
            connector.SelectDevice(this);
            return;
        }

        PacketSender sender = FindAnyObjectByType<PacketSender>();
        if (sender != null && sender.isSendingMode)
        {
            sender.SelectDevice(this);
            return;
        }

        if (!IsSwitch)
        {
            DeviceConfigUI configUI = FindAnyObjectByType<DeviceConfigUI>();
            if (configUI != null) configUI.OpenTerminal(this);
        }
    }
}

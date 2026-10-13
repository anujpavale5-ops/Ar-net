using UnityEngine;
using TMPro;

public class DeviceConfigUI : MonoBehaviour
{
    [Header("UI Panels")]
    public GameObject configPanel;
    public TMP_Text titleText;

    [Header("Input Fields")]
    public TMP_InputField ipInputField;
    public TMP_InputField subnetInputField;
    public TMP_InputField gatewayInputField;

    private NetworkDevice activeDevice;
    private Color defaultTitleColor = Color.white;

    void Start()
    {
        if (configPanel != null) configPanel.SetActive(false);
        if (titleText != null) defaultTitleColor = titleText.color;

        if (ipInputField != null)
        {
            ipInputField.onEndEdit.AddListener(AutoFillSubnetMask);
        }
    }

    void Update()
    {
        // The device was deleted while its terminal was open
        if (configPanel != null && configPanel.activeSelf && activeDevice == null)
        {
            configPanel.SetActive(false);
        }
    }

    public void OpenTerminal(NetworkDevice device)
    {
        if (device == null) return;

        activeDevice = device;
        SetTitle(device.deviceName + " Terminal", defaultTitleColor);

        ipInputField.text = device.ipAddress;
        subnetInputField.text = device.subnetMask;
        gatewayInputField.text = device.gateway;

        if (ipInputField.placeholder is TMP_Text placeholder && device.IsRouter)
        {
            placeholder.text = "e.g. 192.168.1.1, 192.168.2.1";
        }

        configPanel.SetActive(true);
    }

    public void CloseIfEditing(NetworkDevice device)
    {
        if (activeDevice == device) CloseTerminal();
    }

    // Classful default mask as a convenience when the user leaves the mask blank
    private void AutoFillSubnetMask(string ipEntered)
    {
        if (!string.IsNullOrEmpty(subnetInputField.text)) return;

        string[] octets = ipEntered.Split('.');

        if (octets.Length >= 1 && int.TryParse(octets[0], out int firstOctet))
        {
            if (firstOctet >= 1 && firstOctet <= 126) subnetInputField.text = "255.0.0.0";
            else if (firstOctet >= 128 && firstOctet <= 191) subnetInputField.text = "255.255.0.0";
            else if (firstOctet >= 192 && firstOctet <= 223) subnetInputField.text = "255.255.255.0";
        }
    }

    public void ApplyChanges()
    {
        if (activeDevice == null)
        {
            CloseTerminal();
            return;
        }

        string ip = ipInputField.text.Trim();
        string mask = subnetInputField.text.Trim();
        string gateway = gatewayInputField.text.Trim();

        if (!Validate(ip, mask, gateway, out string error))
        {
            SetTitle(error, new Color(1f, 0.35f, 0.3f));
            return;
        }

        activeDevice.SetNetworkConfiguration(ip, mask, gateway);

        FloatingLabel label = activeDevice.GetComponentInChildren<FloatingLabel>();
        if (label != null) label.RefreshText();

        CloseTerminal();
    }

    bool Validate(string ip, string mask, string gateway, out string error)
    {
        error = null;

        // Clearing everything un-configures the device
        if (ip.Length == 0 && mask.Length == 0 && gateway.Length == 0) return true;

        if (ip.Length == 0) { error = "IP address is required"; return false; }

        if (!activeDevice.IsRouter && ip.Contains(","))
        {
            error = "Only routers can have multiple IPs";
            return false;
        }

        if (!IpUtil.IsValidList(ip, out error)) return false;

        if (!IpUtil.IsValidMask(mask))
        {
            error = "Invalid subnet mask (e.g. 255.255.255.0)";
            return false;
        }

        if (gateway.Length == 0) return true;

        if (!IpUtil.TryParse(gateway, out uint gatewayAddress))
        {
            error = "Invalid gateway address";
            return false;
        }

        IpUtil.TryParse(mask, out uint maskValue);
        foreach (uint address in IpUtil.ParseList(ip))
        {
            if (address == gatewayAddress)
            {
                error = "Gateway cannot be the device's own IP";
                return false;
            }
            if (IpUtil.SameSubnet(address, gatewayAddress, maskValue)) return true;
        }

        error = "Gateway must be in the same subnet";
        return false;
    }

    void SetTitle(string text, Color color)
    {
        if (titleText == null) return;
        titleText.text = text;
        titleText.color = color;
    }

    public void CloseTerminal()
    {
        activeDevice = null;
        if (configPanel != null) configPanel.SetActive(false);
    }
}

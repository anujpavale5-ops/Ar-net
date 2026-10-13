using UnityEngine;
using UnityEngine.EventSystems;

public class DeviceClick : MonoBehaviour
{
    public DeviceInfoPanel infoPanel;

    void Start()
    {
        if (infoPanel == null)
        {
            infoPanel = FindAnyObjectByType<DeviceInfoPanel>();
        }
    }

    void OnMouseDown()
    {
        if (EventSystem.current != null &&
            EventSystem.current.IsPointerOverGameObject())
        {
            return;
        }

        NetworkDevice device = GetComponent<NetworkDevice>();

        if (device == null)
            return;

        if (infoPanel == null)
            return;

        infoPanel.ShowDevice(device);
    }
}
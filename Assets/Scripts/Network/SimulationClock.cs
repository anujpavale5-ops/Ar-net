using UnityEngine;

// Speed, pause and single-step control for packet traffic.
// Only the simulation slows down; the camera, UI and animations keep running normally.
public static class SimulationClock
{
    public static float Speed { get; set; } = 1f;
    public static bool Paused { get; set; }

    // When stepping, traffic runs until the next packet reaches a device, then pauses again
    public static bool Stepping { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Speed = 1f;
        Paused = false;
        Stepping = false;
    }

    public static float DeltaTime => Paused ? 0f : Time.deltaTime * Speed;

    public static void Step()
    {
        Stepping = true;
        Paused = false;
    }

    public static void NotifyHop()
    {
        if (!Stepping) return;
        Stepping = false;
        Paused = true;
    }

    public static void TogglePause()
    {
        Stepping = false;
        Paused = !Paused;
    }
}

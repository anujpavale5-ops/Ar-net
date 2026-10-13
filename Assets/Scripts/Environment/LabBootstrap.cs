using UnityEngine;
using UnityEngine.SceneManagement;

// Adds the lab environment, visualizer and orbit camera to any scene that runs the simulator.
// Add the components to the scene yourself to tweak their settings; the bootstrap then skips them.
public static class LabBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Register()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (Object.FindAnyObjectByType<NetworkManager>() == null) return;

        UITheme.ApplyToScene();

        if (Object.FindAnyObjectByType<LabEnvironment>() == null)
            new GameObject("LabEnvironment").AddComponent<LabEnvironment>();

        if (Object.FindAnyObjectByType<NetworkVisualizer>() == null)
            new GameObject("NetworkVisualizer").AddComponent<NetworkVisualizer>();

        // Save/load, command console, toolbar and guided missions
        if (Object.FindAnyObjectByType<TopologyIO>() == null)
        {
            GameObject features = new GameObject("LabFeatures");
            features.AddComponent<TopologyIO>();
            features.AddComponent<LabConsole>();
            features.AddComponent<LabToolbar>();
            features.AddComponent<MissionManager>();
        }

        Camera mainCamera = Camera.main;
        // Orbit overview plus Tab-to-walk first-person mode
        if (mainCamera != null && mainCamera.GetComponent<LabViewMode>() == null)
            mainCamera.gameObject.AddComponent<LabViewMode>();

        // The device-preview cameras each carry an AudioListener; keep only the main camera's
        foreach (AudioListener listener in Object.FindObjectsByType<AudioListener>())
        {
            if (mainCamera == null || listener.gameObject != mainCamera.gameObject) listener.enabled = false;
        }
    }
}

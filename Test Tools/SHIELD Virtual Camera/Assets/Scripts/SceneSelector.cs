using System;
using System.IO;
using UnityEngine;

// Reads the day/night selection written by the external STE control system
// and swaps the scene's skybox in place. Used to switch scenes entirely;
// now that Day/Night are just two skybox materials in one scene, swapping
// RenderSettings.skybox avoids the cost (and the loss of live sim state)
// of a full scene reload.
public class SceneSelector : MonoBehaviour
{
    [Tooltip("Applied when VirtualEnvironment.txt contains 0.")]
    public Material daySkybox;
    [Tooltip("Applied when VirtualEnvironment.txt contains 1.")]
    public Material nightSkybox;

    [Tooltip("How often (in seconds) to re-check VirtualEnvironment.txt for changes.")]
    public float pollInterval = 1f;

    private static readonly string SharedDataPath = Path.Combine(Application.dataPath, "STE Shared Data", "VirtualEnvironment.txt");

    private int? appliedValue;
    private float nextPollTime;

    void Start()
    {
        ApplyFromFile();
    }

    void Update()
    {
        if (Time.unscaledTime < nextPollTime)
            return;

        nextPollTime = Time.unscaledTime + pollInterval;
        ApplyFromFile();
    }

    private void ApplyFromFile()
    {
        if (!File.Exists(SharedDataPath))
        {
            Debug.LogError("VirtualEnvironment.txt not found at: " + SharedDataPath);
            return;
        }

        int value;
        try
        {
            string line = File.ReadAllLines(SharedDataPath)[0];
            value = int.Parse(line);
        }
        catch (Exception ex)
        {
            Debug.LogError("Error reading VirtualEnvironment.txt: " + ex.Message);
            return;
        }

        if (value == appliedValue)
            return;

        switch (value)
        {
            case 0:
                ApplySkybox(daySkybox);
                appliedValue = value;
                break;
            case 1:
                ApplySkybox(nightSkybox);
                appliedValue = value;
                break;
            default:
                Debug.LogError($"VirtualEnvironment.txt contains unrecognized value '{value}' (expected 0 for day or 1 for night).");
                break;
        }
    }

    private void ApplySkybox(Material skybox)
    {
        RenderSettings.skybox = skybox;
        DynamicGI.UpdateEnvironment();
    }
}
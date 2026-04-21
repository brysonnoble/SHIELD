using UnityEngine;
using UnityEngine.SceneManagement;
using System.IO;

public class SceneSelector : MonoBehaviour
{
    void Start()
    {
        string sharedDataPath = Path.Combine(Application.dataPath, "STE Shared Data", "VirtualEnvironment.txt");

        if (File.Exists(sharedDataPath))
        {
            try
            {
                string sceneIndexStr = File.ReadAllLines(sharedDataPath)[0];
                int sceneIndex = int.Parse(sceneIndexStr);
                SceneManager.LoadScene(sceneIndex);
            }
            catch (System.Exception ex)
            {
                Debug.LogError("Error loading scene from VirtualEnvironment.txt: " + ex.Message);
            }
        }
        else
        {
            Debug.LogError("VirtualEnvironment.txt not found at: " + sharedDataPath);
        }
    }
}

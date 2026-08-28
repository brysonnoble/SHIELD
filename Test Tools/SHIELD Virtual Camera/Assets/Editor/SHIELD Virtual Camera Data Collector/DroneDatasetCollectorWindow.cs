using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace SHIELD.EditorTools
{
    // Generates a synthetic drone-detection dataset: instantiates drone prefabs at
    // randomized positions (biased so most frames have at least one drone inside the
    // camera's FOV), randomizes the camera's rotation and the day/night skybox, and
    // renders 1920x1080 JPEGs paired with YOLO-format label .txt files (classId cx cy
    // w h, normalized 0-1, one line per visible drone). Labels are computed from the
    // known ground-truth transforms rather than hand-annotated.
    //
    // Runs entirely in Edit Mode (no Play Mode) so instantiated drones never get a
    // frame of Update() to fly off/animate away from where they were placed before
    // the screenshot is taken.
    public class DroneDatasetCollectorWindow : EditorWindow
    {
        [Serializable]
        private class DronePrefabEntry
        {
            public GameObject prefab;
            public int classId;
        }

        private struct SpawnedDrone
        {
            public GameObject GameObject;
            public int ClassId;
        }

        private const int ImageWidth = 1920;
        private const int ImageHeight = 1080;

        // Below this normalized box area, a drone clipped almost entirely out of
        // frame is dropped from the label rather than written as a near-zero sliver.
        private const float MinNormalizedBoxArea = 0.0002f;

        private int screenshotCount = 200;
        private string outputFolder;
        private List<DronePrefabEntry> dronePrefabs = new List<DronePrefabEntry>();
        private Material daySkybox;
        private Material nightSkybox;
        private Camera targetCamera;

        private float minDistance = 10f;
        private float maxDistance = 30f;
        private float droneVisibleChance = 0.9f;

        private float cameraPitchMin = -50f;
        private float cameraPitchMax = 10f;
        private float cameraRollMin = -25f;
        private float cameraRollMax = 25f;

        private int jpegQuality = 90;

        [MenuItem("SHIELD/Virtual Camera Data Collector")]
        private static void Open()
        {
            GetWindow<DroneDatasetCollectorWindow>("Drone Dataset Collector");
        }

        private void OnEnable()
        {
            if (dronePrefabs.Count == 0) AutoFindDronePrefabs();
            if (targetCamera == null) targetCamera = Camera.main;
            if (daySkybox == null || nightSkybox == null) AutoFindSkyboxes();
            if (string.IsNullOrEmpty(outputFolder)) outputFolder = DefaultOutputFolder();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
            screenshotCount = EditorGUILayout.IntField(
                new GUIContent("Screenshot Count", "How many image/label pairs to generate this run."),
                screenshotCount);

            EditorGUILayout.BeginHorizontal();
            outputFolder = EditorGUILayout.TextField("Output Folder", outputFolder);
            if (GUILayout.Button("Browse", GUILayout.Width(60)))
            {
                string picked = EditorUtility.OpenFolderPanel("Choose Output Folder", outputFolder, "");
                if (!string.IsNullOrEmpty(picked)) outputFolder = picked;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Scene References", EditorStyles.boldLabel);
            targetCamera = (Camera)EditorGUILayout.ObjectField("Camera", targetCamera, typeof(Camera), true);
            daySkybox = (Material)EditorGUILayout.ObjectField("Day Skybox", daySkybox, typeof(Material), false);
            nightSkybox = (Material)EditorGUILayout.ObjectField("Night Skybox", nightSkybox, typeof(Material), false);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Drone Prefabs", EditorStyles.boldLabel);
            for (int i = 0; i < dronePrefabs.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                dronePrefabs[i].prefab = (GameObject)EditorGUILayout.ObjectField(
                    dronePrefabs[i].prefab, typeof(GameObject), false);
                dronePrefabs[i].classId = EditorGUILayout.IntField("Class", dronePrefabs[i].classId, GUILayout.Width(140));
                if (GUILayout.Button("-", GUILayout.Width(24)))
                {
                    dronePrefabs.RemoveAt(i);
                    GUIUtility.ExitGUI();
                }
                EditorGUILayout.EndHorizontal();
            }
            if (GUILayout.Button("Add Prefab Slot")) dronePrefabs.Add(new DronePrefabEntry());

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Placement", EditorStyles.boldLabel);
            minDistance = EditorGUILayout.FloatField("Min Distance", minDistance);
            maxDistance = EditorGUILayout.FloatField("Max Distance", maxDistance);
            droneVisibleChance = EditorGUILayout.Slider(
                new GUIContent("Guaranteed-Visible Chance", "Fraction of frames where at least one drone is deliberately placed inside the camera's FOV."),
                droneVisibleChance, 0f, 1f);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Camera Rotation Range (degrees)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Yaw is always randomized across the full 0-360 range. This camera represents another drone, not a fixed security camera, so pitch/roll are wide but still bounded away from full tumbling/upside-down.", MessageType.None);
            cameraPitchMin = EditorGUILayout.FloatField("Pitch Min", cameraPitchMin);
            cameraPitchMax = EditorGUILayout.FloatField("Pitch Max", cameraPitchMax);
            cameraRollMin = EditorGUILayout.FloatField("Roll Min", cameraRollMin);
            cameraRollMax = EditorGUILayout.FloatField("Roll Max", cameraRollMax);

            EditorGUILayout.Space();
            jpegQuality = EditorGUILayout.IntSlider("JPEG Quality", jpegQuality, 10, 100);

            EditorGUILayout.Space();
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorGUILayout.HelpBox("Exit Play Mode before generating - this tool renders in Edit Mode so drones stay exactly where they're placed.", MessageType.Warning);
            }

            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (GUILayout.Button("Generate Dataset", GUILayout.Height(32)))
                {
                    GenerateDataset();
                }
            }
        }

        private void GenerateDataset()
        {
            if (targetCamera == null)
            {
                EditorUtility.DisplayDialog("Drone Dataset Collector", "Assign a camera first.", "OK");
                return;
            }
            var validPrefabs = dronePrefabs.Where(p => p.prefab != null).ToList();
            if (validPrefabs.Count == 0)
            {
                EditorUtility.DisplayDialog("Drone Dataset Collector", "Assign at least one drone prefab.", "OK");
                return;
            }
            if (daySkybox == null || nightSkybox == null)
            {
                EditorUtility.DisplayDialog("Drone Dataset Collector", "Assign both day and night skybox materials.", "OK");
                return;
            }
            if (screenshotCount <= 0 || maxDistance <= minDistance || cameraPitchMax < cameraPitchMin || cameraRollMax < cameraRollMin)
            {
                EditorUtility.DisplayDialog("Drone Dataset Collector", "Check screenshot count / distance / rotation range values.", "OK");
                return;
            }

            Directory.CreateDirectory(outputFolder);
            int startIndex = FindNextIndex(outputFolder);

            Vector3 originalPosition = targetCamera.transform.position;
            Quaternion originalRotation = targetCamera.transform.rotation;
            RenderTexture originalTargetTexture = targetCamera.targetTexture;
            Material originalSkybox = RenderSettings.skybox;

            var rt = new RenderTexture(ImageWidth, ImageHeight, 24, RenderTextureFormat.ARGB32);
            var tex = new Texture2D(ImageWidth, ImageHeight, TextureFormat.RGB24, false);
            var frameDrones = new List<SpawnedDrone>();

            try
            {
                targetCamera.targetTexture = rt;

                for (int i = 0; i < screenshotCount; i++)
                {
                    if (EditorUtility.DisplayCancelableProgressBar("Generating Drone Dataset",
                            $"Frame {i + 1} / {screenshotCount}", (float)i / screenshotCount))
                    {
                        break;
                    }

                    RenderSettings.skybox = UnityEngine.Random.value < 0.5f ? daySkybox : nightSkybox;

                    float yaw = UnityEngine.Random.Range(0f, 360f);
                    float pitch = UnityEngine.Random.Range(cameraPitchMin, cameraPitchMax);
                    float roll = UnityEngine.Random.Range(cameraRollMin, cameraRollMax);
                    targetCamera.transform.rotation = Quaternion.Euler(pitch, yaw, roll);

                    int droneCount = UnityEngine.Random.Range(1, 4); // 1-3 inclusive
                    bool guaranteeVisible = UnityEngine.Random.value < droneVisibleChance;

                    for (int d = 0; d < droneCount; d++)
                    {
                        var entry = validPrefabs[UnityEngine.Random.Range(0, validPrefabs.Count)];
                        var instance = (GameObject)PrefabUtility.InstantiatePrefab(entry.prefab);
                        instance.hideFlags = HideFlags.DontSave;

                        bool placeInView = (guaranteeVisible && d == 0) || UnityEngine.Random.value < 0.7f;
                        PositionDrone(instance.transform, placeInView);

                        frameDrones.Add(new SpawnedDrone { GameObject = instance, ClassId = entry.classId });
                    }

                    targetCamera.Render();
                    RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, ImageWidth, ImageHeight), 0, 0);
                    tex.Apply(false);
                    RenderTexture.active = null;

                    int index = startIndex + i;
                    string baseName = index.ToString("D4");
                    File.WriteAllBytes(Path.Combine(outputFolder, baseName + ".jpg"), tex.EncodeToJPG(jpegQuality));
                    WriteLabelFile(Path.Combine(outputFolder, baseName + ".txt"), frameDrones);

                    foreach (var drone in frameDrones) DestroyImmediate(drone.GameObject);
                    frameDrones.Clear();
                }
            }
            finally
            {
                foreach (var drone in frameDrones)
                {
                    if (drone.GameObject != null) DestroyImmediate(drone.GameObject);
                }

                targetCamera.targetTexture = originalTargetTexture;
                targetCamera.transform.SetPositionAndRotation(originalPosition, originalRotation);
                RenderSettings.skybox = originalSkybox;

                RenderTexture.active = null;
                rt.Release();
                DestroyImmediate(rt);
                DestroyImmediate(tex);

                EditorUtility.ClearProgressBar();
            }
        }

        private void PositionDrone(Transform t, bool placeInView)
        {
            Transform camT = targetCamera.transform;

            if (placeInView)
            {
                float distance = UnityEngine.Random.Range(minDistance, maxDistance);
                float halfHeight = distance * Mathf.Tan(targetCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
                float halfWidth = halfHeight * targetCamera.aspect;

                // Keep the pivot inset from the true frustum edge so the drone's own
                // extent doesn't immediately push it out of frame.
                const float inset = 0.75f;
                float offsetX = UnityEngine.Random.Range(-halfWidth, halfWidth) * inset;
                float offsetY = UnityEngine.Random.Range(-halfHeight, halfHeight) * inset;

                t.position = camT.position + camT.forward * distance + camT.right * offsetX + camT.up * offsetY;
            }
            else
            {
                float distance = UnityEngine.Random.Range(minDistance, maxDistance * 1.5f);
                t.position = camT.position + UnityEngine.Random.onUnitSphere * distance;
            }

            t.rotation = Quaternion.Euler(
                UnityEngine.Random.Range(-15f, 15f),
                UnityEngine.Random.Range(0f, 360f),
                UnityEngine.Random.Range(-15f, 15f));
        }

        private void WriteLabelFile(string path, List<SpawnedDrone> frameDrones)
        {
            var lines = new List<string>();
            foreach (var drone in frameDrones)
            {
                if (TryComputeNormalizedBox(drone.GameObject, targetCamera, out Rect box))
                {
                    float cx = box.x + box.width / 2f;
                    float cy = box.y + box.height / 2f;
                    lines.Add(string.Join(" ", new[]
                    {
                        drone.ClassId.ToString(CultureInfo.InvariantCulture),
                        cx.ToString("F6", CultureInfo.InvariantCulture),
                        cy.ToString("F6", CultureInfo.InvariantCulture),
                        box.width.ToString("F6", CultureInfo.InvariantCulture),
                        box.height.ToString("F6", CultureInfo.InvariantCulture),
                    }));
                }
            }
            File.WriteAllLines(path, lines);
        }

        // Projects the combined renderer bounds of a drone into viewport space and
        // returns its axis-aligned box in image-normalized coordinates (0,0 = top
        // left), clipped to the frame. Returns false if the drone isn't visible
        // (behind the camera, or clipped down to a sliver).
        private static bool TryComputeNormalizedBox(GameObject go, Camera cam, out Rect box)
        {
            box = default;

            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return false;

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

            Vector3 c = bounds.center, e = bounds.extents;
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            bool any = false;

            for (int sx = -1; sx <= 1; sx += 2)
            {
                for (int sy = -1; sy <= 1; sy += 2)
                {
                    for (int sz = -1; sz <= 1; sz += 2)
                    {
                        Vector3 corner = c + new Vector3(sx * e.x, sy * e.y, sz * e.z);
                        Vector3 vp = cam.WorldToViewportPoint(corner);
                        if (vp.z <= 0f) continue; // behind the camera

                        any = true;
                        minX = Mathf.Min(minX, vp.x);
                        maxX = Mathf.Max(maxX, vp.x);

                        float imgY = 1f - vp.y; // viewport 0 = bottom, image 0 = top
                        minY = Mathf.Min(minY, imgY);
                        maxY = Mathf.Max(maxY, imgY);
                    }
                }
            }

            if (!any) return false;

            minX = Mathf.Clamp01(minX); maxX = Mathf.Clamp01(maxX);
            minY = Mathf.Clamp01(minY); maxY = Mathf.Clamp01(maxY);

            float w = maxX - minX;
            float h = maxY - minY;
            if (w <= 0f || h <= 0f || w * h < MinNormalizedBoxArea) return false;

            box = new Rect(minX, minY, w, h);
            return true;
        }

        private static int FindNextIndex(string folder)
        {
            int max = 0;
            foreach (var file in Directory.GetFiles(folder, "*.jpg"))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                if (int.TryParse(name, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n > max) max = n;
            }
            return max + 1;
        }

        private static string DefaultOutputFolder()
        {
            // Assets/ -> project root (".../SHIELD Virtual Camera") -> "Test Tools" ->
            // sibling "SHIELD Virtual Camera Data Collector/dataset_txt". Kept outside
            // Assets so Unity never tries to import thousands of jpg/txt files.
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string testToolsRoot = Directory.GetParent(projectRoot).FullName;
            return Path.Combine(testToolsRoot, "SHIELD Virtual Camera Data Collector", "dataset_txt");
        }

        private void AutoFindDronePrefabs()
        {
            dronePrefabs.Clear();
            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs" });
            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!Path.GetFileNameWithoutExtension(path).StartsWith("_Drone", StringComparison.OrdinalIgnoreCase)) continue;

                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go != null) dronePrefabs.Add(new DronePrefabEntry { prefab = go, classId = 0 });
            }
        }

        private void AutoFindSkyboxes()
        {
            string[] guids = AssetDatabase.FindAssets("t:Material SkyLightBox");
            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null) continue;

                if (path.IndexOf("Day", StringComparison.OrdinalIgnoreCase) >= 0) daySkybox = mat;
                else if (path.IndexOf("Dusk", StringComparison.OrdinalIgnoreCase) >= 0
                         || path.IndexOf("Night", StringComparison.OrdinalIgnoreCase) >= 0) nightSkybox = mat;
            }
        }
    }
}

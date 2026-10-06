using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

// Listens for a spawn command from the external STE control system and
// instantiates one of the three drone prefabs at the requested world
// coordinates, optionally flying it on to a second position.
//
// Line-based text protocol: each connection sends one of
//   "SPAWN <Quad|Toad|BumbleBee> <x> <y> <z>\n"
//       - spawns a drone that hovers in place
//   "SPAWN <Quad|Toad|BumbleBee> <x> <y> <z> <endX> <endY> <endZ> <speed>\n"
//       - spawns a drone facing the end position, which DroneAutopilot then
//         flies it to at <speed> m/s and hovers there
//   "DESPAWN ALL\n"
// See
// Common_Test_Functions.vb (InstDrone/DespawnAllDrones) in the STE test
// library for the matching VB.NET client.
//
// Attach this to a GameObject in the scene and assign the three prefab
// fields in the Inspector.
public class DroneSpawner : MonoBehaviour
{
    [Header("Network")]
    public int port = 5557;

    [Header("Drone prefabs")]
    public GameObject quadPrefab;
    public GameObject toadPrefab;
    public GameObject bumbleBeePrefab;

    private TcpListener listener;
    private Thread serverThread;
    private volatile bool running;

    private readonly object queueLock = new object();
    private readonly System.Collections.Generic.Queue<string> pendingCommands = new System.Collections.Generic.Queue<string>();

    // Every drone this spawner has instantiated and not yet destroyed, so
    // "DESPAWN ALL" can clean them up - Common_Test_Functions.vb's
    // DespawnAllDrones() sends it. Not called automatically by
    // TestCaseEnd(), which instead relaunches the whole Unity player
    // between test cases; a test case that spawns drones and wants them
    // gone before the case ends must call DespawnAllDrones() itself.
    private readonly System.Collections.Generic.List<GameObject> spawnedDrones = new System.Collections.Generic.List<GameObject>();

    private void Start()
    {
        Application.runInBackground = true;

        running = true;
        serverThread = new Thread(ServerLoop) { IsBackground = true };
        serverThread.Start();
    }

    private void Update()
    {
        while (true)
        {
            string command;
            lock (queueLock)
            {
                if (pendingCommands.Count == 0)
                    break;
                command = pendingCommands.Dequeue();
            }
            ApplyCommand(command);
        }
    }

    private void ApplyCommand(string command)
    {
        if (command == "DESPAWN ALL")
        {
            DespawnAll();
            return;
        }

        string[] parts = command.Split(' ');
        if ((parts.Length != 5 && parts.Length != 9) || parts[0] != "SPAWN")
        {
            Debug.LogError($"[DroneSpawner] Unrecognized command '{command}' (expected 'SPAWN <Quad|Toad|BumbleBee> <x> <y> <z> [<endX> <endY> <endZ> <speed>]' or 'DESPAWN ALL').");
            return;
        }

        GameObject prefab = PrefabForType(parts[1]);
        if (prefab == null)
        {
            Debug.LogError($"[DroneSpawner] Unrecognized drone type '{parts[1]}' (expected Quad, Toad, or BumbleBee).");
            return;
        }

        if (!TryParseVector(parts, 2, out Vector3 position))
        {
            Debug.LogError($"[DroneSpawner] Could not parse coordinates from command '{command}'.");
            return;
        }

        if (parts.Length == 5)
        {
            spawnedDrones.Add(InstantiateDrone(prefab, position));
            return;
        }

        if (!TryParseVector(parts, 5, out Vector3 endPosition) || !TryParseFloat(parts[8], out float speed))
        {
            Debug.LogError($"[DroneSpawner] Could not parse end coordinates/speed from command '{command}'.");
            return;
        }
        if (speed <= 0f)
        {
            Debug.LogError($"[DroneSpawner] Speed must be positive in command '{command}'.");
            return;
        }

        spawnedDrones.Add(InstantiateDrone(prefab, position, endPosition, speed));
    }

    // Spawns a drone that hovers at startPosition.
    private GameObject InstantiateDrone(GameObject prefab, Vector3 startPosition)
    {
        return Instantiate(prefab, startPosition, Quaternion.identity);
    }

    // Spawns a drone at startPosition and has DroneAutopilot fly it to
    // endPosition at speed (m/s), where it then hovers. It spawns already
    // facing endPosition so it sets off nose-first instead of yawing round
    // on the spot.
    private GameObject InstantiateDrone(GameObject prefab, Vector3 startPosition, Vector3 endPosition, float speed)
    {
        Vector3 flatHeading = new Vector3(endPosition.x - startPosition.x, 0f, endPosition.z - startPosition.z);
        Quaternion rotation = flatHeading.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(flatHeading) : Quaternion.identity;

        GameObject drone = Instantiate(prefab, startPosition, rotation);
        DroneAutopilot autopilot = drone.AddComponent<DroneAutopilot>();
        autopilot.targetPosition = endPosition;
        autopilot.speed = speed;
        return drone;
    }

    private void DespawnAll()
    {
        foreach (GameObject drone in spawnedDrones)
        {
            if (drone != null)
                Destroy(drone);
        }
        spawnedDrones.Clear();
    }

    private GameObject PrefabForType(string droneType)
    {
        switch (droneType)
        {
            case "Quad": return quadPrefab;
            case "Toad": return toadPrefab;
            case "BumbleBee": return bumbleBeePrefab;
            default: return null;
        }
    }

    // Parses parts[start], parts[start + 1], parts[start + 2] as x/y/z.
    private static bool TryParseVector(string[] parts, int start, out Vector3 vector)
    {
        vector = Vector3.zero;
        if (!TryParseFloat(parts[start], out float x)
            || !TryParseFloat(parts[start + 1], out float y)
            || !TryParseFloat(parts[start + 2], out float z))
        {
            return false;
        }

        vector = new Vector3(x, y, z);
        return true;
    }

    private static bool TryParseFloat(string text, out float value)
    {
        return float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value);
    }

    private void ServerLoop()
    {
        try
        {
            listener = new TcpListener(IPAddress.Loopback, port);
            try
            {
                listener.Start();
            }
            catch (SocketException ex)
            {
                Debug.LogError(
                    $"[DroneSpawner] Could not bind port {port}: {ex.Message}. "
                    + "Is another Play session (or another app) already using this port? "
                    + "Stop it, or change the 'port' field, then re-enter Play Mode."
                );
                return;
            }
            Debug.Log($"[DroneSpawner] Listening on 127.0.0.1:{port}");

            while (running)
            {
                if (!listener.Pending())
                {
                    Thread.Sleep(100);
                    continue;
                }

                using (TcpClient client = listener.AcceptTcpClient())
                using (NetworkStream stream = client.GetStream())
                {
                    ReadCommands(stream);
                }
            }
        }
        catch (SocketException)
        {
            // Expected during shutdown (listener.Stop() unblocks AcceptTcpClient with an exception).
        }
        catch (ThreadAbortException)
        {
            // Expected during shutdown.
        }
    }

    private void ReadCommands(NetworkStream stream)
    {
        var reader = new System.IO.StreamReader(stream, System.Text.Encoding.ASCII);
        while (running)
        {
            string line;
            try
            {
                line = reader.ReadLine();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DroneSpawner] Client stream ended: {ex.Message}");
                return;
            }

            if (line == null)
                return; // client closed the connection

            lock (queueLock)
            {
                pendingCommands.Enqueue(line.Trim());
            }
        }
    }

    private void Shutdown()
    {
        running = false;
        try { listener?.Stop(); } catch { }
    }

    private void OnDestroy()
    {
        Shutdown();
    }

    private void OnApplicationQuit()
    {
        Shutdown();
    }
}

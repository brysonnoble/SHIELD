using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

// Listens for a spawn command from the external STE control system and
// instantiates one of the three drone prefabs at the requested world
// coordinates.
//
// Line-based text protocol: each connection sends
// "SPAWN <Quad|Toad|BumbleBee> <x> <y> <z>\n". See Common_Test_Functions.vb
// (InstDrone) in the STE test library for the matching VB.NET client.
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
        string[] parts = command.Split(' ');
        if (parts.Length != 5 || parts[0] != "SPAWN")
        {
            Debug.LogError($"[DroneSpawner] Unrecognized command '{command}' (expected 'SPAWN <Quad|Toad|BumbleBee> <x> <y> <z>').");
            return;
        }

        GameObject prefab = PrefabForType(parts[1]);
        if (prefab == null)
        {
            Debug.LogError($"[DroneSpawner] Unrecognized drone type '{parts[1]}' (expected Quad, Toad, or BumbleBee).");
            return;
        }

        if (!TryParseCoordinates(parts, out Vector3 position))
        {
            Debug.LogError($"[DroneSpawner] Could not parse coordinates from command '{command}'.");
            return;
        }

        Instantiate(prefab, position, Quaternion.identity);
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

    private static bool TryParseCoordinates(string[] parts, out Vector3 position)
    {
        position = Vector3.zero;
        if (!float.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float x)
            || !float.TryParse(parts[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float y)
            || !float.TryParse(parts[4], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float z))
        {
            return false;
        }

        position = new Vector3(x, y, z);
        return true;
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

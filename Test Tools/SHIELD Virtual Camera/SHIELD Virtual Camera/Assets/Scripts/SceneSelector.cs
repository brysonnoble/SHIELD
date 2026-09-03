using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

// Listens for a day/night command from the external STE control system and
// swaps the scene's skybox in place. Used to switch scenes entirely; now
// that Day/Night are just two skybox materials in one scene, swapping
// RenderSettings.skybox avoids the cost (and the loss of live sim state)
// of a full scene reload.
//
// Line-based text protocol: each connection sends "ENV DAY\n" or
// "ENV NIGHT\n". See Common_Test_Functions.vb (EditVirtualEnvironment) in
// the STE test library for the matching VB.NET client.
public class SceneSelector : MonoBehaviour
{
    [Header("Network")]
    public int port = 5556;

    [Header("Skyboxes")]
    [Tooltip("Applied on an \"ENV DAY\" command.")]
    public Material daySkybox;
    [Tooltip("Applied on an \"ENV NIGHT\" command.")]
    public Material nightSkybox;

    private TcpListener listener;
    private Thread serverThread;
    private volatile bool running;

    private readonly object commandLock = new object();
    private string pendingCommand;

    private void Start()
    {
        Application.runInBackground = true;

        running = true;
        serverThread = new Thread(ServerLoop) { IsBackground = true };
        serverThread.Start();
    }

    private void Update()
    {
        string command = null;
        lock (commandLock)
        {
            if (pendingCommand != null)
            {
                command = pendingCommand;
                pendingCommand = null;
            }
        }

        if (command != null)
            ApplyCommand(command);
    }

    private void ApplyCommand(string command)
    {
        string[] parts = command.Split(' ');
        if (parts.Length != 2 || parts[0] != "ENV")
        {
            Debug.LogError($"[SceneSelector] Unrecognized command '{command}' (expected 'ENV DAY' or 'ENV NIGHT').");
            return;
        }

        switch (parts[1])
        {
            case "DAY":
                ApplySkybox(daySkybox);
                break;
            case "NIGHT":
                ApplySkybox(nightSkybox);
                break;
            default:
                Debug.LogError($"[SceneSelector] Unrecognized environment '{parts[1]}' (expected DAY or NIGHT).");
                break;
        }
    }

    private void ApplySkybox(Material skybox)
    {
        RenderSettings.skybox = skybox;
        DynamicGI.UpdateEnvironment();
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
                    $"[SceneSelector] Could not bind port {port}: {ex.Message}. "
                    + "Is another Play session (or another app) already using this port? "
                    + "Stop it, or change the 'port' field, then re-enter Play Mode."
                );
                return;
            }
            Debug.Log($"[SceneSelector] Listening on 127.0.0.1:{port}");

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
                Debug.LogWarning($"[SceneSelector] Client stream ended: {ex.Message}");
                return;
            }

            if (line == null)
                return; // client closed the connection

            lock (commandLock)
            {
                pendingCommand = line.Trim();
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

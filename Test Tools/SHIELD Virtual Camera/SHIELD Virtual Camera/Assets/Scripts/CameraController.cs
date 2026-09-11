using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

// Listens for two commands from the external STE control system: a
// camera-reset that moves this GameObject back to the world origin, and a
// graceful quit. Line-based text protocol: each connection sends either
// "CAM RESET\n" or "QUIT\n". See Common_Test_Functions.vb
// (CloseUnityPlayer) in the STE test library for the matching VB.NET
// client - it sends QUIT before ever falling back to an OS-level
// close/kill, since Application.Quit() runs Unity's own shutdown path
// (releasing its Direct3D/OpenGL device cleanly) instead of forcibly
// killing a process that still has a live graphics device open, which has
// been observed to crash the GPU driver (BSOD) rather than just closing
// the window.
//
// Attach this to the scene's Main Camera, alongside CameraStreamer.
public class CameraController : MonoBehaviour
{
    [Header("Network")]
    public int port = 5558;

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
        switch (command)
        {
            case "CAM RESET":
                transform.position = Vector3.zero;
                break;
            case "QUIT":
                // Runs Unity's normal shutdown path (OnApplicationQuit
                // callbacks, releasing the graphics device, etc.) instead
                // of leaving the caller to close/kill the process from the
                // outside.
                Application.Quit();
                break;
            default:
                Debug.LogError($"[CameraController] Unrecognized command '{command}' (expected 'CAM RESET' or 'QUIT').");
                break;
        }
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
                    $"[CameraController] Could not bind port {port}: {ex.Message}. "
                    + "Is another Play session (or another app) already using this port? "
                    + "Stop it, or change the 'port' field, then re-enter Play Mode."
                );
                return;
            }
            Debug.Log($"[CameraController] Listening on 127.0.0.1:{port}");

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
                Debug.LogWarning($"[CameraController] Client stream ended: {ex.Message}");
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

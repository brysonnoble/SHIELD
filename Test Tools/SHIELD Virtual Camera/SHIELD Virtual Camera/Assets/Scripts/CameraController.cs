using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

// Listens for a camera-reset command from the external STE control system
// and moves this GameObject back to the world origin. Used by the STE test
// library's TestCaseBegin()/TestCaseEnd() (Common_Test_Functions.vb) to put
// the camera in a known position at the start and end of every test case.
//
// Line-based text protocol: each connection sends "CAM RESET\n". See
// Common_Test_Functions.vb (TestCaseBegin/TestCaseEnd) in the STE test
// library for the matching VB.NET client.
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
        if (command != "CAM RESET")
        {
            Debug.LogError($"[CameraController] Unrecognized command '{command}' (expected 'CAM RESET').");
            return;
        }

        transform.position = Vector3.zero;
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

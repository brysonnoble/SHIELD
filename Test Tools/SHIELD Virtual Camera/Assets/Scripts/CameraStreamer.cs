using System;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

// Attach to the scene's Main Camera. Streams the rendered frame to a
// local Python process over TCP so the SHIELD detect/track pipeline can
// run against the Unity virtual camera before the Raspberry Pi + Camera
// Module 3 hardware exists.
//
// Pull-based protocol: the client sends a single request byte, then
// this sends back a 4-byte big-endian length followed by that many JPEG
// bytes. Frames are only ever sent on request so a slow client (e.g.
// CPU-bound YOLO inference) can't cause stale frames to back up in the
// socket buffer. See SHIELD/SHIELD/SHIELD/video_source.py
// (UnityStreamSource) for the matching Python client.
public class CameraStreamer : MonoBehaviour
{
    [Header("Network")]
    public int port = 5555;

    [Header("Capture")]
    [Range(1, 30)] public int targetFps = 15;
    [Range(10, 100)] public int jpegQuality = 75;
    [Tooltip("Frames larger than this are downscaled before JPEG encoding (aspect ratio preserved).")]
    public int maxWidth = 1920;
    public int maxHeight = 1080;

    private Texture2D captureTexture;
    private Texture2D scaledTexture;
    private TcpListener listener;
    private TcpClient client;
    private Thread serverThread;
    private volatile bool running;

    private readonly object frameLock = new object();
    private byte[] latestFrame;
    private bool hasNewFrame;

    private void Start()
    {
        // Keep capturing/streaming even if the Editor or a built player
        // loses focus (e.g. while you alt-tab to run the Python side).
        Application.runInBackground = true;

        running = true;
        serverThread = new Thread(ServerLoop) { IsBackground = true };
        serverThread.Start();
        StartCoroutine(CaptureLoop());
    }

    private IEnumerator CaptureLoop()
    {
        var waitForEndOfFrame = new WaitForEndOfFrame();
        float nextCaptureTime = 0f;

        while (running)
        {
            yield return waitForEndOfFrame;

            if (Time.unscaledTime < nextCaptureTime)
                continue;

            nextCaptureTime = Time.unscaledTime + 1f / Mathf.Max(1, targetFps);
            CaptureFrame();
        }
    }

    private void CaptureFrame()
    {
        int width = Screen.width;
        int height = Screen.height;
        if (width <= 0 || height <= 0)
            return;

        if (captureTexture == null || captureTexture.width != width || captureTexture.height != height)
        {
            if (captureTexture != null)
                Destroy(captureTexture);
            captureTexture = new Texture2D(width, height, TextureFormat.RGB24, false);
        }

        try
        {
            captureTexture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            captureTexture.Apply(false);

            Texture2D toEncode = captureTexture;
            float scale = Mathf.Min(1f, (float)maxWidth / width, (float)maxHeight / height);
            if (scale < 1f)
            {
                int scaledWidth = Mathf.Max(1, Mathf.RoundToInt(width * scale));
                int scaledHeight = Mathf.Max(1, Mathf.RoundToInt(height * scale));
                toEncode = Downscale(captureTexture, scaledWidth, scaledHeight);
            }

            byte[] jpg = toEncode.EncodeToJPG(jpegQuality);

            lock (frameLock)
            {
                latestFrame = jpg;
                hasNewFrame = true;
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[CameraStreamer] Frame capture failed: {ex.Message}");
        }
    }

    private Texture2D Downscale(Texture2D source, int width, int height)
    {
        if (scaledTexture == null || scaledTexture.width != width || scaledTexture.height != height)
        {
            if (scaledTexture != null)
                Destroy(scaledTexture);
            scaledTexture = new Texture2D(width, height, TextureFormat.RGB24, false);
        }

        RenderTexture rt = RenderTexture.GetTemporary(width, height);
        RenderTexture prevActive = RenderTexture.active;
        Graphics.Blit(source, rt);
        RenderTexture.active = rt;
        scaledTexture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        scaledTexture.Apply(false);
        RenderTexture.active = prevActive;
        RenderTexture.ReleaseTemporary(rt);

        return scaledTexture;
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
                    $"[CameraStreamer] Could not bind port {port}: {ex.Message}. "
                    + "Is another Play session (or another app) already using this port? "
                    + "Stop it, or change the 'port' field, then re-enter Play Mode."
                );
                return;
            }
            Debug.Log($"[CameraStreamer] Listening on 127.0.0.1:{port}");

            while (running)
            {
                if (!listener.Pending())
                {
                    Thread.Sleep(100);
                    continue;
                }

                using (client = listener.AcceptTcpClient())
                using (NetworkStream stream = client.GetStream())
                {
                    client.NoDelay = true; // disable Nagle: frames are small and latency-sensitive
                    Debug.Log("[CameraStreamer] Python client connected.");
                    StreamToClient(stream);
                }
                Debug.Log("[CameraStreamer] Python client disconnected.");
            }
        }
        catch (SocketException ex)
        {
            Debug.LogError($"[CameraStreamer] Socket error: {ex.Message}");
        }
        catch (ThreadAbortException)
        {
            // Expected during shutdown.
        }
    }

    private void StreamToClient(NetworkStream stream)
    {
        // Deliberately not checking TcpClient.Connected here: on Unity's
        // Mono runtime it isn't reliable as a loop condition (it can read
        // false immediately after AcceptTcpClient() returns, which would
        // exit this loop - and the enclosing using blocks - before ever
        // writing a byte, silently resetting every incoming connection).
        // A dead connection is instead detected by stream.Read()/Write()
        // throwing or returning 0, which is the reliable signal.
        var requestByte = new byte[1];

        while (running)
        {
            int bytesRead;
            try
            {
                bytesRead = stream.Read(requestByte, 0, 1);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[CameraStreamer] Client stream ended: {ex.Message}");
                return;
            }

            if (bytesRead <= 0)
                return; // client closed the connection

            byte[] frame = WaitForNextFrame();
            if (frame == null)
                return; // shutting down

            try
            {
                byte[] packet = new byte[4 + frame.Length];
                byte[] lengthPrefix = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(frame.Length));
                Buffer.BlockCopy(lengthPrefix, 0, packet, 0, 4);
                Buffer.BlockCopy(frame, 0, packet, 4, frame.Length);
                // Single write for header+payload: two separate small
                // writes would each eat a TCP segment, and combined with
                // Nagle's algorithm/delayed ACK that can add tens to
                // hundreds of ms per frame even with NoDelay set.
                stream.Write(packet, 0, packet.Length);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[CameraStreamer] Client stream ended: {ex.Message}");
                return;
            }
        }
    }

    // Blocks until a frame captured after the last one sent is available.
    // Only ever returns a genuinely new frame (never resends a stale one),
    // so a client that's fallen behind gets caught up to the latest frame
    // instead of working through a backlog.
    private byte[] WaitForNextFrame()
    {
        while (running)
        {
            lock (frameLock)
            {
                if (hasNewFrame)
                {
                    hasNewFrame = false;
                    return latestFrame;
                }
            }
            Thread.Sleep(2);
        }
        return null;
    }

    private void Shutdown()
    {
        running = false;
        try { client?.Close(); } catch { }
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

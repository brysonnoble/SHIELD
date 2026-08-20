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
// Wire format per frame: 4-byte big-endian length, then that many JPEG
// bytes. See SHIELD/SHIELD/SHIELD/video_source.py (UnityStreamSource)
// for the matching Python client.
public class CameraStreamer : MonoBehaviour
{
    [Header("Network")]
    public int port = 5555;

    [Header("Capture")]
    [Range(1, 30)] public int targetFps = 15;
    [Range(10, 100)] public int jpegQuality = 75;

    private Texture2D captureTexture;
    private TcpListener listener;
    private TcpClient client;
    private Thread serverThread;
    private volatile bool running;

    private readonly object frameLock = new object();
    private byte[] latestFrame;
    private bool hasNewFrame;

    private void Start()
    {
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

        captureTexture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        captureTexture.Apply(false);

        byte[] jpg = captureTexture.EncodeToJPG(jpegQuality);

        lock (frameLock)
        {
            latestFrame = jpg;
            hasNewFrame = true;
        }
    }

    private void ServerLoop()
    {
        try
        {
            listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
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
        while (running && client.Connected)
        {
            byte[] frame = null;
            lock (frameLock)
            {
                if (hasNewFrame)
                {
                    frame = latestFrame;
                    hasNewFrame = false;
                }
            }

            if (frame == null)
            {
                Thread.Sleep(5);
                continue;
            }

            try
            {
                byte[] lengthPrefix = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(frame.Length));
                stream.Write(lengthPrefix, 0, lengthPrefix.Length);
                stream.Write(frame, 0, frame.Length);
            }
            catch (Exception)
            {
                return; // client disconnected
            }
        }
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

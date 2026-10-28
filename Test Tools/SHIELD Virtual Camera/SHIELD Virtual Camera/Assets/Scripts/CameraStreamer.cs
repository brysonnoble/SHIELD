using System;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

// Attach to the scene's Main Camera. Streams what a stereo pair of
// e-CAM25_CUONX (AR0234) cameras would see to a local Python process over
// TCP, so the SHIELD detect/track/range pipeline can run against the Unity
// virtual camera before the Jetson Orin Nano + camera hardware exists.
//
// The Main Camera is the left (reference) camera: the one shown on screen,
// and the one test placement and every reported position is relative to.
// A right camera is added as a child, baselineMeters to its right with the
// same orientation and lens, so the two images are a rectified pair (rows
// aligned, as a real pair is after stereo calibration). Both render at the
// sensor's resolution and vertical field of view, independent of the
// player window's size.
//
// Pull-based protocol: the client sends a single request byte, then this
// sends back a 4-byte big-endian length followed by that many payload
// bytes. Request 0x02 gets a stereo payload: "SHST", float32 baseline
// (m), float32 focal length (px), then the left and right JPEGs, each
// prefixed with its uint32 length (all big-endian). Any other request
// byte (0x01) gets the left JPEG alone. Frames are only ever sent on
// request so a slow client (e.g. CPU-bound YOLO inference) can't cause
// stale frames to back up in the socket buffer. See
// SHIELD/SHIELD/video_source.py (UnityStreamSource) for the matching
// Python client.
[RequireComponent(typeof(Camera))]
public class CameraStreamer : MonoBehaviour
{
    private const byte StereoRequest = 0x02;
    private static readonly byte[] StereoMagic = { (byte)'S', (byte)'H', (byte)'S', (byte)'T' };

    [Header("Network")]
    public int port = 5555;

    [Header("Capture")]
    [Range(1, 30)] public int targetFps = 30;
    [Range(10, 100)] public int jpegQuality = 75;

    [Header("Sensor (e-CAM25_CUONX, onsemi AR0234)")]
    [Tooltip("AR0234 active array, 1920x1200 at 3 um pixels.")]
    public int sensorWidth = 1920;
    public int sensorHeight = 1200;
    [Tooltip("e-con's stock lens: 61.6 deg vertical. Applied to both cameras as an ideal (undistorted) pinhole - "
             + "the real lens's barrel distortion is calibrated out on hardware before matching.")]
    public float verticalFovDegrees = 61.6f;

    [Header("Stereo")]
    public bool stereo = true;
    [Tooltip("Distance between the two camera centers. Range error grows as range^2 / (focal length * baseline), "
             + "so wider ranges better; 20 cm gives ~2-3% at 20 m.")]
    public float baselineMeters = 0.20f;

    private Camera leftCamera;
    private Camera rightCamera;
    private RenderTexture leftTarget;
    private RenderTexture rightTarget;
    private Texture2D leftReadback;
    private Texture2D rightReadback;

    private TcpListener listener;
    private TcpClient client;
    private Thread serverThread;
    private Thread encoderThread;
    private volatile bool running;
    private volatile bool clientConnected;

    // One capture, as raw pixels, handed from the main thread to the
    // encoder thread so JPEG encoding never stalls rendering. Pooled -
    // each is ~7 MB per eye, too much to allocate 30 times a second.
    private class Capture
    {
        public byte[] left;
        public byte[] right;
        public bool hasRight; // false when stereo is off
        public GraphicsFormat format;
        public int width;
        public int height;
        public float baselineMeters;
        public float focalPixels;
    }

    // One capture, encoded and ready to send.
    private class EncodedFrame
    {
        public byte[] left;
        public byte[] right; // null when stereo is off
        public float baselineMeters;
        public float focalPixels;
    }

    private readonly object captureLock = new object();
    private readonly System.Collections.Generic.Stack<Capture> freeCaptures =
        new System.Collections.Generic.Stack<Capture>();
    private Capture latestCapture; // newest capture not yet taken by the encoder
    private readonly AutoResetEvent captureReady = new AutoResetEvent(false);

    private readonly object frameLock = new object();
    private EncodedFrame latestFrame;
    private bool hasNewFrame;

    private void Awake()
    {
        leftCamera = GetComponent<Camera>();
        leftCamera.fieldOfView = verticalFovDegrees;

        var rightEye = new GameObject("Right Camera (stereo)");
        rightCamera = rightEye.AddComponent<Camera>();
        // CopyFrom also copies the transform, so parent and offset after it.
        rightCamera.CopyFrom(leftCamera);
        rightEye.transform.SetParent(transform, false);
        rightEye.transform.localPosition = new Vector3(baselineMeters, 0f, 0f);
        rightEye.transform.localRotation = Quaternion.identity;
        rightEye.transform.localScale = Vector3.one;
        rightCamera.enabled = false; // rendered on demand into its own texture, never to the screen
    }

    private void Start()
    {
        // Keep capturing/streaming even if the Editor or a built player
        // loses focus (e.g. while you alt-tab to run the Python side).
        Application.runInBackground = true;

        leftTarget = NewEyeTarget();
        rightTarget = NewEyeTarget();
        leftReadback = new Texture2D(sensorWidth, sensorHeight, TextureFormat.RGB24, false);
        rightReadback = new Texture2D(sensorWidth, sensorHeight, TextureFormat.RGB24, false);

        running = true;
        serverThread = new Thread(ServerLoop) { IsBackground = true };
        serverThread.Start();
        encoderThread = new Thread(EncodeLoop) { IsBackground = true };
        encoderThread.Start();
        StartCoroutine(CaptureLoop());
    }

    private RenderTexture NewEyeTarget()
    {
        // Same antialiasing as the on-screen view, which the stream used
        // to be captured from.
        return new RenderTexture(sensorWidth, sensorHeight, 24)
        {
            antiAliasing = Mathf.Max(1, QualitySettings.antiAliasing),
        };
    }

    private IEnumerator CaptureLoop()
    {
        var waitForEndOfFrame = new WaitForEndOfFrame();
        float nextCaptureTime = 0f;

        while (running)
        {
            yield return waitForEndOfFrame;

            // Two extra full-resolution renders per capture - skip them
            // while nothing is listening.
            if (!clientConnected || Time.unscaledTime < nextCaptureTime)
                continue;

            nextCaptureTime = Time.unscaledTime + 1f / Mathf.Max(1, targetFps);
            CaptureFrame();
        }
    }

    private void CaptureFrame()
    {
        try
        {
            // Keep the right lens matched if the left one is changed at runtime.
            rightCamera.fieldOfView = leftCamera.fieldOfView;
            rightCamera.nearClipPlane = leftCamera.nearClipPlane;
            rightCamera.farClipPlane = leftCamera.farClipPlane;
            rightCamera.transform.localPosition = new Vector3(baselineMeters, 0f, 0f);

            Capture capture;
            lock (captureLock)
            {
                capture = freeCaptures.Count > 0 ? freeCaptures.Pop() : new Capture();
            }

            RenderInto(leftCamera, leftTarget, leftReadback, ref capture.left);
            capture.hasRight = stereo;
            if (stereo)
                RenderInto(rightCamera, rightTarget, rightReadback, ref capture.right);
            capture.format = leftReadback.graphicsFormat;
            capture.width = sensorWidth;
            capture.height = sensorHeight;
            capture.baselineMeters = baselineMeters;
            capture.focalPixels = sensorHeight * 0.5f / Mathf.Tan(leftCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);

            lock (captureLock)
            {
                // A newer capture replaces one the encoder hasn't got to.
                if (latestCapture != null)
                    freeCaptures.Push(latestCapture);
                latestCapture = capture;
            }
            captureReady.Set();
        }
        catch (Exception ex)
        {
            Debug.LogError($"[CameraStreamer] Frame capture failed: {ex.Message}");
        }
    }

    private static void RenderInto(Camera cam, RenderTexture target, Texture2D readback, ref byte[] pixels)
    {
        RenderTexture prevTarget = cam.targetTexture;
        RenderTexture prevActive = RenderTexture.active;
        try
        {
            cam.targetTexture = target;
            cam.Render();
            RenderTexture.active = target;
            readback.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            var data = readback.GetRawTextureData<byte>();
            if (pixels == null || pixels.Length != data.Length)
                pixels = new byte[data.Length];
            data.CopyTo(pixels);
        }
        finally
        {
            cam.targetTexture = prevTarget;
            RenderTexture.active = prevActive;
        }
    }

    // Encodes each capture as soon as it's taken, both eyes in parallel, so
    // a request is answered with an already-encoded frame instead of
    // waiting on two full-resolution JPEG encodes back to back.
    private void EncodeLoop()
    {
        while (running)
        {
            if (!captureReady.WaitOne(100))
                continue;

            Capture capture;
            lock (captureLock)
            {
                capture = latestCapture;
                latestCapture = null;
            }
            if (capture == null)
                continue;

            try
            {
                var frame = new EncodedFrame
                {
                    baselineMeters = capture.baselineMeters,
                    focalPixels = capture.focalPixels,
                };
                // ImageConversion.EncodeArrayToJPG is safe to call off the main thread.
                System.Threading.Tasks.Task<byte[]> rightTask = capture.hasRight
                    ? System.Threading.Tasks.Task.Run(() => EncodeJpeg(capture, capture.right))
                    : null;
                frame.left = EncodeJpeg(capture, capture.left);
                frame.right = rightTask?.Result;

                lock (frameLock)
                {
                    latestFrame = frame;
                    hasNewFrame = true;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CameraStreamer] JPEG encoding failed: {ex.Message}");
            }
            finally
            {
                lock (captureLock)
                {
                    freeCaptures.Push(capture);
                }
            }
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
                    clientConnected = true;
                    try
                    {
                        StreamToClient(stream);
                    }
                    finally
                    {
                        clientConnected = false;
                    }
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

            EncodedFrame frame = WaitForNextFrame();
            if (frame == null)
                return; // shutting down

            try
            {
                byte[] payload = requestByte[0] == StereoRequest && frame.right != null
                    ? StereoPayload(frame)
                    : frame.left;
                byte[] packet = new byte[4 + payload.Length];
                WriteInt(packet, 0, payload.Length);
                Buffer.BlockCopy(payload, 0, packet, 4, payload.Length);
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

    private static byte[] StereoPayload(EncodedFrame frame)
    {
        byte[] left = frame.left;
        byte[] right = frame.right;

        byte[] payload = new byte[4 + 4 + 4 + 4 + left.Length + 4 + right.Length];
        int offset = 0;
        Buffer.BlockCopy(StereoMagic, 0, payload, offset, 4);
        offset += 4;
        WriteInt(payload, offset, BitConverter.SingleToInt32Bits(frame.baselineMeters));
        offset += 4;
        WriteInt(payload, offset, BitConverter.SingleToInt32Bits(frame.focalPixels));
        offset += 4;
        WriteInt(payload, offset, left.Length);
        offset += 4;
        Buffer.BlockCopy(left, 0, payload, offset, left.Length);
        offset += left.Length;
        WriteInt(payload, offset, right.Length);
        offset += 4;
        Buffer.BlockCopy(right, 0, payload, offset, right.Length);
        return payload;
    }

    private byte[] EncodeJpeg(Capture capture, byte[] pixels)
    {
        return ImageConversion.EncodeArrayToJPG(
            pixels, capture.format, (uint)capture.width, (uint)capture.height, 0, jpegQuality);
    }

    private static void WriteInt(byte[] buffer, int offset, int value)
    {
        byte[] bigEndian = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(value));
        Buffer.BlockCopy(bigEndian, 0, buffer, offset, 4);
    }

    // Blocks until a frame captured after the last one sent is available.
    // Only ever returns a genuinely new frame (never resends a stale one),
    // so a client that's fallen behind gets caught up to the latest frame
    // instead of working through a backlog.
    private EncodedFrame WaitForNextFrame()
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
        if (leftTarget != null) leftTarget.Release();
        if (rightTarget != null) rightTarget.Release();
    }

    private void OnApplicationQuit()
    {
        Shutdown();
    }
}

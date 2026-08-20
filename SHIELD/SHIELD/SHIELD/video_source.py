# Video source abstraction so the detection pipeline doesn't care whether
# frames come from a real camera, a recorded clip, or the Unity virtual
# camera streaming over TCP.

import socket
import struct
import time

import cv2
import numpy as np


class VideoSource:
    def open(self):
        raise NotImplementedError

    def read(self):
        """Return the next BGR frame as a numpy array, or None when the
        source is exhausted/disconnected."""
        raise NotImplementedError

    def release(self):
        raise NotImplementedError


class WebcamSource(VideoSource):
    def __init__(self, index=0):
        self.index = index
        self._cap = None

    def open(self):
        self._cap = cv2.VideoCapture(self.index, cv2.CAP_DSHOW)
        if not self._cap.isOpened():
            raise RuntimeError(f"Could not open webcam index {self.index}")

    def read(self):
        ok, frame = self._cap.read()
        return frame if ok else None

    def release(self):
        if self._cap is not None:
            self._cap.release()


class VideoFileSource(VideoSource):
    def __init__(self, path, loop=True):
        self.path = path
        self.loop = loop
        self._cap = None

    def open(self):
        self._cap = cv2.VideoCapture(self.path)
        if not self._cap.isOpened():
            raise RuntimeError(f"Could not open video file {self.path}")

    def read(self):
        ok, frame = self._cap.read()
        if not ok:
            if self.loop:
                self._cap.set(cv2.CAP_PROP_POS_FRAMES, 0)
                ok, frame = self._cap.read()
                if not ok:
                    return None
            else:
                return None
        return frame

    def release(self):
        if self._cap is not None:
            self._cap.release()


def _recv_exact(sock, num_bytes):
    buf = bytearray()
    while len(buf) < num_bytes:
        chunk = sock.recv(num_bytes - len(buf))
        if not chunk:
            return None
        buf.extend(chunk)
    return bytes(buf)


class UnityStreamSource(VideoSource):
    """Connects to the CameraStreamer.cs TCP server running inside the
    Unity Editor (Play Mode) or a Unity player build, and decodes the
    length-prefixed JPEG frames it sends.

    Wire format per frame: 4-byte big-endian length, then that many JPEG
    bytes.
    """

    def __init__(self, host, port, connect_retries=15, retry_delay=1.0):
        self.host = host
        self.port = port
        self.connect_retries = connect_retries
        self.retry_delay = retry_delay
        self._sock = None

    def open(self):
        last_error = None
        for attempt in range(1, self.connect_retries + 1):
            try:
                self._sock = socket.create_connection((self.host, self.port), timeout=5.0)
                print(f"[UnityStreamSource] Connected to Unity at {self.host}:{self.port}")
                return
            except OSError as exc:
                last_error = exc
                print(
                    f"[UnityStreamSource] Waiting for Unity virtual camera "
                    f"({attempt}/{self.connect_retries})... is Play Mode running "
                    f"with CameraStreamer attached?"
                )
                time.sleep(self.retry_delay)
        raise ConnectionError(
            f"Could not connect to Unity virtual camera at {self.host}:{self.port}: {last_error}"
        )

    def read(self):
        if self._sock is None:
            return None
        header = _recv_exact(self._sock, 4)
        if header is None:
            return None
        (length,) = struct.unpack(">I", header)
        payload = _recv_exact(self._sock, length)
        if payload is None:
            return None
        frame = cv2.imdecode(np.frombuffer(payload, dtype=np.uint8), cv2.IMREAD_COLOR)
        return frame

    def release(self):
        if self._sock is not None:
            try:
                self._sock.close()
            except OSError:
                pass

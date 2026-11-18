# The drone <-> Ground Control System command link.
#
# AVS.05:    the drone shall receive engage and abort commands from the GCS.
# AVS.05.01: the drone shall send a heartbeat to the GCS at 1 Hz.
#
# DroneLinkServer is the drone's end: it runs in the detection pipeline
# (on the Orin, eventually), owns the engagement state, and is the only
# thing that decides whether a command is carried out. GcsLinkClient is the
# operator's end, used by the GCS window. In emulation both ends run on one
# PC over localhost TCP; on hardware the transport becomes the telemetry
# radio, but the messages stay the same.
#
# Wire format: one ASCII line per message, "\n"-terminated.
#
#   GCS -> drone   ENGAGE <track id>   start an engagement of that track
#                  ABORT               cancel the current engagement
#                  LAND                Land Now: abort and land immediately
#                  SHUTDOWN            Shut Down: abort and cut the motors
#
#   drone -> GCS   ACK <command>               carried out
#                  NAK <command> <reason>      refused, with why
#                  STATE state=<S> target=<id|->     on every state change
#                  HEARTBEAT seq=<n> state=<S> target=<id|-> frames=<n> video=<ok|lost>
#
# Replies go only to the connection that sent the command; STATE and
# HEARTBEAT go to every connected GCS. <S> is one of the STATE_* values
# below. Any number of GCS connections can be open at once (the GCS window
# and an STE test script, for example).

import queue
import socket
import threading
import time

STATE_TRACKING = "TRACKING"
STATE_ENGAGED = "ENGAGED"
STATE_LANDING = "LANDING"
STATE_SHUTDOWN = "SHUTDOWN"


class _TrackEntry:
    __slots__ = ("confidence", "frames_since_seen")

    def __init__(self, confidence):
        self.confidence = confidence
        self.frames_since_seen = 0


class _Client:
    def __init__(self, sock, address):
        self.sock = sock
        self.address = address
        self.send_lock = threading.Lock()
        self.alive = True

    def send_line(self, line):
        if not self.alive:
            return
        try:
            with self.send_lock:
                self.sock.sendall((line + "\n").encode("ascii"))
        except OSError:
            self.alive = False


class DroneLinkServer:
    """The drone's end of the command link.

    Threads: one accepts connections, one reads each connection, and one
    sends the heartbeat. Commands are only queued by those threads and are
    carried out on the pipeline's main thread by process_commands(), the
    same thread that calls update_tracks(), so a command is always checked
    against the tracks the pipeline has right now.
    """

    def __init__(self, host, port, heartbeat_hz, confidence_floor, track_max_age):
        self.host = host
        self.port = port
        self.heartbeat_period = 1.0 / heartbeat_hz
        self.confidence_floor = confidence_floor
        self.track_max_age = track_max_age

        self._listener = None
        self._clients = []
        self._clients_lock = threading.Lock()
        self._commands = queue.Queue()
        self._stop = threading.Event()
        self._threads = []

        # Read by the heartbeat thread, written by the main thread.
        self._state_lock = threading.Lock()
        self._state = STATE_TRACKING
        self._target_id = None
        self._frames = 0
        self._video_ok = False

        # Main thread only.
        self._tracks = {}

    # --- lifecycle -------------------------------------------------
    def start(self):
        listener = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        listener.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        listener.bind((self.host, self.port))
        listener.listen()
        # Lets the accept loop notice stop() instead of blocking forever.
        listener.settimeout(0.5)
        self._listener = listener
        print(f"[SHIELD] GCS link listening on {self.host}:{self.port}")
        self._spawn(self._accept_loop, "gcs-link-accept")
        self._spawn(self._heartbeat_loop, "gcs-link-heartbeat")

    def stop(self):
        self._stop.set()
        if self._listener is not None:
            try:
                self._listener.close()
            except OSError:
                pass
        with self._clients_lock:
            clients = list(self._clients)
            self._clients.clear()
        for client in clients:
            client.alive = False
            try:
                client.sock.close()
            except OSError:
                pass
        for thread in self._threads:
            thread.join(timeout=1.0)

    # --- main-thread API -------------------------------------------
    def update_tracks(self, detections):
        """Call once per processed frame with that frame's detections.
        Mirrors the GCS window's target list: a track stays engageable for
        up to track_max_age frames after it was last seen (GNC-06's
        30-frame line-of-sight limit, the same window the detector coasts
        its Kalman tracks for)."""
        for entry in self._tracks.values():
            entry.frames_since_seen += 1
        for det in detections:
            self._tracks[det.track_id] = _TrackEntry(det.confidence)
        stale = [tid for tid, e in self._tracks.items() if e.frames_since_seen > self.track_max_age]
        for tid in stale:
            del self._tracks[tid]
        with self._state_lock:
            self._frames += 1

    def set_video_ok(self, ok):
        with self._state_lock:
            self._video_ok = ok

    def process_commands(self):
        """Carry out every command received since the last call. Call from
        the pipeline's main loop, including while it waits on the video
        source, so a Land Now or Shut Down never waits on a frame."""
        while True:
            try:
                client, line = self._commands.get_nowait()
            except queue.Empty:
                return
            reply = self._handle(line)
            if reply is not None:
                client.send_line(reply)

    # --- command handling ------------------------------------------
    def _handle(self, line):
        parts = line.split()
        if not parts:
            return None
        verb = parts[0].upper()
        command = " ".join([verb] + parts[1:])
        with self._state_lock:
            state = self._state
            target = self._target_id

        if verb == "ENGAGE":
            if len(parts) != 2:
                return f"NAK {command} usage: ENGAGE <track id>"
            try:
                track_id = int(parts[1])
            except ValueError:
                return f"NAK {command} track id must be a number"
            if state == STATE_LANDING:
                return f"NAK {command} drone is landing"
            if state == STATE_SHUTDOWN:
                return f"NAK {command} motors are shut down"
            entry = self._tracks.get(track_id)
            if entry is None:
                return f"NAK {command} no such target"
            # AVS.03: never engage below the 25% confidence floor.
            if entry.confidence < self.confidence_floor:
                return (
                    f"NAK {command} confidence {entry.confidence * 100:.0f}% is below "
                    f"{self.confidence_floor * 100:.0f}% (AVS.03)"
                )
            self._set_state(STATE_ENGAGED, track_id)
            print(f"[SHIELD] ENGAGE accepted for track T-{track_id:02d} (no effector in emulation)")
            return f"ACK {command}"

        if verb == "ABORT":
            if state == STATE_ENGAGED:
                self._set_state(STATE_TRACKING, None)
                print(f"[SHIELD] ABORT - engagement of T-{target:02d} cancelled")
            else:
                print("[SHIELD] ABORT - nothing engaged")
            return f"ACK {command}"

        if verb == "LAND":
            if state == STATE_SHUTDOWN:
                return f"NAK {command} motors are shut down"
            self._set_state(STATE_LANDING, None)
            print("[SHIELD] LAND NOW - engagement cancelled, landing (no flight controller in emulation)")
            return f"ACK {command}"

        if verb == "SHUTDOWN":
            self._set_state(STATE_SHUTDOWN, None)
            print("[SHIELD] SHUT DOWN - engagement cancelled, motors cut (no flight controller in emulation)")
            return f"ACK {command}"

        return f"NAK {command} unknown command"

    def _set_state(self, state, target_id):
        with self._state_lock:
            changed = (state, target_id) != (self._state, self._target_id)
            self._state = state
            self._target_id = target_id
        if changed:
            self._broadcast(f"STATE state={state} target={_fmt_target(target_id)}")

    # --- threads ---------------------------------------------------
    def _spawn(self, target, name, *args):
        thread = threading.Thread(target=target, args=args, name=name, daemon=True)
        thread.start()
        self._threads.append(thread)

    def _accept_loop(self):
        while not self._stop.is_set():
            try:
                sock, address = self._listener.accept()
            except socket.timeout:
                continue
            except OSError:
                return
            sock.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
            client = _Client(sock, address)
            with self._clients_lock:
                self._clients.append(client)
            # A new GCS learns the current state right away instead of
            # waiting up to a second for the next heartbeat.
            with self._state_lock:
                client.send_line(f"STATE state={self._state} target={_fmt_target(self._target_id)}")
            threading.Thread(
                target=self._read_loop, args=(client,),
                name=f"gcs-link-read-{address[1]}", daemon=True,
            ).start()

    def _read_loop(self, client):
        buffer = b""
        try:
            while not self._stop.is_set() and client.alive:
                data = client.sock.recv(4096)
                if not data:
                    break
                buffer += data
                while b"\n" in buffer:
                    raw, buffer = buffer.split(b"\n", 1)
                    line = raw.decode("ascii", errors="replace").strip()
                    if line:
                        self._commands.put((client, line))
        except OSError:
            pass
        finally:
            client.alive = False
            with self._clients_lock:
                if client in self._clients:
                    self._clients.remove(client)
            try:
                client.sock.close()
            except OSError:
                pass

    def _heartbeat_loop(self):
        # Scheduled against a fixed timeline rather than sleeping a period
        # after each send, so jitter in one beat doesn't push back every
        # later one. Runs on its own thread, so it keeps beating while the
        # main loop waits on a lost video link - the frames= and video=
        # fields are what tell the GCS the pipeline itself has stalled.
        seq = 0
        next_beat = time.monotonic()
        while not self._stop.is_set():
            with self._state_lock:
                line = (
                    f"HEARTBEAT seq={seq} state={self._state} "
                    f"target={_fmt_target(self._target_id)} frames={self._frames} "
                    f"video={'ok' if self._video_ok else 'lost'}"
                )
            print(f"[SHIELD] {line}")
            self._broadcast(line)
            seq += 1
            next_beat += self.heartbeat_period
            delay = next_beat - time.monotonic()
            if delay < 0:
                # Fell more than a period behind (e.g. the PC was suspended):
                # restart the timeline instead of sending a burst.
                next_beat = time.monotonic() + self.heartbeat_period
                delay = self.heartbeat_period
            self._stop.wait(delay)

    def _broadcast(self, line):
        with self._clients_lock:
            clients = list(self._clients)
        for client in clients:
            client.send_line(line)


class GcsLinkClient:
    """The operator's end of the command link, for the GCS window.

    A background thread reads the drone's messages into a queue; the
    window drains it with poll() from its own (Tk) thread, since Tk isn't
    thread-safe. Reconnects on its own if the link drops.
    """

    def __init__(self, host, port, reconnect_delay=1.0):
        self.host = host
        self.port = port
        self.reconnect_delay = reconnect_delay
        self._sock = None
        self._sock_lock = threading.Lock()
        self._incoming = queue.Queue()
        self._stop = threading.Event()
        self._thread = threading.Thread(target=self._run, name="gcs-link-client", daemon=True)
        self._thread.start()

    @property
    def connected(self):
        return self._sock is not None

    def send(self, command):
        """Send one command. Returns False if the link is down."""
        with self._sock_lock:
            sock = self._sock
            if sock is None:
                return False
            try:
                sock.sendall((command + "\n").encode("ascii"))
                return True
            except OSError:
                return False

    def poll(self):
        """Every message received since the last call, oldest first."""
        lines = []
        while True:
            try:
                lines.append(self._incoming.get_nowait())
            except queue.Empty:
                return lines

    def close(self):
        self._stop.set()
        with self._sock_lock:
            if self._sock is not None:
                try:
                    self._sock.close()
                except OSError:
                    pass
                self._sock = None
        self._thread.join(timeout=1.0)

    def _run(self):
        while not self._stop.is_set():
            try:
                sock = socket.create_connection((self.host, self.port), timeout=2.0)
            except OSError:
                self._stop.wait(self.reconnect_delay)
                continue
            sock.settimeout(None)
            sock.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
            with self._sock_lock:
                self._sock = sock
            buffer = b""
            try:
                while not self._stop.is_set():
                    data = sock.recv(4096)
                    if not data:
                        break
                    buffer += data
                    while b"\n" in buffer:
                        raw, buffer = buffer.split(b"\n", 1)
                        line = raw.decode("ascii", errors="replace").strip()
                        if line:
                            self._incoming.put(line)
            except OSError:
                pass
            with self._sock_lock:
                if self._sock is sock:
                    self._sock = None
            try:
                sock.close()
            except OSError:
                pass
            if not self._stop.is_set():
                self._stop.wait(self.reconnect_delay)


def parse_fields(line):
    """'HEARTBEAT seq=3 state=ENGAGED' -> {'seq': '3', 'state': 'ENGAGED'}."""
    fields = {}
    for token in line.split()[1:]:
        key, sep, value = token.partition("=")
        if sep:
            fields[key] = value
    return fields


def _fmt_target(track_id):
    return "-" if track_id is None else str(track_id)

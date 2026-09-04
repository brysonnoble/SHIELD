import argparse
import sys
import time

import config
from gcs_ui import SHIELDGCSWindow
from object_detection import SHIELDDetector
from video_source import UnityStreamSource, VideoFileSource, WebcamSource

PLATFORM_NAMES = {"0": "Emulation", "1": "Hardware", "2": "Prototype"}


def parse_args():
    parser = argparse.ArgumentParser(prog="SHIELD")
    parser.add_argument(
        "platform",
        choices=sorted(PLATFORM_NAMES.keys()),
        help="0=Emulation (PC), 1=Hardware (Jetson Orin Nano), 2=Prototype",
    )
    parser.add_argument(
        "--source",
        choices=["unity", "webcam", "file"],
        default="unity",
        help="Where emulation-mode frames come from (default: unity)",
    )
    parser.add_argument("--file", help="Path to a video file when --source file")
    parser.add_argument(
        "--camera-index", type=int, default=config.WEBCAM_INDEX,
        help="Webcam index when --source webcam",
    )
    parser.add_argument("--no-display", action="store_true", help="Don't open a preview window")
    parser.add_argument(
        "--profile", action="store_true",
        help="Print average per-stage timing (read/detect/render/pump) every 30 frames",
    )
    return parser.parse_args()


def build_source(args):
    if args.source == "unity":
        return UnityStreamSource(
            host=config.UNITY_HOST,
            port=config.UNITY_PORT,
            connect_retries=config.UNITY_CONNECT_RETRIES,
            retry_delay=config.UNITY_RETRY_DELAY_SEC,
            read_timeout=config.UNITY_READ_TIMEOUT_SEC,
        )
    if args.source == "webcam":
        return WebcamSource(index=args.camera_index)
    if args.source == "file":
        if not args.file:
            raise SystemExit("--file is required when --source file")
        return VideoFileSource(args.file)
    raise ValueError(f"Unknown source {args.source}")


def _pump_tick(window):
    """Pump the Tk event loop once. Used as the on_wait callback for any
    blocking wait (initial connect, reconnect backoff) so the GCS window
    keeps redrawing - and stays closable - instead of appearing frozen for
    the whole wait. Returns True if the window was closed, so the wait
    loop calling this can bail out early instead of finishing its delay.
    """
    if window is None:
        return False
    window.pump()
    return window.closed


def _wait_pumping(window, seconds):
    """Sleep for `seconds`, pumping `window` throughout instead of
    blocking it. Returns True if the window was closed during the wait."""
    end = time.monotonic() + seconds
    while time.monotonic() < end:
        if _pump_tick(window):
            return True
        time.sleep(0.05)
    return False


def run_emulation(args):
    source = build_source(args)
    detector = SHIELDDetector()
    window = SHIELDGCSWindow() if not args.no_display else None
    profile_stats = {"read": 0.0, "detect": 0.0, "render": 0.0, "pump": 0.0, "n": 0} if args.profile else None

    print(f"[SHIELD] Starting emulation with source={args.source}")
    consecutive_immediate_drops = 0
    try:
        if window is not None:
            window.set_link_status("Connecting...", "warn")
            window.pump()
        try:
            source.open(on_wait=lambda: _pump_tick(window))
        except (ConnectionError, RuntimeError) as exc:
            # Was previously unguarded: on total failure to connect (e.g.
            # no stream source available at all) this raised straight out
            # of run_emulation, skipping the finally block below - the
            # window never got destroyed or pumped again, so it just sat
            # there looking frozen instead of reporting the real error.
            print(f"[SHIELD] {exc}")
            if window is not None:
                window.set_link_status("No video source - see console", "bad")
                _wait_pumping(window, 5.0)
            return
        if window is not None:
            window.set_link_status("Link stable", "ok")
            window.pump()

        while True:
            if window is not None and window.closed:
                break

            frame_start = time.monotonic()
            t0 = time.perf_counter()
            frame = source.read()
            if profile_stats is not None:
                profile_stats["read"] += time.perf_counter() - t0
            if frame is None:
                if args.source == "unity":
                    # If the connection dies within ~1s of being (re)opened,
                    # over and over, something is actively rejecting us
                    # (wrong process on the port, Unity-side exception,
                    # firewall/AV) rather than a one-off Editor hiccup.
                    # Don't spin-hammer the port in that case.
                    if time.monotonic() - frame_start < 1.0:
                        consecutive_immediate_drops += 1
                    else:
                        consecutive_immediate_drops = 0

                    if consecutive_immediate_drops >= 5:
                        print(
                            "[SHIELD] Connection to Unity keeps dropping immediately "
                            "after connecting. This isn't a transient blip - check the "
                            "Unity Editor Console for '[CameraStreamer]' messages (is "
                            "something else already using this port? did Play Mode "
                            "actually start?). Giving up."
                        )
                        break

                    print("[SHIELD] Lost connection to Unity, reconnecting...")
                    if window is not None:
                        window.set_link_status("Video feed lost - reconnecting...", "bad")
                        if _pump_tick(window):
                            break
                    source.release()
                    if _wait_pumping(window, 1.0):
                        break
                    try:
                        source.open(on_wait=lambda: _pump_tick(window))
                    except ConnectionError as exc:
                        print(f"[SHIELD] Reconnect failed: {exc}")
                        if window is not None:
                            window.set_link_status("Video feed lost - link down", "bad")
                            window.pump()
                        break
                    if window is not None:
                        window.set_link_status("Link stable", "ok")
                        window.pump()
                    continue
                print("[SHIELD] Video source ended or disconnected.")
                if window is not None:
                    window.set_link_status("Video feed lost - source disconnected", "bad")
                    window.pump()
                break

            t0 = time.perf_counter()
            detections = detector.process_frame(frame)
            if profile_stats is not None:
                profile_stats["detect"] += time.perf_counter() - t0
            for det in detections:
                cx, cy = det.smoothed_center
                print(
                    f"target id={det.track_id} class={det.class_name} "
                    f"conf={det.confidence:.2f} pos=({cx:.0f},{cy:.0f})"
                )

            if window is not None:
                t0 = time.perf_counter()
                window.update_frame(frame, detections)
                if profile_stats is not None:
                    profile_stats["render"] += time.perf_counter() - t0

                t0 = time.perf_counter()
                window.pump()
                if profile_stats is not None:
                    profile_stats["pump"] += time.perf_counter() - t0

            if profile_stats is not None:
                profile_stats["n"] += 1
                if profile_stats["n"] >= 30:
                    n = profile_stats["n"]
                    canvas_size = (
                        f"{window.video_canvas.winfo_width()}x{window.video_canvas.winfo_height()}"
                        if window is not None else "n/a"
                    )
                    print(
                        f"[SHIELD][profile] canvas={canvas_size} "
                        f"read={profile_stats['read'] / n * 1000:.1f}ms "
                        f"detect={profile_stats['detect'] / n * 1000:.1f}ms "
                        f"render={profile_stats['render'] / n * 1000:.1f}ms "
                        f"pump={profile_stats['pump'] / n * 1000:.1f}ms "
                        f"total={sum(v for k, v in profile_stats.items() if k != 'n') / n * 1000:.1f}ms"
                    )
                    for key in ("read", "detect", "render", "pump"):
                        profile_stats[key] = 0.0
                    profile_stats["n"] = 0
    finally:
        source.release()
        if window is not None:
            window.destroy()


def main():
    args = parse_args()
    platform_name = PLATFORM_NAMES[args.platform]

    if args.platform != "0":
        raise SystemExit(
            f"Platform '{platform_name}' is not implemented yet. "
            f"Only Emulation (0) runs on PC today."
        )

    run_emulation(args)


if __name__ == "__main__":
    main()

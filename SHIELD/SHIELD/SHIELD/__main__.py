import argparse
import sys

import cv2

import config
from object_detection import SHIELDDetector
from video_source import UnityStreamSource, VideoFileSource, WebcamSource

PLATFORM_NAMES = {"0": "Emulation", "1": "Hardware", "2": "Prototype"}


def parse_args():
    parser = argparse.ArgumentParser(prog="SHIELD")
    parser.add_argument(
        "platform",
        choices=sorted(PLATFORM_NAMES.keys()),
        help="0=Emulation (PC), 1=Hardware (Raspberry Pi), 2=Prototype",
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
    return parser.parse_args()


def build_source(args):
    if args.source == "unity":
        return UnityStreamSource(
            host=config.UNITY_HOST,
            port=config.UNITY_PORT,
            connect_retries=config.UNITY_CONNECT_RETRIES,
            retry_delay=config.UNITY_RETRY_DELAY_SEC,
        )
    if args.source == "webcam":
        return WebcamSource(index=args.camera_index)
    if args.source == "file":
        if not args.file:
            raise SystemExit("--file is required when --source file")
        return VideoFileSource(args.file)
    raise ValueError(f"Unknown source {args.source}")


def run_emulation(args):
    source = build_source(args)
    detector = SHIELDDetector()

    print(f"[SHIELD] Starting emulation with source={args.source}")
    source.open()
    try:
        while True:
            frame = source.read()
            if frame is None:
                print("[SHIELD] Video source ended or disconnected.")
                break

            detections = detector.process_frame(frame)
            for det in detections:
                cx, cy = det.smoothed_center
                print(
                    f"target id={det.track_id} class={det.class_name} "
                    f"conf={det.confidence:.2f} pos=({cx:.0f},{cy:.0f})"
                )

            if not args.no_display:
                annotated = detector.annotate(frame, detections)
                cv2.imshow("SHIELD - Emulation", annotated)
                if cv2.waitKey(1) & 0xFF == ord("q"):
                    break
    finally:
        source.release()
        cv2.destroyAllWindows()


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

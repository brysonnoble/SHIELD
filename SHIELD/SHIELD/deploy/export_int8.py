"""Export a trained checkpoint to an INT8 TensorRT engine for the Jetson
Orin.

This does NOT train in INT8 - it takes an already-trained FP32/FP16
checkpoint (e.g. training/weights/drone_balloon_yolo26s_best.pt) and
post-training-quantizes it, calibrating INT8 scales against a sample of
the real dataset (training/data.yaml) so accuracy loss is minimized.

IMPORTANT: run this ON THE JETSON ORIN itself (with its own JetPack/
TensorRT/ultralytics install), not on the training PC. TensorRT engines
are locked to the GPU architecture and TensorRT version they were built
with, so an engine built here will not load on the Orin.

Run from SHIELD/SHIELD (on the Orin):
    python deploy/export_int8.py
"""

from pathlib import Path

from ultralytics import YOLO

REPO_ROOT = Path(__file__).parent.parent
DATA_YAML = str(REPO_ROOT / "training" / "data.yaml")
WEIGHTS = REPO_ROOT / "training" / "weights" / "drone_balloon_yolo26s_best.pt"
IMG_SIZE = 640
INFERENCE_BATCH = 1  # real-time single-frame inference on the Orin


def main():
    model = YOLO(str(WEIGHTS))
    engine_path = model.export(
        format="engine",
        int8=True,
        data=DATA_YAML,  # calibration images drawn from here
        imgsz=IMG_SIZE,
        batch=INFERENCE_BATCH,
        device=0,
    )
    print(f"Wrote INT8 engine to {engine_path}")


if __name__ == "__main__":
    main()

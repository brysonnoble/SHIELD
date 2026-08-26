"""Fine-tune YOLO11n on the drone dataset (training/data.yaml).

Run from SHIELD/SHIELD:
    .venv\\Scripts\\python training\\train.py

Trains on GPU if available; falls back to CPU otherwise. All raw run
artifacts (checkpoints, plots, last.pt) go to training/runs/, which is
gitignored - only the final best.pt gets copied to training/weights/,
the one drone-model file that IS committed (see .gitignore) so a
second machine gets it via `git pull` instead of retraining.
"""

import shutil
from pathlib import Path

import torch
from ultralytics import YOLO

REPO_ROOT = Path(__file__).parent.parent
DATA_YAML = str(REPO_ROOT / "training" / "data.yaml")
PROJECT_DIR = str(REPO_ROOT / "training" / "runs")
TRACKED_WEIGHTS = REPO_ROOT / "training" / "weights" / "drone_yolo11n_best.pt"
BASE_MODEL = "yolo11n.pt"
EPOCHS = 100
IMG_SIZE = 640
RUN_NAME = "drone_yolo11n"


def main():
    device = 0 if torch.cuda.is_available() else "cpu"
    print(f"Training on device: {device}")

    model = YOLO(BASE_MODEL)
    model.train(
        data=DATA_YAML,
        epochs=EPOCHS,
        imgsz=IMG_SIZE,
        device=device,
        patience=20,
        project=PROJECT_DIR,
        name=RUN_NAME,
    )

    best = model.trainer.save_dir / "weights" / "best.pt"
    TRACKED_WEIGHTS.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(best, TRACKED_WEIGHTS)
    print(f"Copied {best} -> {TRACKED_WEIGHTS}")


if __name__ == "__main__":
    main()

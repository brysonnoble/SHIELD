"""Fine-tune YOLO26s on the combined drone+balloon dataset
(training/data.yaml, produced by training/prepare_combined_dataset.py).

Run from SHIELD/SHIELD:
    .venv\\Scripts\\python training\\train.py
On a bigger GPU (e.g. AI.Panther), raise the batch size and loader workers:
    python training/train.py --batch 64 --workers 8
For a variant run, give it its own name and any extra Ultralytics train
settings as key=value:
    python training/train.py --batch 64 --name seed1 seed=1
    python training/train.py --batch 64 --name coslr_p40 cos_lr=True patience=40

Trains on GPU if available; falls back to CPU otherwise. All raw run
artifacts (checkpoints, plots, last.pt) go to training/runs/, which is
gitignored. The default run's best.pt gets copied to
training/weights/drone_balloon_yolo26s_best.pt, the one model file that
IS committed (see .gitignore) so a second machine gets it via `git pull`
instead of retraining. A named variant run's best.pt goes to
training/weights/<name>_best.pt instead (gitignored), so variants never
overwrite the committed model; promote one by copying it over by hand.
"""

import argparse
import ast
import shutil
from pathlib import Path

import torch
from ultralytics import YOLO

REPO_ROOT = Path(__file__).parent.parent
DATA_YAML = str(REPO_ROOT / "training" / "data.yaml")
PROJECT_DIR = str(REPO_ROOT / "training" / "runs")
WEIGHTS_DIR = REPO_ROOT / "training" / "weights"
TRACKED_WEIGHTS = WEIGHTS_DIR / "drone_balloon_yolo26s_best.pt"
BASE_MODEL = "yolo26s.pt"
EPOCHS = 100
IMG_SIZE = 640
RUN_NAME = "drone_balloon_yolo26s"


def parse_override(text):
    key, sep, value = text.partition("=")
    if not sep:
        raise argparse.ArgumentTypeError(f"expected key=value, got {text!r}")
    try:
        value = ast.literal_eval(value)
    except (ValueError, SyntaxError):
        pass  # leave it as a plain string
    return key, value


def main():
    parser = argparse.ArgumentParser()
    # batch=8 is about the limit on the 6GB laptop card - raising it
    # further there may OOM.
    parser.add_argument("--batch", type=int, default=8)
    parser.add_argument("--workers", type=int, default=2)
    parser.add_argument("--name", default=RUN_NAME)
    parser.add_argument("overrides", nargs="*", type=parse_override,
                        help="extra Ultralytics train settings, key=value")
    args = parser.parse_args()

    device = 0 if torch.cuda.is_available() else "cpu"
    print(f"Training on device: {device}")

    train_args = dict(
        data=DATA_YAML,
        epochs=EPOCHS,
        imgsz=IMG_SIZE,
        device=device,
        patience=20,
        project=PROJECT_DIR,
        name=args.name,
        workers=args.workers,
        batch=args.batch,
    )
    train_args.update(args.overrides)

    model = YOLO(BASE_MODEL)
    model.train(**train_args)

    best = model.trainer.save_dir / "weights" / "best.pt"
    dest = TRACKED_WEIGHTS if args.name == RUN_NAME else WEIGHTS_DIR / f"{args.name}_best.pt"
    dest.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(best, dest)
    print(f"Copied {best} -> {dest}")


if __name__ == "__main__":
    main()

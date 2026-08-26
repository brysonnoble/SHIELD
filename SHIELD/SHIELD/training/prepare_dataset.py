"""One-time split of the Kaggle drone-dataset-uav (already in YOLO txt
format, flat directory) into the images/labels + train/val layout
Ultralytics expects.

Source:  datasets/drone_dataset_yolo/dataset_txt/  (from
         `kaggle datasets download -d dasmehdixtr/drone-dataset-uav`)
Output:  datasets/drone_yolo/{images,labels}/{train,val}/
"""

import random
import shutil
from pathlib import Path

REPO_ROOT = Path(__file__).parent.parent
SRC = REPO_ROOT / "datasets" / "drone_dataset_yolo" / "dataset_txt"
DST = REPO_ROOT / "datasets" / "drone_yolo"
VAL_FRACTION = 0.15
SEED = 0


def main():
    pairs = sorted(p.stem for p in SRC.glob("*.jpg"))
    random.Random(SEED).shuffle(pairs)

    n_val = int(len(pairs) * VAL_FRACTION)
    split = {"val": pairs[:n_val], "train": pairs[n_val:]}

    for split_name, stems in split.items():
        img_dir = DST / "images" / split_name
        lbl_dir = DST / "labels" / split_name
        img_dir.mkdir(parents=True, exist_ok=True)
        lbl_dir.mkdir(parents=True, exist_ok=True)
        for stem in stems:
            shutil.copy2(SRC / f"{stem}.jpg", img_dir / f"{stem}.jpg")
            shutil.copy2(SRC / f"{stem}.txt", lbl_dir / f"{stem}.txt")

    # No `path:` key: Ultralytics then defaults the dataset root to this
    # yaml's own directory (see check_det_dataset in ultralytics/data/
    # utils.py) and resolves train/val relative to that - so this stays
    # a portable, committed file with no machine-specific paths in it.
    data_yaml = Path(__file__).parent / "data.yaml"
    data_yaml.write_text(
        "train: ../datasets/drone_yolo/images/train\n"
        "val: ../datasets/drone_yolo/images/val\n"
        "\n"
        "names:\n"
        "  0: drone\n"
    )

    print(f"train: {len(split['train'])} images, val: {len(split['val'])} images")
    print(f"written to {DST}")
    print(f"wrote {data_yaml}")


if __name__ == "__main__":
    main()

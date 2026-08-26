"""Build the combined drone+balloon training set from the two raw Kaggle
downloads, and (re)generate training/data.yaml for a 2-class model.

Sources (already unzipped under datasets/, which is gitignored):
  - datasets/drone_dataset_yolo/dataset_txt/   flat dir, class 0 "drone"
    (from `kaggle datasets download -d dasmehdixtr/drone-dataset-uav`)
  - datasets/balloon_dataset_yolo/{train,valid}/{images,labels}
    already split, class 0 "balloon"
    (from `kaggle datasets download -d serhiibiruk/balloon-object-detection`)

Output: datasets/combined_yolo/{images,labels}/{train,val}/, with balloon
label class ids remapped 0 -> 1 (drone stays 0) and filenames prefixed per
source so the two sets can't collide.
"""

import random
import shutil
from pathlib import Path

REPO_ROOT = Path(__file__).parent.parent
DRONE_SRC = REPO_ROOT / "datasets" / "drone_dataset_yolo" / "dataset_txt"
BALLOON_SRC = REPO_ROOT / "datasets" / "balloon_dataset_yolo"
DST = REPO_ROOT / "datasets" / "combined_yolo"
DRONE_VAL_FRACTION = 0.15
SEED = 0


def copy_pair(img_src, lbl_src, split_name, stem, remap_class=None):
    img_dir = DST / "images" / split_name
    lbl_dir = DST / "labels" / split_name
    img_dir.mkdir(parents=True, exist_ok=True)
    lbl_dir.mkdir(parents=True, exist_ok=True)

    shutil.copy2(img_src, img_dir / f"{stem}{img_src.suffix}")

    if remap_class is None:
        shutil.copy2(lbl_src, lbl_dir / f"{stem}.txt")
    else:
        lines = lbl_src.read_text().splitlines()
        remapped = []
        for line in lines:
            parts = line.split()
            if not parts:
                continue
            parts[0] = str(remap_class)
            remapped.append(" ".join(parts))
        (lbl_dir / f"{stem}.txt").write_text("\n".join(remapped) + "\n")


def add_drone():
    pairs = sorted(p.stem for p in DRONE_SRC.glob("*.jpg"))
    random.Random(SEED).shuffle(pairs)
    n_val = int(len(pairs) * DRONE_VAL_FRACTION)
    split = {"val": pairs[:n_val], "train": pairs[n_val:]}

    counts = {}
    for split_name, stems in split.items():
        for stem in stems:
            copy_pair(
                DRONE_SRC / f"{stem}.jpg",
                DRONE_SRC / f"{stem}.txt",
                split_name,
                f"drone_{stem}",
            )
        counts[split_name] = len(stems)
    return counts


def add_balloon():
    # Kaggle's own split: train/ -> train, valid/ -> val.
    counts = {}
    for kaggle_split, split_name in [("train", "train"), ("valid", "val")]:
        img_dir = BALLOON_SRC / kaggle_split / "images"
        lbl_dir = BALLOON_SRC / kaggle_split / "labels"
        stems = sorted(p.stem for p in img_dir.glob("*.jpg"))
        for stem in stems:
            copy_pair(
                img_dir / f"{stem}.jpg",
                lbl_dir / f"{stem}.txt",
                split_name,
                f"balloon_{stem}",
                remap_class=1,
            )
        counts[split_name] = len(stems)
    return counts


def main():
    if DST.exists():
        shutil.rmtree(DST)

    drone_counts = add_drone()
    balloon_counts = add_balloon()

    data_yaml = Path(__file__).parent / "data.yaml"
    data_yaml.write_text(
        "train: ../datasets/combined_yolo/images/train\n"
        "val: ../datasets/combined_yolo/images/val\n"
        "\n"
        "names:\n"
        "  0: drone\n"
        "  1: balloon\n"
    )

    print(f"drone:   train {drone_counts['train']}, val {drone_counts['val']}")
    print(f"balloon: train {balloon_counts['train']}, val {balloon_counts['val']}")
    print(f"written to {DST}")
    print(f"wrote {data_yaml}")


if __name__ == "__main__":
    main()

"""Build the combined drone+balloon training set from the raw Kaggle
downloads, and (re)generate training/data.yaml for a 2-class model.

Sources (already unzipped under datasets/, which is gitignored):
  - datasets/seraphim_raw/{train,test}/{images,labels}  class 0 "drone"
    Seraphim drone detection dataset (Hugging Face
    lgrzybowski/seraphim-drone-detection-dataset): 23 public drone sets
    merged, de-duplicated and padded to 640x640. Both of its splits are
    pooled here; our own val split is drawn below. It already contains
    the two Kaggle drone sets this script used to read directly
    (dasmehdixtr/drone-dataset-uav -> datasets/drone_dataset_yolo/,
    sshikamaru/drone-yolo-detection -> datasets/drone_dataset_yolo_2/), so
    those are no longer read: the same photos would otherwise land on
    both sides of the train/val split.
  - datasets/balloon_dataset_yolo/{train,valid}/{images,labels}
    already split, class 0 "balloon"
    (from `kaggle datasets download -d serhiibiruk/balloon-object-detection`)
  - datasets/unity_drone_dataset/dataset_txt/    flat dir, class 0 "drone"
    synthetic renders from the Unity SHIELD Virtual Camera (see
    Test Tools/SHIELD Virtual Camera/Assets/Editor/SHIELD Virtual Camera
    Data Collector); ground-truth boxes computed from known scene
    transforms, not hand-labeled. Fills in the sim-domain gap (day/night
    skybox, camera roll, multi-drone frames) real-photo sources lack.
  - datasets/{dut_anti_uav,det_fly,halmstad}_yolo/dataset_txt/
    flat dirs, class 0 "drone", produced from the raw DUT Anti-UAV,
    Det-Fly and Halmstad downloads by convert_extra_datasets.py (run that
    first). Halmstad also contributes bird/airplane/helicopter frames with
    empty labels as hard negatives.

A source dir that is missing is skipped with a warning, so a partial set
can be built while downloads are still outstanding; check the warnings
before training on the result.

Output: datasets/combined_yolo/{images,labels}/{train,val}/, with balloon
label class ids remapped 0 -> 1 (drone stays 0) and filenames prefixed per
source so the sets can't collide.
"""

import random
import shutil
from pathlib import Path

REPO_ROOT = Path(__file__).parent.parent
DATASETS = REPO_ROOT / "datasets"
SERAPHIM = DATASETS / "seraphim_raw"
# (image dir, label dir, filename prefix); flat dirs hold both.
DRONE_SRCS = [
    (SERAPHIM / "train" / "images", SERAPHIM / "train" / "labels", "seraphim_tr"),
    (SERAPHIM / "test" / "images", SERAPHIM / "test" / "labels", "seraphim_te"),
    *(
        (DATASETS / name / "dataset_txt", DATASETS / name / "dataset_txt", prefix)
        for name, prefix in [
            ("unity_drone_dataset", "unity"),
            ("dut_anti_uav_yolo", "dut"),
            ("det_fly_yolo", "detfly"),
            ("halmstad_yolo", "halmstad"),
        ]
    ),
]
BALLOON_SRC = DATASETS / "balloon_dataset_yolo"
DST = DATASETS / "combined_yolo"
DRONE_VAL_FRACTION = 0.15
IMAGE_EXTS = {".jpg", ".jpeg", ".png"}
SEED = 0


def list_images(directory):
    return sorted(p for p in directory.iterdir() if p.suffix.lower() in IMAGE_EXTS)


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
    pairs = []  # (img_path, lbl_path, stem)
    skipped = 0
    for img_dir, lbl_dir, prefix in DRONE_SRCS:
        if not img_dir.is_dir():
            print(f"WARNING: drone source missing, skipped: {img_dir}")
            continue
        for img_path in list_images(img_dir):
            lbl_path = lbl_dir / f"{img_path.stem}.txt"
            if not lbl_path.exists():
                skipped += 1
                continue
            pairs.append((img_path, lbl_path, f"{prefix}_{img_path.stem}"))

    random.Random(SEED).shuffle(pairs)
    n_val = int(len(pairs) * DRONE_VAL_FRACTION)
    split = {"val": pairs[:n_val], "train": pairs[n_val:]}

    counts = {}
    for split_name, items in split.items():
        for img_path, lbl_path, stem in items:
            copy_pair(img_path, lbl_path, split_name, stem)
        counts[split_name] = len(items)
    if skipped:
        print(f"drone: skipped {skipped} image(s) with no label file")
    return counts


def add_balloon():
    # Kaggle's own split: train/ -> train, valid/ -> val.
    counts = {"train": 0, "val": 0}
    if not BALLOON_SRC.is_dir():
        print(f"WARNING: balloon source missing, skipped: {BALLOON_SRC}")
        return counts
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

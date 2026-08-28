"""Build the combined drone+balloon training set from the raw Kaggle
downloads, and (re)generate training/data.yaml for a 2-class model.

Sources (already unzipped under datasets/, which is gitignored):
  - datasets/drone_dataset_yolo/dataset_txt/    flat dir, class 0 "drone"
    (from `kaggle datasets download -d dasmehdixtr/drone-dataset-uav`)
  - datasets/drone_dataset_yolo_2/dataset_txt/  flat dir, class 0 "drone"
    (from `kaggle datasets download -d sshikamaru/drone-yolo-detection`)
    adds more scale/distance variety (many far-away/small-in-frame drones);
    some images are unlabeled backgrounds (empty .txt), kept as negatives.
  - datasets/balloon_dataset_yolo/{train,valid}/{images,labels}
    already split, class 0 "balloon"
    (from `kaggle datasets download -d serhiibiruk/balloon-object-detection`)
  - datasets/unity_drone_dataset/dataset_txt/    flat dir, class 0 "drone"
    synthetic renders from the Unity SHIELD Virtual Camera (see
    Test Tools/SHIELD Virtual Camera/Assets/Editor/SHIELD Virtual Camera
    Data Collector); ground-truth boxes computed from known scene
    transforms, not hand-labeled. Fills in the sim-domain gap (day/night
    skybox, camera roll, multi-drone frames) real-photo sources lack.

Output: datasets/combined_yolo/{images,labels}/{train,val}/, with balloon
label class ids remapped 0 -> 1 (drone stays 0) and filenames prefixed per
source so the sets can't collide.
"""

import random
import shutil
from pathlib import Path

REPO_ROOT = Path(__file__).parent.parent
DRONE_SRCS = [
    (REPO_ROOT / "datasets" / "drone_dataset_yolo" / "dataset_txt", "drone"),
    (REPO_ROOT / "datasets" / "drone_dataset_yolo_2" / "dataset_txt", "drone2"),
    (REPO_ROOT / "datasets" / "unity_drone_dataset" / "dataset_txt", "unity"),
]
BALLOON_SRC = REPO_ROOT / "datasets" / "balloon_dataset_yolo"
DST = REPO_ROOT / "datasets" / "combined_yolo"
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
    for src_dir, prefix in DRONE_SRCS:
        for img_path in list_images(src_dir):
            lbl_path = src_dir / f"{img_path.stem}.txt"
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

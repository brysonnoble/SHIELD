"""Convert the non-YOLO drone datasets into the flat YOLO layout that
prepare_combined_dataset.py reads (one dataset_txt/ dir per source, each
image next to a same-stem .txt label, class 0 "drone").

Sources (raw downloads under datasets/, which is gitignored):
  - datasets/dut_anti_uav_raw/{train,val,test}/{img,xml}
    DUT Anti-UAV detection set, Pascal VOC XML, class "UAV". All three
    official splits are used as training material; our own val split is
    drawn later by prepare_combined_dataset.py.
  - datasets/det_fly_raw/{JPEGImages,Annotations}/{010,020}/
    Det-Fly air-to-air set, Pascal VOC XML. Full-res frames are
    downscaled to DET_FLY_MAX_SIDE on the long side (labels are
    normalized, so they're unaffected) to keep the set a manageable size.
  - datasets/halmstad_raw/Video_V/
    Halmstad multi-sensor drone dataset, visible-light videos only (the
    IR videos and audio clips are not used and needn't be downloaded).
    Labels are MATLAB groundTruth .mat files, read with `mcos-decoder`
    (pip install mcos-decoder). Every DRONE_FRAME_STEP-th frame of the
    DRONE videos is kept with its box; every OTHER_FRAME_STEP-th frame of
    the AIRPLANE/BIRD/HELICOPTER videos is kept with an empty label as a
    hard negative.

Output: datasets/{dut_anti_uav,det_fly,halmstad}_yolo/dataset_txt/.
Pass source names to convert only some, e.g. `python
convert_extra_datasets.py halmstad`.
"""

import shutil
import sys
import xml.etree.ElementTree as ET
from concurrent.futures import ProcessPoolExecutor
from pathlib import Path

import cv2

DATASETS = Path(__file__).parent.parent / "datasets"
DUT_SRC = DATASETS / "dut_anti_uav_raw"
DET_FLY_SRC = DATASETS / "det_fly_raw"
HALMSTAD_SRC = DATASETS / "halmstad_raw" / "Video_V"
DRONE_NAMES = {"uav", "drone"}
DET_FLY_MAX_SIDE = 1920
DRONE_FRAME_STEP = 10
OTHER_FRAME_STEP = 30


def fresh_dst(name):
    dst = DATASETS / f"{name}_yolo" / "dataset_txt"
    if dst.exists():
        shutil.rmtree(dst)
    dst.mkdir(parents=True)
    return dst


def yolo_line(x0, y0, x1, y1, w, h):
    x0, x1 = max(0.0, x0), min(float(w), x1)
    y0, y1 = max(0.0, y0), min(float(h), y1)
    if x1 <= x0 or y1 <= y0:
        return None
    return (
        f"0 {(x0 + x1) / 2 / w:.6f} {(y0 + y1) / 2 / h:.6f} "
        f"{(x1 - x0) / w:.6f} {(y1 - y0) / h:.6f}"
    )


def voc_to_lines(xml_path, unknown):
    root = ET.parse(xml_path).getroot()
    w = float(root.findtext("size/width"))
    h = float(root.findtext("size/height"))
    lines = []
    for obj in root.iter("object"):
        name = obj.findtext("name", "").strip()
        if name.lower() not in DRONE_NAMES:
            unknown[name] = unknown.get(name, 0) + 1
            continue
        box = obj.find("bndbox")
        line = yolo_line(*(float(box.findtext(k)) for k in ("xmin", "ymin", "xmax", "ymax")), w, h)
        if line:
            lines.append(line)
    return lines


def write_label(path, lines):
    path.write_text("\n".join(lines) + "\n" if lines else "")


def convert_dut():
    dst = fresh_dst("dut_anti_uav")
    unknown, n = {}, 0
    for split in ("train", "val", "test"):
        for xml_path in sorted((DUT_SRC / split / "xml").glob("*.xml")):
            img = DUT_SRC / split / "img" / f"{xml_path.stem}.jpg"
            if not img.exists():
                continue
            stem = f"{split}_{xml_path.stem}"
            shutil.copy2(img, dst / f"{stem}.jpg")
            write_label(dst / f"{stem}.txt", voc_to_lines(xml_path, unknown))
            n += 1
    report("dut_anti_uav", n, unknown)


def det_fly_one(xml_path):
    """Convert one Det-Fly frame; returns its unknown object names, or
    None if the image is missing. Runs in a worker process."""
    rel = xml_path.relative_to(DET_FLY_SRC / "Annotations").with_suffix("")
    img_path = next(
        (p for ext in (".jpg", ".JPG", ".jpeg", ".png")
         if (p := (DET_FLY_SRC / "JPEGImages" / rel).with_suffix(ext)).exists()),
        None,
    )
    if img_path is None:
        return None
    dst = DATASETS / "det_fly_yolo" / "dataset_txt"
    stem = "_".join(rel.parts)
    unknown = {}
    img = cv2.imread(str(img_path))
    scale = DET_FLY_MAX_SIDE / max(img.shape[:2])
    if scale < 1:
        img = cv2.resize(img, None, fx=scale, fy=scale, interpolation=cv2.INTER_AREA)
    cv2.imwrite(str(dst / f"{stem}.jpg"), img, [cv2.IMWRITE_JPEG_QUALITY, 92])
    write_label(dst / f"{stem}.txt", voc_to_lines(xml_path, unknown))
    return unknown


def convert_det_fly():
    # Decoding and downscaling the 4K originals is the slow part, so it
    # runs across all cores.
    fresh_dst("det_fly")
    unknown, n = {}, 0
    xmls = sorted((DET_FLY_SRC / "Annotations").rglob("*.xml"))
    with ProcessPoolExecutor() as pool:
        for result in pool.map(det_fly_one, xmls, chunksize=32):
            if result is None:
                continue
            n += 1
            for name, count in result.items():
                unknown[name] = unknown.get(name, 0) + count
    report("det_fly", n, unknown)


def convert_halmstad():
    from mcos_decoder import load_groundtruth

    dst = fresh_dst("halmstad")
    counts = {"drone": 0, "negative": 0}
    for video in sorted(HALMSTAD_SRC.glob("V_*.mp4")):
        is_drone = video.stem.startswith("V_DRONE")
        step = DRONE_FRAME_STEP if is_drone else OTHER_FRAME_STEP
        boxes = load_groundtruth(str(HALMSTAD_SRC / f"{video.stem}_LABELS.mat"))
        cap = cv2.VideoCapture(str(video))
        idx = 0
        while True:
            ok, frame = cap.read()
            if not ok:
                break
            if idx % step == 0:
                h, w = frame.shape[:2]
                lines = []
                box = boxes[idx] if idx < len(boxes) else None
                if is_drone and box is not None:
                    x, y, bw, bh = box
                    line = yolo_line(x, y, x + bw, y + bh, w, h)
                    lines = [line] if line else []
                stem = f"{video.stem}_{idx:04d}"
                cv2.imwrite(str(dst / f"{stem}.jpg"), frame, [cv2.IMWRITE_JPEG_QUALITY, 95])
                write_label(dst / f"{stem}.txt", lines)
                counts["drone" if lines else "negative"] += 1
            idx += 1
        cap.release()
    print(f"halmstad: {counts['drone']} drone frames, {counts['negative']} negative frames")


def report(name, n, unknown):
    print(f"{name}: {n} images")
    if unknown:
        print(f"{name}: ignored non-drone object names {unknown}")


CONVERTERS = {"dut_anti_uav": convert_dut, "det_fly": convert_det_fly, "halmstad": convert_halmstad}


def main():
    for name in sys.argv[1:] or CONVERTERS:
        CONVERTERS[name]()


if __name__ == "__main__":
    main()

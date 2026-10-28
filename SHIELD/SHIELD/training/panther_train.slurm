#!/bin/bash
# Slurm job for training on AI.Panther (Florida Tech HPC). Submit from
# SHIELD/SHIELD on the login node:
#     sbatch training/panther_train.slurm
# Watch it with `squeue -u $USER` and `tail -f training/logs/train.<jobid>.out`.
#
# One-time setup on the login node (light work, allowed there). The
# `python` module resolves to a build compiled for whichever CPU you load it
# on: the login node's build (Intel) dies with "Illegal instruction" on the
# AMD EPYC GPU nodes. Build the venv from the GPU nodes' zen2 build, which
# also runs on the login node:
#     /usr/local/spack/opt/spack/linux-ubuntu24.04-zen2/gcc-13.3.0/python-3.13.0-utjglfymbfyjlc5rdpkpblvhn5txgx6g/bin/python -m venv ~/shield-venv
#     source ~/shield-venv/bin/activate
#     pip install -r requirements.txt
# Compute nodes may not have internet access, so yolo26s.pt must already
# be in SHIELD/SHIELD (copy it over with the repo), and Ultralytics' plot
# font is fetched once here:
#     python -c "from ultralytics.utils.checks import check_font; check_font('Arial.ttf')"
#
# gpu1/gpu2 are the A100 partitions (no time limit); h200 is also an option.

#SBATCH --job-name=shield-yolo26s
#SBATCH --partition=gpu1
#SBATCH --nodes=1
#SBATCH --ntasks=1
#SBATCH --gres=gpu:1
#SBATCH --cpus-per-task=16
#SBATCH --mem=64GB
#SBATCH --time=3-00:00:00
#SBATCH --output=training/logs/train.%J.out
#SBATCH --error=training/logs/train.%J.err

source ~/shield-venv/bin/activate
# train.py silently falls back to CPU; on a GPU job that's a failure.
python -c "import torch, sys; sys.exit(0 if torch.cuda.is_available() else 1)" || { echo "No CUDA GPU visible"; exit 1; }

echo "Starting at $(date) on $SLURM_NODELIST"
nvidia-smi
python training/train.py --batch 64 --workers 14
echo "Finished at $(date)"

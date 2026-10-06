@echo off
setlocal enabledelayedexpansion

echo ================================================
echo  SHIELD Local Emulation Setup
echo ================================================
echo.

set "REPO_ROOT=%~dp0"
set "PY_DIR=%REPO_ROOT%SHIELD\SHIELD"
set "VENV_DIR=%PY_DIR%\.venv"

if not exist "%PY_DIR%\requirements.txt" (
    echo [ERROR] Could not find "%PY_DIR%\requirements.txt".
    echo Run this script from the root of the SHIELD repo.
    exit /b 1
)

where python >nul 2>nul
if errorlevel 1 (
    echo [ERROR] Python was not found on PATH.
    echo Install Python 3.11+ from https://www.python.org/downloads/
    echo and make sure "Add python.exe to PATH" is checked, then re-run this script.
    exit /b 1
)

for /f "tokens=*" %%v in ('python --version 2^>^&1') do echo Found %%v

echo.
echo Creating virtual environment at "%VENV_DIR%" ...
python -m venv "%VENV_DIR%"
if errorlevel 1 (
    echo [ERROR] Failed to create the virtual environment.
    exit /b 1
)

echo.
echo Installing Python dependencies ...
"%VENV_DIR%\Scripts\python.exe" -m pip install --upgrade pip
if errorlevel 1 (
    echo [ERROR] Failed to upgrade pip.
    exit /b 1
)

rem PyPI's Windows PyTorch wheels are CPU-only, so install a CUDA build
rem from PyTorch's own index first (before requirements.txt pulls torch
rem in via ultralytics) when an NVIDIA GPU + driver is present:
rem   cu130 - driver reports CUDA 13.0+ and GPU is Turing (sm_75) or newer
rem   cu126 - driver reports CUDA 12.6+ (also covers pre-Turing GPUs,
rem           which CUDA 13 dropped)
rem   none  - fall back to PyPI's CPU-only build
echo.
echo Detecting NVIDIA GPU for PyTorch ...
set "TORCH_INDEX="
set "CUDA_MAJOR=0"
set "CUDA_MINOR=0"
set "CC_MAJOR="
set "CC_MINOR=0"
where nvidia-smi >nul 2>nul
if errorlevel 1 (
    echo No NVIDIA GPU detected ^(nvidia-smi not found^).
    goto :torch_selected
)
for /f "tokens=3 delims=:" %%a in ('nvidia-smi ^| findstr /c:"CUDA Version"') do (
    for /f "tokens=1,2 delims=. " %%b in ("%%a") do (
        set "CUDA_MAJOR=%%b"
        set "CUDA_MINOR=%%c"
    )
)
for /f "tokens=1,2 delims=." %%a in ('nvidia-smi --query-gpu^=compute_cap --format^=csv^,noheader 2^>nul') do (
    if not defined CC_MAJOR (
        set "CC_MAJOR=%%a"
        set "CC_MINOR=%%b"
    )
)
rem Anything non-numeric (e.g. "N/A", or an old driver that doesn't know
rem the compute_cap field) is treated as 0 / unknown.
echo !CUDA_MAJOR!| findstr /r "^[0-9][0-9]*$" >nul || set "CUDA_MAJOR=0"
echo !CUDA_MINOR!| findstr /r "^[0-9][0-9]*$" >nul || set "CUDA_MINOR=0"
echo !CC_MAJOR!| findstr /r "^[0-9][0-9]*$" >nul || set "CC_MAJOR=0"
echo !CC_MINOR!| findstr /r "^[0-9][0-9]*$" >nul || set "CC_MINOR=0"
set /a CUDA_VER=CUDA_MAJOR*10+CUDA_MINOR
set /a GPU_CC=CC_MAJOR*10+CC_MINOR
echo Driver supports CUDA !CUDA_MAJOR!.!CUDA_MINOR!, GPU compute capability !CC_MAJOR!.!CC_MINOR!
if !GPU_CC! GEQ 75 if !CUDA_VER! GEQ 130 set "TORCH_INDEX=cu130"
if not defined TORCH_INDEX if !CUDA_VER! GEQ 126 set "TORCH_INDEX=cu126"
if not defined TORCH_INDEX (
    echo [WARNING] NVIDIA driver is too old for current CUDA PyTorch builds ^(needs CUDA 12.6+^).
    echo Update your driver from https://www.nvidia.com/drivers and re-run this script for GPU support.
)

:torch_selected
if defined TORCH_INDEX (
    echo Installing CUDA PyTorch build ^(!TORCH_INDEX!^) ...
    "%VENV_DIR%\Scripts\python.exe" -m pip install torch torchvision --index-url https://download.pytorch.org/whl/!TORCH_INDEX!
    if errorlevel 1 (
        echo [ERROR] Failed to install the CUDA PyTorch build.
        exit /b 1
    )
    rem A venv from an earlier run may already hold PyPI's CPU-only torch,
    rem which the install above leaves alone - swap it for the CUDA build.
    "%VENV_DIR%\Scripts\python.exe" -c "import sys, torch; sys.exit(0 if torch.cuda.is_available() else 1)" >nul 2>nul
    if errorlevel 1 (
        echo Replacing existing CPU-only PyTorch with the CUDA build ...
        "%VENV_DIR%\Scripts\python.exe" -m pip install --force-reinstall --no-deps torch torchvision --index-url https://download.pytorch.org/whl/!TORCH_INDEX!
        if errorlevel 1 (
            echo [ERROR] Failed to install the CUDA PyTorch build.
            exit /b 1
        )
    )
)

"%VENV_DIR%\Scripts\python.exe" -m pip install -r "%PY_DIR%\requirements.txt"
if errorlevel 1 (
    echo [ERROR] Failed to install dependencies from requirements.txt.
    exit /b 1
)

echo.
"%VENV_DIR%\Scripts\python.exe" -c "import sys, torch; print('PyTorch', torch.__version__, '- CUDA available on', torch.cuda.get_device_name(0)) if torch.cuda.is_available() else print('PyTorch', torch.__version__, '- CUDA NOT available'); sys.exit(0 if torch.cuda.is_available() else 1)"
if errorlevel 1 (
    echo [WARNING] SHIELD will not be able to run inference on the GPU.
    echo Set DEVICE = "cpu" in "%PY_DIR%\config.py", or fix the GPU driver and re-run this script.
)

echo.
echo Pre-downloading the default YOLO model ...
pushd "%PY_DIR%"
"%VENV_DIR%\Scripts\python.exe" -c "from ultralytics import YOLO; YOLO('yolo26s.pt')"
set "MODEL_RESULT=%errorlevel%"
popd
if not "%MODEL_RESULT%"=="0" (
    echo [WARNING] Could not pre-download the YOLO model ^(check your internet connection^).
    echo It will be downloaded automatically the first time you run SHIELD instead.
)

echo.
echo ================================================
echo  Setup complete.
echo ================================================
echo.
echo Run against your webcam right now:
echo   cd "%PY_DIR%"
echo   .venv\Scripts\python __main__.py 0 --source webcam
echo.
echo To use the Unity virtual camera instead, see README.md for the
echo one-time Unity setup steps, then run:
echo   .venv\Scripts\python __main__.py 0 --source unity
echo.

endlocal

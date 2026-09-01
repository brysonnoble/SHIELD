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

"%VENV_DIR%\Scripts\python.exe" -m pip install -r "%PY_DIR%\requirements.txt"
if errorlevel 1 (
    echo [ERROR] Failed to install dependencies from requirements.txt.
    exit /b 1
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

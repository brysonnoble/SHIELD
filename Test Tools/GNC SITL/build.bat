@echo off
rem Builds build\shield_gnc_sitl.exe. Uses MSVC (any Visual Studio 2019+
rem install with the C++ workload, found via vswhere) if present, else
rem g++ on PATH (e.g. MSYS2 MinGW-w64). STE_Test_Solution.vbproj runs this
rem automatically before every test build when a source file changed.
setlocal

set "HERE=%~dp0"
set "OUT=%HERE%build"
set "SOURCES=..\src\main.cpp ..\src\vehicle_model.cpp ..\src\lqr_controller.cpp ..\src\io.cpp"
if not exist "%OUT%" mkdir "%OUT%"
pushd "%OUT%"

set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
set "VSINSTALL="
if not exist "%VSWHERE%" goto :try_gxx
for /f "usebackq tokens=*" %%i in (`"%VSWHERE%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "VSINSTALL=%%i"
if not defined VSINSTALL goto :try_gxx
if not exist "%VSINSTALL%\VC\Auxiliary\Build\vcvars64.bat" goto :try_gxx

call "%VSINSTALL%\VC\Auxiliary\Build\vcvars64.bat" >nul 2>nul
rem vcvars finds MSVC even without a Windows SDK, but windows.h/the
rem CRT headers come from the SDK - fall back to g++ when it's missing.
if not defined UCRTVersion (
    echo [GNC SITL] MSVC found but no Windows 10/11 SDK is installed - trying g++
    goto :try_gxx
)
echo [GNC SITL] Building with MSVC
cl /nologo /O2 /std:c++17 /EHsc /W4 /DNOMINMAX /Fe:shield_gnc_sitl.exe %SOURCES% ws2_32.lib winmm.lib
goto :done

:try_gxx
where g++ >nul 2>nul
if errorlevel 1 (
    echo [GNC SITL] ERROR: no C++ compiler found. Install Visual Studio's "Desktop development with C++" workload, or MSYS2 MinGW-w64 g++.
    popd
    exit /b 1
)
echo [GNC SITL] Building with g++
g++ -O2 -std=c++17 -Wall -Wextra -DNOMINMAX -static -o shield_gnc_sitl.exe %SOURCES% -lws2_32 -lwinmm

:done
set "RESULT=%errorlevel%"
popd
if not "%RESULT%"=="0" (
    echo [GNC SITL] Build FAILED.
    exit /b %RESULT%
)
echo [GNC SITL] Built "%OUT%\shield_gnc_sitl.exe"
exit /b 0

Imports System.IO

Public Module Common_Test_Variables
    ' Resolved from the running exe's own bin folder (AppContext.BaseDirectory)
    ' rather than a fixed relative path, so it's correct regardless of whether
    ' this is launched via "dotnet run", Visual Studio, the built exe directly,
    ' or (per HomePage.xaml.cs) launched by the STE app as a child process.
    Private ReadOnly RepoRoot As String = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", ".."))

    ' Matches README.md's "Running against the Unity virtual camera":
    '   cd "SHIELD\SHIELD"
    '   .venv\Scripts\python __main__.py 0 --source unity
    Public ReadOnly SHIELD_DIRECTORY As String = Path.Combine(RepoRoot, "SHIELD", "SHIELD")
    Public ReadOnly PYTHON_PATH As String = Path.Combine(SHIELD_DIRECTORY, ".venv", "Scripts", "python.exe")

    ' Built standalone player for the "SHIELD Virtual Camera" Unity project,
    ' so tests don't require the Unity Editor to be open in Play Mode.
    Public ReadOnly UNITY_PLAYER_PATH As String = Path.Combine(
        RepoRoot, "Test Tools", "SHIELD Virtual Camera", "SHIELD Virtual Camera.exe")

    ' Unity scene TCP command listeners. Must match SceneSelector.cs's,
    ' DroneSpawner.cs's, and CameraController.cs's "port" fields in the
    ' "SHIELD Virtual Camera" project.
    Public Const UNITY_HOST As String = "127.0.0.1"
    Public Const UNITY_ENV_PORT As Integer = 5556
    Public Const UNITY_SPAWN_PORT As Integer = 5557
    Public Const UNITY_CAMERA_PORT As Integer = 5558

    ' The Python pipeline's drone <-> GCS command link (gcs_link.py). Must
    ' match config.py's GCS_LINK_HOST/GCS_LINK_PORT. Test scripts connect to
    ' it as a second GCS, alongside the GCS window.
    Public Const GCS_LINK_HOST As String = "127.0.0.1"
    Public Const GCS_LINK_PORT As Integer = 5560

    ' Where the frame edges sit in InstDroneAtRange()'s offset units (the
    ' tangent of the angle off the line of sight). Must match the frames
    ' CameraStreamer.cs streams - the Waveshare IMX219-83 it emulates, in the
    ' IMX219's 1280x720 @ 60 fps mode: a 34.5 degree vertical field of view,
    ' so tan(17.25 deg) (~0.31), on a 16:9 image, which widens the
    ' horizontal edge by 1280/720 (~0.55).
    Public ReadOnly FRAME_EDGE_OFFSET_Y As Double = Math.Tan(17.25 * Math.PI / 180)
    Public ReadOnly FRAME_EDGE_OFFSET_X As Double = FRAME_EDGE_OFFSET_Y * 1280 / 720

    ' The same frames in pixels, for turning an offset into the pixel
    ' position the pipeline reports ("pos=(x,y)", y down): x = width/2 +
    ' offX * FOCAL_PX, y = height/2 - offY * FOCAL_PX.
    Public Const FRAME_WIDTH_PX As Integer = 1280
    Public Const FRAME_HEIGHT_PX As Integer = 720
    Public ReadOnly FOCAL_PX As Double = (FRAME_HEIGHT_PX / 2) / FRAME_EDGE_OFFSET_Y

    ' Must match DroneAutopilot.cs's maxAcceleration (m/s^2) - see
    ' Common_Test_Functions.EstimatedFlightSeconds().
    Public Const DRONE_MAX_ACCELERATION As Double = 4

    ' Root folder under which each test's logs are written - one subfolder
    ' per test name, then one subfolder per run (named by the run's start
    ' time). Set from the command line by Program.vb (STE's Settings page
    ' controls this - see AppSettings.LogDirectory); this default is only
    ' used when STE_Test_Solution.exe is run standalone.
    Public LogRootDirectory As String = Path.Combine(RepoRoot, "Test Tools", "STE", "Logs")

    ' Set by Program.vb, before invoking a test script's Main, to that
    ' script's module name (e.g. "AVS_Detection_Test") - used to name that
    ' test's log subfolder under LogRootDirectory.
    Public CurrentTestName As String = ""

    ' Seconds BeginTest() waits, after Unity's TCP listeners are confirmed up,
    ' before test cases start - gives everything else (Python's model load,
    ' its OpenCV preview window, Unity's scene) time to finish opening.
    ' Set from the command line by Program.vb (STE's Settings page controls
    ' this - see AppSettings.cs); defaults to 15s otherwise.
    Public StartupDelaySeconds As Integer = 15

    Public Enum TestPlatform
        Emulation
        Hardware
        Prototype
    End Enum

    Public Enum VirtualEnvironment
        Day
        Night
    End Enum

    Public Enum DroneType
        Quad
        Toad
        BumbleBee
    End Enum
End Module

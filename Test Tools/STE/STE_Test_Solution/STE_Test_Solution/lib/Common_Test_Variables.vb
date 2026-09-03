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

    ' Unity scene TCP command listeners. Must match SceneSelector.cs's and
    ' DroneSpawner.cs's "port" fields in the "SHIELD Virtual Camera" project.
    Public Const UNITY_HOST As String = "127.0.0.1"
    Public Const UNITY_ENV_PORT As Integer = 5556
    Public Const UNITY_SPAWN_PORT As Integer = 5557

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

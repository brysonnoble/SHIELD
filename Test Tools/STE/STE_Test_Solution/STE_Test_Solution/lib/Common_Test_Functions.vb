Imports System.Globalization
Imports System.IO
Imports System.Threading

Public Module Common_Test_Functions
    Private unityProcess As Process
    Private pythonProcess As Process
    Private logWriter As StreamWriter
    Private testCaseNumber As Integer

    Public Sub BeginTest()
        Dim runFolder As String = Path.Combine(
            LogRootDirectory, CurrentTestName, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture))
        Directory.CreateDirectory(runFolder)
        logWriter = New StreamWriter(Path.Combine(runFolder, CurrentTestName & ".log"))
        logWriter.AutoFlush = True
        testCaseNumber = 0
        WriteLog($"=== BeginTest: {CurrentTestName} ===")

        unityProcess = RunProcess(UNITY_PLAYER_PATH)

        ' Matches README.md's "Running against the Unity virtual camera":
        '   cd "SHIELD\SHIELD"
        '   .venv\Scripts\python __main__.py 0 --source unity
        pythonProcess = RunProcess(PYTHON_PATH, $"__main__.py {CInt(TestPlatform.Emulation)} --source unity", SHIELD_DIRECTORY)

        ' Don't let test cases start until every launched program is actually
        ' up, not just started - Process.Start() returning only means the OS
        ' created the process, not that Unity has reached Play and bound its
        ' scene command listeners.
        WaitForListener(UNITY_HOST, UNITY_ENV_PORT)
        WaitForListener(UNITY_HOST, UNITY_SPAWN_PORT)

        ' Confirms Unity's TCP listeners are up, but not that everything else
        ' (Python's model load, its OpenCV preview window, Unity's scene
        ' fully rendering) has finished opening. Configurable in STE's
        ' Settings page.
        Thread.Sleep(StartupDelaySeconds * 1000)
    End Sub

    Public Sub EndTest()
        CloseProcess(pythonProcess)
        CloseProcess(unityProcess)
        pythonProcess = Nothing
        unityProcess = Nothing

        WriteLog("=== EndTest ===")
        logWriter.Dispose()
        logWriter = Nothing
    End Sub

    ' Call at the start of every test case (TCxx), before any TraceTo/other
    ' calls. Resets the Unity Main Camera to the world origin so each test
    ' case starts from the same known camera position, and marks the test
    ' case's start in the log.
    Public Sub TestCaseBegin()
        testCaseNumber += 1
        WriteLog($"--- TestCase {testCaseNumber} Begin ---")
        ResetCamera()
    End Sub

    ' Call at the end of every test case (TCxx). Resets the camera again so
    ' the next thing that runs doesn't inherit a leftover camera position,
    ' and marks the test case's end in the log.
    Public Sub TestCaseEnd()
        ResetCamera()
        WriteLog($"--- TestCase {testCaseNumber} End ---")
    End Sub

    Private Sub ResetCamera()
        SendTcpCommand(UNITY_HOST, UNITY_CAMERA_PORT, "CAM RESET")
    End Sub

    ' Call at the start of a test case, once per requirement it tests, so
    ' the requirement coverage for that test case is recorded in the log.
    Public Sub TraceTo(requirementName As String)
        WriteLog($"Trace: {requirementName}")
    End Sub

    Private Sub WriteLog(message As String)
        Dim line As String = $"[{DateTime.Now:HH:mm:ss}] {message}"
        Console.WriteLine(line)
        logWriter?.WriteLine(line)
    End Sub

    ' Sends a command over TCP to the Unity scene to switch the day/night skybox.
    Public Sub EditVirtualEnvironment(virtualEnvironment As Common_Test_Variables.VirtualEnvironment)
        SendTcpCommand(UNITY_HOST, UNITY_ENV_PORT, "ENV " & virtualEnvironment.ToString().ToUpperInvariant())
    End Sub

    ' Sends a command over TCP to the Unity scene to instantiate one of the
    ' three drones (DroneType.Quad/Toad/BumbleBee) at the given world coordinates.
    Public Sub InstDrone(droneType As Common_Test_Variables.DroneType, x As Double, y As Double, z As Double)
        Dim command As String = String.Format(CultureInfo.InvariantCulture,
                                               "SPAWN {0} {1} {2} {3}",
                                               droneType.ToString(), x, y, z)
        SendTcpCommand(UNITY_HOST, UNITY_SPAWN_PORT, command)
    End Sub
End Module
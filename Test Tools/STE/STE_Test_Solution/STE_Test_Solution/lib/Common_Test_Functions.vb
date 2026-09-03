Imports System.Globalization
Imports System.Threading

Public Module Common_Test_Functions
    Private unityProcess As Process
    Private pythonProcess As Process

    Public Sub BeginTest()
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
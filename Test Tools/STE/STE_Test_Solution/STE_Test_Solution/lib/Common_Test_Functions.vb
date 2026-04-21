Imports System.IO

Public Module Common_Test_Functions
    Public Sub BeginTest()
        RunProcess(SHIELD_PATH, TestPlatform.Emulation)
        ' TODO: launch all programs
    End Sub
    Public Sub EndTest()
        ' TODO: close all programs

        ' Clear the shared .txt files used in the Unity scene to prevent test data from being used in future tests.
        File.WriteAllText("..\..\SHIELD Virtual Camera\Assets\STE Shared Data\TargetList.txt", "")
        File.WriteAllText("..\..\SHIELD Virtual Camera\Assets\STE Shared Data\VirtualEnvironment.txt", "")
    End Sub

    ' This function Is used to edit the virtual environment "setting" for the test
    ' by editing a shared .txt file used in the Unity scene.
    Public Sub EditVirtualEnvironment(virtualEnvironment As Common_Test_Variables.VirtualEnvironment)

    End Sub

    ' This function Is used to set the target position for the test by editing a
    ' shared .txt file used in the Unity scene.
    Public Sub InstTarget(targetPos As Double)

    End Sub
End Module
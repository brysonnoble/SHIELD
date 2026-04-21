Imports System.IO

Module Common_Test_Functions
    Function BeginTest()
        ' TODO: launch all programs
    End Function

    Function EndTest()
        ' TODO: close all programs

        ' Clear the shared .txt files used in the Unity scene to prevent test data from being used in future tests.
        File.WriteAllText("..\..\SHIELD Virtual Camera\Assets\STE Shared Data\TargetList.txt", "")
        File.WriteAllText("..\..\SHIELD Virtual Camera\Assets\STE Shared Data\VirtualEnvironment.txt", "")
    End Function

    ' This function Is used to edit the virtual environment "setting" for the test
    ' by editing a shared .txt file used in the Unity scene.
    Function EditVirtualEnvironment(virtualEnvironment As Common_Test_Variables.VirtualEnvironment)

    End Function

    ' This function Is used to set the target position for the test by editing a
    ' shared .txt file used in the Unity scene.
    Function InstTarget(targetPos As Double)

    End Function
End Module
Imports System

Module Drone_Spawn_Test
    Sub Main()
        BeginTest()

        TC01()
        TC02()
        TC03()

        EndTest()
    End Sub

    ' Spawn a Quad drone near the camera.
    Sub TC01()
        InstDrone(DroneType.Quad, 0, 5, 20)
    End Sub

    ' Spawn a Toad drone further downrange.
    Sub TC02()
        InstDrone(DroneType.Toad, 15, 8, 40)
    End Sub

    ' Spawn a BumbleBee drone at range, at night.
    Sub TC03()
        EditVirtualEnvironment(VirtualEnvironment.Night)
        InstDrone(DroneType.BumbleBee, -10, 12, 60)
    End Sub
End Module

Imports System

Module AVS_Detection_Test
    Sub Main()
        Try
            BeginTest()
            RunTestCase(AddressOf TC01)
            RunTestCase(AddressOf TC02)
        Finally
            EndTest()
        End Try
    End Sub

    Sub TC01()
        ' =====================================================================
        TraceTo("AVS-01")
        ' AVS-01: The drone shall be capable of detecting a target within 20
        ' meters.
        ' ---------------------------------------------------------------------
        TraceTo("AVS-02")
        ' AVS-02: The drone shall recognize and track a target with a
        ' presented cross-sectional area of 0.25 m^2.
        ' ---------------------------------------------------------------------
        TraceTo("AVS-03")
        ' AVS-03: The drone shall maintain at least 25 percent confidence
        ' while en route to the target.
        ' ---------------------------------------------------------------------
        TraceTo("SYS-03")
        ' SYS-03: The drone shall use onboard computation to autonomously
        ' detect its target​.
        ' =====================================================================

        Dim DetectionRangesMeters   As Double() = {1, 5, 10, 15, 20}
        Dim SweepOffsets()          As (X As Double, Y As Double) = {
            (0.0, 0.0),
            (0.25, 0.15),
            (-0.2, -0.3)
        }
        Dim DetectionTimeoutSeconds As Double = 10
        Dim ConfidenceWindowSeconds As Double = 3
        Dim MinimumConfidence       As Double = 0.25
        Dim EmptySceneWindowSeconds As Double = 3
        Dim DespawnSettleSeconds As Double = 2

        ' No drones have been spawned yet
        WriteLog("Empty scene: checking for false positives before any spawn")
        AssertNoTargetDetectedSince("drone", EmptySceneWindowSeconds, OutputMark())

        ' Spawn drones at various ranges and offsets
        For sweep As Integer = 0 To SweepOffsets.Length - 1
            Dim offset = SweepOffsets(sweep)
            For Each rangeMeters As Double In DetectionRangesMeters
                WriteLog($"Sweep {sweep + 1}/{SweepOffsets.Length}: drone at {rangeMeters} m")
                Dim mark As Integer = OutputMark()
                InstDroneAtRange(DroneType.Quad, rangeMeters, offset.X, offset.Y)
                AssertTargetDetectedSince("drone", DetectionTimeoutSeconds, mark)                           ' AVS-01, AVS-02, SYS-03
                AssertMinConfidenceAtLeastSince("drone", MinimumConfidence, ConfidenceWindowSeconds, mark)  ' AVS-03
                DespawnAllDrones()
                Wait(DespawnSettleSeconds)
            Next

            ' Check for false positives after the sweep
            WriteLog($"Empty scene: checking for false positives after sweep {sweep + 1}/{SweepOffsets.Length}")
            AssertNoTargetDetectedSince("drone", EmptySceneWindowSeconds, OutputMark())
        Next
    End Sub

    Sub TC02()
        ' =====================================================================
        TraceTo("AVS-04")
        ' AVS-04: The drone's onboard computing system shall process target
        ' detection within 33.333 ms of receiving the data from the onboard
        ' vision system.
        ' =====================================================================

        Dim MaxDetectMs           As Double = 33.333
        Dim LatencyTimeoutSeconds As Double = 30
        Dim SpawnSettleSeconds    As Double = 2
        Dim BatchCount            As Integer = 2
        Dim DroneOffsets()        As (X As Double, Y As Double) = {
            (0.0, 0.0),
            (0.3, 0.2),
            (-0.3, 0.2)
        }

        ' Check detect latency for multiple batches of drones
        For batch As Integer = 1 To BatchCount
            For droneIndex As Integer = 0 To DroneOffsets.Length - 1
                Dim offset = DroneOffsets(droneIndex)
                WriteLog($"Batch {batch}/{BatchCount}: spawning drone {droneIndex + 1}/{DroneOffsets.Length} ({droneIndex + 1} in scene) and checking detect latency")
                InstDroneAtRange(DroneType.Quad, 10, offset.X, offset.Y)
                Wait(SpawnSettleSeconds)
                Dim mark As Integer = OutputMark()
                AssertDetectLatencyBelowSince(MaxDetectMs, LatencyTimeoutSeconds, mark)                     ' AVS-04
            Next

            DespawnAllDrones()
            Wait(SpawnSettleSeconds)
        Next
    End Sub

    Private Sub InstDroneAtRange(droneType As DroneType, rangeMeters As Double, offX As Double, offY As Double)
        Dim length As Double = Math.Sqrt(offX * offX + offY * offY + 1)
        InstDrone(droneType,
                  rangeMeters * offX / length,
                  rangeMeters * offY / length,
                  rangeMeters / length)
    End Sub
End Module

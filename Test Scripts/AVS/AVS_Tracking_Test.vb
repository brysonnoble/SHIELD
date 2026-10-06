Imports System

Module AVS_Tracking_Test
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
        TraceTo("AVS-02")
        ' AVS-02: The drone shall recognize and track a target with a
        ' minimum span of 0.25 m.
        ' =====================================================================

        ' Targets sit at least 0.3 apart in offset units, about 280 px in a
        ' 1920x1080 frame; a drone at 2 m/s and 12 m moves about 5 px per
        ' frame, so a jump over MaxJumpPixels means an ID changed hands
        ' between targets.
        Dim TargetRangeMeters    As Double = 12
        Dim StillOffsets()       As (X As Double, Y As Double) = {
            (-0.4, 0.3),
            (0.0, 0.0),
            (0.4, -0.3)
        }
        Dim Lanes()              As (OffY As Double, StartOffX As Double, EndOffX As Double) = {
            (0.3, -0.6, 0.6),
            (0.0, 0.6, -0.6),
            (-0.3, -0.6, 0.6)
        }
        Dim SpeedMps             As Double = 2
        Dim TrackWindowSeconds   As Double = 10
        Dim MaxJumpPixels        As Double = 100
        Dim DespawnSettleSeconds As Double = 2

        ' Spawn three still drones at distinct positions, then watch their
        ' tracks
        Dim stillMark As Integer = OutputMark()
        For droneIndex As Integer = 0 To StillOffsets.Length - 1
            Dim offset = StillOffsets(droneIndex)
            WriteLog($"Still: spawning drone {droneIndex + 1}/{StillOffsets.Length} at offset ({offset.X}, {offset.Y}), {TargetRangeMeters} m")
            InstDroneAtRange(DroneType.Quad, TargetRangeMeters, offset.X, offset.Y)
        Next
        AssertTrackCountSince("drone", StillOffsets.Length, TrackWindowSeconds, stillMark)            ' AVS-02
        AssertNoTrackJumpsSince("drone", MaxJumpPixels, 0, stillMark)                                 ' AVS-02 (same window, already waited out)
        DespawnAllDrones()
        Wait(DespawnSettleSeconds)

        ' Fly three drones across the frame in parallel lanes, the middle one
        ' the opposite way, so they pass each other without overlapping, and
        ' watch their tracks for the whole flight
        Dim movingMark As Integer = OutputMark()
        For laneIndex As Integer = 0 To Lanes.Length - 1
            Dim lane = Lanes(laneIndex)
            WriteLog($"Moving: spawning drone {laneIndex + 1}/{Lanes.Length} in the lane at offset Y {lane.OffY}, flying from X {lane.StartOffX} to {lane.EndOffX} at {SpeedMps} m/s")
            InstDroneAtRange(DroneType.Quad,
                             TargetRangeMeters, lane.StartOffX, lane.OffY,
                             TargetRangeMeters, lane.EndOffX, lane.OffY,
                             SpeedMps)
        Next
        AssertTrackCountSince("drone", Lanes.Length, TrackWindowSeconds, movingMark)                  ' AVS-02
        AssertNoTrackJumpsSince("drone", MaxJumpPixels, 0, movingMark)                                ' AVS-02 (same window, already waited out)
    End Sub

    Sub TC02()
        ' =====================================================================
        TraceTo("AVS-03")
        ' AVS-03: The drone shall maintain at least 25 percent confidence
        ' while en route to the target.
        ' =====================================================================

        ' The detector drops anything under the 0.25 floor from its output,
        ' so a confidence dip shows up as a gap in the reports rather than a
        ' low reading - AssertTargetContinuouslyDetectedSince checks for it.
        Dim StartRangeMeters      As Double = 20
        Dim EndRangeMeters        As Double = 1
        Dim Approaches()          As (StartOffX As Double, StartOffY As Double, EndOffX As Double, EndOffY As Double) = {
            (0.0, 0.0, 0.0, 0.0),
            (-0.5, 0.25, 0.15, -0.1)
        }
        Dim SpeedMps              As Double = 2
        Dim AcquireTimeoutSeconds As Double = 3
        Dim MaxGapSeconds         As Double = 0.5
        Dim MinimumConfidence     As Double = 0.25
        Dim DespawnSettleSeconds  As Double = 2

        ' Fly a drone from 20 m in to 1 m along each approach
        For approachIndex As Integer = 0 To Approaches.Length - 1
            Dim approach = Approaches(approachIndex)
            Dim flightSeconds As Double = EstimatedFlightSeconds(
                FlightDistanceAtRange(StartRangeMeters, approach.StartOffX, approach.StartOffY,
                                      EndRangeMeters, approach.EndOffX, approach.EndOffY),
                SpeedMps)
            WriteLog($"Approach {approachIndex + 1}/{Approaches.Length}: drone flying from {StartRangeMeters} m to {EndRangeMeters} m at {SpeedMps} m/s (~{flightSeconds:F0} s)")

            Dim mark As Integer = OutputMark()
            InstDroneAtRange(DroneType.Quad,
                             StartRangeMeters, approach.StartOffX, approach.StartOffY,
                             EndRangeMeters, approach.EndOffX, approach.EndOffY,
                             SpeedMps)
            AssertTargetDetectedSince("drone", AcquireTimeoutSeconds, mark)                            ' acquired near 20 m, before the approach is underway
            AssertTargetContinuouslyDetectedSince("drone", MaxGapSeconds, flightSeconds, OutputMark()) ' AVS-03
            AssertMinConfidenceAtLeastSince("drone", MinimumConfidence, 0, mark)                       ' AVS-03 (whole flight, already waited out)
            DespawnAllDrones()
            Wait(DespawnSettleSeconds)
        Next
    End Sub
End Module

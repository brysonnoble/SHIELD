Imports System

' How reliably each target keeps one track ID (ByteTrack + the per-track
' Kalman filter), which is what lets SHIELD stay locked on to the target
' the operator engaged. AVS_Tracking_Test only checks the final count of
' IDs; this script measures, per target, how many times its ID changed
' (the pass/fail check: 0), how much of the time it carried its main ID,
' and how often the detector reported it at all, so a failure shows
' whether the tracker or the detector lost the target.
'
' Targets stay off the horizon (offset Y = 0), where the current model
' detects poorly, so this measures tracking rather than that.
Module AVS_ID_Persistence_Test
    Private Const MaxIdSwitches        As Integer = 0
    Private Const SettleSeconds        As Double = 1
    Private Const DespawnSettleSeconds As Double = 2

    Sub Main()
        Try
            BeginTest()
            RunTestCase(AddressOf TC01)
            RunTestCase(AddressOf TC02)
            RunTestCase(AddressOf TC03)
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

        ' One drone at a time, through motions that change how it looks:
        ' still, slow and fast passes (the faster it flies, the harder it
        ' banks), a head-on approach (its box grows ~7x), and a diagonal climb
        ' away from the camera.
        Dim HoverSeconds As Double = 8
        Dim Motions()    As (Name As String,
                             StartRange As Double, StartOffX As Double, StartOffY As Double,
                             EndRange As Double, EndOffX As Double, EndOffY As Double,
                             SpeedMps As Double) = {
            ("slow lateral pass at 12 m, 2 m/s", 12, -0.45, 0.12, 12, 0.45, 0.12, 2),
            ("fast lateral pass at 12 m, 6 m/s", 12, -0.45, 0.12, 12, 0.45, 0.12, 6),
            ("head-on approach from 20 m to 3 m, 3 m/s", 20, 0.1, 0.1, 3, 0.1, 0.1, 3),
            ("diagonal climb from 8 m to 16 m, 3 m/s", 8, -0.35, -0.2, 16, 0.35, 0.2, 3)
        }

        WriteLog($"Single drone: hovering at 12 m for {HoverSeconds} s")
        Dim hoverMark As Integer = OutputMark()
        InstDroneAtRange(DroneType.Quad, 12, 0.15, 0.12)
        AssertIdPersistenceSince("drone", {"hovering drone"}, Function(r) 0, MaxIdSwitches, HoverSeconds, hoverMark)   ' AVS-02
        DespawnAllDrones()
        Wait(DespawnSettleSeconds)

        For Each motion In Motions
            Dim flightSeconds As Double = EstimatedFlightSeconds(
                FlightDistanceAtRange(motion.StartRange, motion.StartOffX, motion.StartOffY,
                                      motion.EndRange, motion.EndOffX, motion.EndOffY),
                motion.SpeedMps)
            WriteLog($"Single drone: {motion.Name} (~{flightSeconds:F0} s)")
            Dim mark As Integer = OutputMark()
            InstDroneAtRange(DroneType.Quad,
                             motion.StartRange, motion.StartOffX, motion.StartOffY,
                             motion.EndRange, motion.EndOffX, motion.EndOffY,
                             motion.SpeedMps)
            AssertIdPersistenceSince("drone", {motion.Name}, Function(r) 0, MaxIdSwitches,
                                     flightSeconds + SettleSeconds, mark)                                       ' AVS-02
            DespawnAllDrones()
            Wait(DespawnSettleSeconds)
        Next
    End Sub

    Sub TC02()
        ' =====================================================================
        TraceTo("AVS-02")
        ' AVS-02: The drone shall recognize and track a target with a
        ' minimum span of 0.25 m.
        ' =====================================================================

        ' Three drones at once in parallel lanes, the middle one flying the
        ' other way. Each report is credited to the drone whose lane (pixel
        ' row) it is nearest; at 12 m a drone's box is ~50 px tall and the
        ' lanes are 160-250 px apart.
        Dim RangeMeters  As Double = 12
        Dim SpeedMps     As Double = 2
        Dim LaneHalfPx   As Double = 70
        Dim Lanes()      As (Name As String, OffY As Double, StartOffX As Double, EndOffX As Double) = {
            ("upper lane drone", 0.2, -0.45, 0.45),
            ("middle lane drone", 0.06, 0.45, -0.45),
            ("lower lane drone", -0.15, -0.45, 0.45)
        }

        Dim flightSeconds As Double = EstimatedFlightSeconds(
            FlightDistanceAtRange(RangeMeters, -0.45, 0, RangeMeters, 0.45, 0), SpeedMps)
        Dim laneRows As Double() = Lanes.Select(Function(l) FRAME_HEIGHT_PX / 2 - l.OffY * FOCAL_PX).ToArray()
        Dim byLane As Func(Of TargetReport, Integer) =
            Function(r)
                For i As Integer = 0 To laneRows.Length - 1
                    If Math.Abs(r.Y - laneRows(i)) <= LaneHalfPx Then
                        Return i
                    End If
                Next
                Return -1
            End Function

        WriteLog($"Three drones in parallel lanes at {RangeMeters} m, {SpeedMps} m/s (~{flightSeconds:F0} s)")
        Dim mark As Integer = OutputMark()
        For Each lane In Lanes
            InstDroneAtRange(DroneType.Quad,
                             RangeMeters, lane.StartOffX, lane.OffY,
                             RangeMeters, lane.EndOffX, lane.OffY,
                             SpeedMps)
        Next
        AssertIdPersistenceSince("drone", Lanes.Select(Function(l) l.Name).ToArray(), byLane, MaxIdSwitches,
                                 flightSeconds + SettleSeconds, mark)                                           ' AVS-02
    End Sub

    Sub TC03()
        ' =====================================================================
        TraceTo("AVS-02")
        ' AVS-02: The drone shall recognize and track a target with a
        ' minimum span of 0.25 m.
        ' =====================================================================

        ' A near drone crosses in front of a far one in the same lane, so it
        ' hides the far one for a moment - the brief obstruction SHIELD is
        ' meant to track through. Their speeds are in proportion to their
        ' ranges, so they cross the middle of the frame at about the same
        ' time. Each report is credited by its stereo range; one with no
        ' range is skipped.
        Dim NearRange   As Double = 8
        Dim FarRange    As Double = 14
        Dim NearSpeed   As Double = 2
        Dim FarSpeed    As Double = NearSpeed * FarRange / NearRange
        Dim OffY        As Double = 0.1
        Dim SplitRange  As Double = (NearRange + FarRange) / 2
        Dim byRange As Func(Of TargetReport, Integer) =
            Function(r)
                If r.Distance Is Nothing Then
                    Return -1
                End If
                Return If(r.Distance.Value < SplitRange, 0, 1)
            End Function

        Dim flightSeconds As Double = Math.Max(
            EstimatedFlightSeconds(FlightDistanceAtRange(NearRange, -0.4, OffY, NearRange, 0.4, OffY), NearSpeed),
            EstimatedFlightSeconds(FlightDistanceAtRange(FarRange, 0.4, OffY, FarRange, -0.4, OffY), FarSpeed))
        WriteLog($"Crossing: drone at {NearRange} m passing in front of a drone at {FarRange} m (~{flightSeconds:F0} s)")
        Dim mark As Integer = OutputMark()
        InstDroneAtRange(DroneType.Quad, NearRange, -0.4, OffY, NearRange, 0.4, OffY, NearSpeed)
        InstDroneAtRange(DroneType.Quad, FarRange, 0.4, OffY, FarRange, -0.4, OffY, FarSpeed)
        AssertIdPersistenceSince("drone", {$"near drone ({NearRange} m)", $"far drone ({FarRange} m)"}, byRange,
                                 MaxIdSwitches, flightSeconds + SettleSeconds, mark)                           ' AVS-02
    End Sub
End Module

Imports System

Module AVS_Edge_Case_Test
    Sub Main()
        Try
            BeginTest()
            RunTestCase(AddressOf TC01)
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
        ' minimum span of 0.25 m.
        ' =====================================================================

        Dim EdgeRangeMeters         As Double = 8
        Dim FrameEdges()            As (Name As String, OffX As Double, OffY As Double) = {
            ("left", -FRAME_EDGE_OFFSET_X, 0.0),
            ("right", FRAME_EDGE_OFFSET_X, 0.0),
            ("top", 0.0, FRAME_EDGE_OFFSET_Y),
            ("bottom", 0.0, -FRAME_EDGE_OFFSET_Y)
        }
        Dim CloseRangeMeters        As Double = 0.5
        Dim FlyInStartOffX          As Double = -(FRAME_EDGE_OFFSET_X + 0.15)
        Dim FlyInSpeedMps           As Double = 2
        Dim DetectionTimeoutSeconds As Double = 10
        Dim TrackWindowSeconds      As Double = 3
        Dim DespawnSettleSeconds    As Double = 2

        ' Spawn a drone centered on each frame edge, so half of it is out of
        ' frame
        For Each edge In FrameEdges
            WriteLog($"Frame edge: drone half outside the {edge.Name} edge at {EdgeRangeMeters} m")
            Dim mark As Integer = OutputMark()
            InstDroneAtRange(DroneType.Quad, EdgeRangeMeters, edge.OffX, edge.OffY)
            AssertTargetDetectedSince("drone", DetectionTimeoutSeconds, mark)                          ' AVS-01
            AssertTrackCountSince("drone", 1, TrackWindowSeconds, mark)                                ' AVS-02
            DespawnAllDrones()
            Wait(DespawnSettleSeconds)
        Next

        ' Spawn a drone close enough to fill most of the frame. The camera's
        ' near clip plane is 0.3 m, so its nearest parts may be cut off too.
        WriteLog($"Close range: drone at {CloseRangeMeters} m")
        Dim closeMark As Integer = OutputMark()
        InstDroneAtRange(DroneType.Quad, CloseRangeMeters, 0, 0)
        AssertTargetDetectedSince("drone", DetectionTimeoutSeconds, closeMark)                         ' AVS-01
        AssertTrackCountSince("drone", 1, TrackWindowSeconds, closeMark)                               ' AVS-02
        DespawnAllDrones()
        Wait(DespawnSettleSeconds)

        ' Fly a drone in from fully outside the left edge to the center of
        ' the frame - it should be picked up as it enters and keep one ID
        ' all the way in
        Dim flyInSeconds As Double = EstimatedFlightSeconds(
            FlightDistanceAtRange(EdgeRangeMeters, FlyInStartOffX, 0, EdgeRangeMeters, 0, 0),
            FlyInSpeedMps)
        WriteLog($"Fly-in: drone entering from outside the left edge at {EdgeRangeMeters} m, {FlyInSpeedMps} m/s (~{flyInSeconds:F0} s)")
        Dim flyInMark As Integer = OutputMark()
        InstDroneAtRange(DroneType.Quad,
                         EdgeRangeMeters, FlyInStartOffX, 0,
                         EdgeRangeMeters, 0, 0,
                         FlyInSpeedMps)
        AssertTargetDetectedSince("drone", flyInSeconds, flyInMark)                                    ' AVS-01
        AssertTrackCountSince("drone", 1, flyInSeconds, flyInMark)                                     ' AVS-02
    End Sub
End Module

Imports System

' Requirement source: "SHIELD RTM & Tracking", sheet AVS. Both requirements
' here are about the drone <-> Ground Control System link (gcs_link.py).
' This script connects to the pipeline's link as a second GCS, alongside
' the GCS window, and sends the same commands the window's buttons do.
Module AVS_GCS_Link_Test
    Private Const TargetRangeMeters       As Double = 8
    Private Const SpawnSettleSeconds      As Double = 2
    Private Const DetectionTimeoutSeconds As Double = 10
    Private Const LogTimeoutSeconds       As Double = 5
    ' A track id no target in the scene can have.
    Private Const UnknownTrackId          As Integer = 999

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
        TraceTo("AVS-05")
        ' AVS-05: The drone shall receive engage and abort commands from the
        ' Ground Control System.
        ' =====================================================================

        Dim trackId As Integer? = SpawnTargetAndGetTrackId()
        If trackId Is Nothing Then
            Return
        End If
        Dim id As Integer = trackId.Value

        ' Engage the target, then abort
        AssertDroneState("TRACKING")
        AssertGcsReply($"ENGAGE {id}", $"ACK ENGAGE {id}")
        AssertDroneState("ENGAGED", id.ToString())
        AssertLogMatches($"ENGAGE accepted for track T-{id:00}\b", LogTimeoutSeconds)

        AssertGcsReply("ABORT", "ACK ABORT")
        AssertDroneState("TRACKING")
        AssertLogMatches($"ABORT - engagement of T-{id:00} cancelled", LogTimeoutSeconds)

        ' The drone checks the command itself rather than trusting the GCS:
        ' it refuses a target it isn't tracking and stays as it was
        AssertGcsReply($"ENGAGE {UnknownTrackId}", $"NAK ENGAGE {UnknownTrackId} no such target")
        AssertDroneState("TRACKING")
    End Sub

    Sub TC02()
        ' =====================================================================
        TraceTo("AVS-05-01")
        ' AVS-05-01: The drone shall send a heartbeat to the Ground Control
        ' System at a rate of 1 Hz.
        ' =====================================================================

        Dim HeartbeatHz           As Double = 1
        Dim ListenSeconds         As Double = 10.5
        ' Averaged over ~10 beats, the rate itself should be close to exact
        Dim MeanToleranceSeconds  As Double = 0.02
        ' Any one beat may land early or late, but never so far off that a
        ' GCS waiting for it would notice
        Dim MaxDeviationSeconds   As Double = 0.25

        ' The heartbeat reports the drone's state and a live video link
        AssertLogMatches("HEARTBEAT seq=\d+ state=TRACKING target=- frames=\d+ video=ok", LogTimeoutSeconds)

        ' With a target in view, so the detector is under load
        InstDroneAtRange(DroneType.Quad, TargetRangeMeters, 0, 0)
        Wait(SpawnSettleSeconds)
        AssertHeartbeatRate(HeartbeatHz, ListenSeconds, MeanToleranceSeconds, MaxDeviationSeconds)
    End Sub

    Sub TC03()
        ' =====================================================================
        TraceTo("AVS-05")
        ' AVS-05: The drone shall receive engage and abort commands from the
        ' Ground Control System - here the two overrides the client asked for
        ' (09/28): Land Now, and Shut Down (cut the motors).
        ' =====================================================================

        Dim trackId As Integer? = SpawnTargetAndGetTrackId()
        If trackId Is Nothing Then
            Return
        End If
        Dim id As Integer = trackId.Value

        ' Land Now cancels an engagement in progress, and the drone won't
        ' engage again while landing
        AssertGcsReply($"ENGAGE {id}", $"ACK ENGAGE {id}")
        AssertGcsReply("LAND", "ACK LAND")
        AssertDroneState("LANDING")
        AssertLogMatches("LAND NOW - engagement cancelled", LogTimeoutSeconds)
        AssertGcsReply($"ENGAGE {id}", $"NAK ENGAGE {id} drone is landing")

        ' Shut Down overrides a landing, and nothing is accepted after it
        AssertGcsReply("SHUTDOWN", "ACK SHUTDOWN")
        AssertDroneState("SHUTDOWN")
        AssertLogMatches("SHUT DOWN - engagement cancelled, motors cut", LogTimeoutSeconds)
        AssertGcsReply("LAND", "NAK LAND motors are shut down")
        AssertGcsReply($"ENGAGE {id}", $"NAK ENGAGE {id} motors are shut down")
    End Sub

    ' Spawns one quad in the middle of the frame and returns the track id
    ' the pipeline gives it, or Nothing (after logging a FAIL) if it's never
    ' detected - every engage command needs a real track to point at.
    Private Function SpawnTargetAndGetTrackId() As Integer?
        Dim mark As Integer = OutputMark()
        InstDroneAtRange(DroneType.Quad, TargetRangeMeters, 0, 0)
        Wait(SpawnSettleSeconds)
        Dim m = WaitForLogMatchSince("target id=(\d+) class=drone\b", DetectionTimeoutSeconds, mark)
        If m Is Nothing Then
            Fail("a drone track to engage", "not detected")
            Return Nothing
        End If
        Pass()
        Return Integer.Parse(m.Groups(1).Value)
    End Function
End Module

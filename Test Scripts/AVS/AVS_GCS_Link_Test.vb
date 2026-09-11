Imports System

' Requirement source: "SHIELD RTM & Tracking", sheet AVS. Both requirements
' here are about the drone <-> Ground Control System link, which doesn't
' have a scriptable command channel yet (engage/abort is a GCS-side Tkinter
' button click - see gcs_ui.py's _on_engage_click - not a TCP command STE
' can send like EditVirtualEnvironment/InstDrone).
Module AVS_GCS_Link_Test
    Sub Main()
        Try
            BeginTest()
            RunTestCase(AddressOf TC01)
            RunTestCase(AddressOf TC02)
        Finally
            EndTest()
        End Try
    End Sub

    ' AVS-05: The drone shall receive engage and abort commands from the
    ' Ground Control System.
    Sub TC01()
        ' TODO: once engage/abort commands travel over an actual GCS<->drone
        ' link (today it's a local button click in gcs_ui.py with "no
        ' effector wired up yet"), add a scene/GCS command this script can
        ' send and a corresponding "ENGAGE issued .../ABORT" log line to
        ' assert on, the same way AssertTargetDetected checks detector
        ' output today.
        TraceTo("AVS-05")
    End Sub

    ' AVS-05-01: The drone shall send a heartbeat to the Ground Control
    ' System at a rate of 1 Hz.
    Sub TC02()
        ' TODO: video_source.py's UnityStreamSource currently treats each
        ' frame request as an implicit liveness check rather than emitting a
        ' distinct heartbeat message (see its "no dedicated heartbeat
        ' message exists on the wire yet" comment). Once a real 1 Hz
        ' heartbeat is implemented, assert on its log line's timing here
        ' (e.g. via WaitForLogMatch called repeatedly and checking the
        ' interval between matches).
        TraceTo("AVS-05-01")
    End Sub
End Module

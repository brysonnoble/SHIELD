Imports System

' Requirement source: "SHIELD RTM & Tracking", sheet AVS. Each TCxx() is
' independent - it spawns whatever target it needs itself rather than
' relying on a drone a previous test case spawned. TestCaseEnd() despawns
' every drone before the next test case's TestCaseBegin() runs, so nothing
' would even be left to rely on.
Module AVS_Detection_Test
    Sub Main()
        Try
            BeginTest()
            RunTestCase(AddressOf TC01)
            RunTestCase(AddressOf TC02)
            RunTestCase(AddressOf TC03)
            RunTestCase(AddressOf TC04)
        Finally
            EndTest()
        End Try
    End Sub

    ' AVS-01: The drone shall be capable of detecting a target within 20 meters.
    Sub TC01()
        TraceTo("AVS-01")
        ' Also satisfies SYS-03 (autonomous detection) at the system level.
        TraceTo("SYS-03")
        ' Spawn a target within 20m and confirm the detector reports it.
        InstDrone(DroneType.Quad, 0, 5, 19)
        AssertTargetDetected("drone", 20)
    End Sub

    ' AVS-02: The drone shall recognize and track a target with a presented
    ' cross-sectional area of 0.25 m^2.
    Sub TC02()
        TraceTo("AVS-02")
        ' Spawn the standard Quad prefab (modeled at ~0.25 m^2) and confirm
        ' it's recognized. This isn't a controlled measurement of the
        ' 0.25 m^2 boundary itself.
        ' TODO: for a rigorous boundary check, spawn a purpose-scaled target
        ' (or fall back to the training pipeline's dataset-level
        ' mAP/precision-recall evaluation) instead of the Quad prefab's
        ' fixed size.
        InstDrone(DroneType.Quad, 0, 5, 15)
        AssertTargetDetected("drone", 20)
    End Sub

    ' AVS-03: The drone shall maintain at least 25 percent confidence while
    ' en route to the target.
    Sub TC03()
        TraceTo("AVS-03")
        ' Spawn a target and confirm every confidence reading for it over
        ' the observation window meets gcs_ui.py's ENGAGE_CONFIDENCE_FLOOR
        ' (the same 0.25 threshold).
        InstDrone(DroneType.Quad, 0, 5, 15)
        AssertMinConfidenceAtLeast("drone", 0.25, 5)
    End Sub

    ' AVS-04: The drone's onboard computing system shall process target
    ' detection within 33.333 ms of receiving the data from the onboard
    ' vision system.
    Sub TC04()
        TraceTo("AVS-04")
        ' No target needs to be spawned - the pipeline runs detection on
        ' every frame regardless of whether anything is in view.
        AssertDetectLatencyBelow(33.333, 30)
    End Sub
End Module

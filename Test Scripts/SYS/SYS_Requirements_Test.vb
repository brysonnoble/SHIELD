Imports System

' Requirement source: "SHIELD RTM & Tracking", sheet SYS.
Module SYS_Requirements_Test
    Sub Main()
        Try
            BeginTest()
            RunTestCase(AddressOf TC01)
            RunTestCase(AddressOf TC02)
            RunTestCase(AddressOf TC03)
            RunTestCase(AddressOf TC04)
            RunTestCase(AddressOf TC05)
            RunTestCase(AddressOf TC06)
            RunTestCase(AddressOf TC07)
            RunTestCase(AddressOf TC08)
        Finally
            EndTest()
        End Try
    End Sub

    ' SYS-03: The drone shall use onboard computation to autonomously
    ' detect its target.
    Sub TC01()
        TraceTo("SYS-03")
        ' Spawn a target and confirm the pipeline reports it without any
        ' manual intervention - the system-level restatement of what
        ' AVS-01/AVS-02 check in more detail (see Test Scripts\AVS).
        InstDrone(DroneType.Quad, 0, 5, 15)
        AssertTargetDetected("drone", 20)
    End Sub

    ' SYS-01: The drone shall comply with small Unmanned Aircraft Systems
    ' Regulations from FAA part 107.
    Sub TC02()
        ' TODO: once test flights start, verify against a Part 107 checklist
        ' (licensure, registration, airspace/altitude rules) - not an
        ' emulation script.
        TraceTo("SYS-01")
    End Sub

    ' SYS-02: The drone shall carry a vision system dedicated to enabling
    ' onboard-edge computing.
    Sub TC03()
        ' TODO: once hardware exists, verify by physical inspection/test
        ' flight with the vision system attached - not an emulation script.
        TraceTo("SYS-02")
    End Sub

    ' SYS-04: The drone's bill of materials shall cost under $TBR.
    Sub TC04()
        ' TODO: once a cost limit is set (currently TBR) and a BOM exists,
        ' check the BOM total against it - not an emulation script.
        TraceTo("SYS-04")
    End Sub

    ' SYS-05: The drone shall have a top speed of at least TBR.
    Sub TC05()
        ' TODO: once a flight-capable airframe and a speed target (currently
        ' TBR) exist, measure top speed in a test flight.
        TraceTo("SYS-05")
    End Sub

    ' SYS-06: The drone shall be able to fly in winds up to 4.5 +/- 1 m/s.
    Sub TC06()
        ' TODO: once a flight-capable airframe exists, run the box-fan hover
        ' test described in the RTM's verification strategy.
        TraceTo("SYS-06")
    End Sub

    ' SYS-07: The drone shall have an endurance of at least TBR minutes.
    Sub TC07()
        ' TODO: once a flight-capable airframe and an endurance target
        ' (currently TBR) exist, time a hover/flight to depletion.
        TraceTo("SYS-07")
    End Sub

    ' SYS-08: The drone shall be capable of returning to a designated area
    ' of TBR.
    Sub TC08()
        ' TODO: once return-to-base logic and a flight-capable airframe
        ' exist, fly a mission profile and confirm it recovers to the
        ' designated area.
        TraceTo("SYS-08")
    End Sub
End Module

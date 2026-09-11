Imports System

' Requirement source: "SHIELD RTM & Tracking", sheet STR. All physical
' structural properties with no corresponding software - the Unity/Python
' emulation harness has nothing to check here. (STR-05 exists as a row in
' the RTM but has no requirement text yet, so there's no TC for it here.)
Module STR_Requirements_Test
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

    ' STR-01: The drone shall not weigh over 55 pounds, including
    ' batteries, payloads, sensors, wiring, fasteners, and all other
    ' installed components.
    Sub TC01()
        ' TODO: once the drone is assembled, weigh it (Inspection/Test) -
        ' not an emulation script.
        TraceTo("STR-01")
    End Sub

    ' STR-02: The drone shall have a Factor of Safety of at least 1.5.
    Sub TC02()
        ' TODO: once maximum expected loads are known, verify by
        ' FEA/calculation against them - not an emulation script.
        TraceTo("STR-02")
    End Sub

    ' STR-03: The payload structure shall securely retain all installed
    ' components during flight and landing conditions.
    Sub TC03()
        ' TODO: once the payload structure is built, verify by
        ' inspection/test (assembly check, shake/rattle test) - not an
        ' emulation script.
        TraceTo("STR-03")
    End Sub

    ' STR-04: The drone shall be able to maintain flight with a minimum
    ' capacity of 5 pounds.
    Sub TC04()
        ' TODO: once a flight-capable airframe exists, verify by test
        ' flight with a 5-pound payload - not an emulation script.
        TraceTo("STR-04")
    End Sub
End Module

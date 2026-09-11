Imports System

' Requirement source: "SHIELD RTM & Tracking", sheet AER. Both requirements
' are airframe properties with no corresponding software in this repo and
' no verification method recorded yet in the RTM - the Unity/Python
' emulation harness has nothing to check here.
Module AER_Requirements_Test
    Sub Main()
        Try
            BeginTest()
            RunTestCase(AddressOf TC01)
            RunTestCase(AddressOf TC02)
        Finally
            EndTest()
        End Try
    End Sub

    ' AER-01: The drone shall have a thrust to weight ratio of 4:1.
    Sub TC01()
        ' TODO: once a propulsion system and airframe weight exist, verify
        ' by bench-testing thrust against measured weight (Analysis/Test) -
        ' not an emulation script.
        TraceTo("AER-01")
    End Sub

    ' AER-02: The drone shall have a maximum pitch angle of at least 25
    ' degrees.
    Sub TC02()
        ' TODO: once a flight-capable airframe exists, verify by test
        ' flight or flight-dynamics analysis - not an emulation script.
        TraceTo("AER-02")
    End Sub
End Module

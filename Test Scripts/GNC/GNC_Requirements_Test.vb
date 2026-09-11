Imports System

' Requirement source: "SHIELD RTM & Tracking", sheet GNC. None of these are
' implemented in software yet - this repo's pipeline stops at detection/
' tracking (see the root README's architecture diagram); there's no
' guidance loop, state-space model, or EKF/UKF estimator to exercise.
Module GNC_Requirements_Test
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
            RunTestCase(AddressOf TC09)
        Finally
            EndTest()
        End Try
    End Sub

    ' GNC-01: The guidance loop shall accept relative target state at a
    ' minimum update rate of TBR Hz.
    Sub TC01()
        ' TODO: once a guidance loop exists, drive it with synthetic
        ' relative target state and confirm it recalculates at the required
        ' rate.
        TraceTo("GNC-01")
    End Sub

    ' GNC-02: The guidance law shall be derived from a state-space
    ' relative-motion dynamic representation.
    Sub TC02()
        ' TODO: once a guidance law is derived and documented, verify by
        ' design review/inspection - not an emulation script.
        TraceTo("GNC-02")
    End Sub

    ' GNC-03: The guidance system shall accept autonomous guidance commands
    ' from the onboard computing system.
    Sub TC03()
        ' TODO: once a guidance system exists, feed it autonomous commands
        ' from the detection/tracking pipeline and confirm it accepts and
        ' acts on them.
        TraceTo("GNC-03")
    End Sub

    ' GNC-04: The drone shall not engage a target if the target escapes the
    ' line of sight for more than 30 frames.
    Sub TC04()
        ' object_detection.py already drops a track's Kalman filter after
        ' config.TRACK_MAX_AGE (30) missed frames, but there's no
        ' engage/abort command channel this script can drive (see
        ' AVS_GCS_Link_Test.vb's AVS-05 TODO) and no way to occlude a
        ' spawned drone without destroying it outright - DroneSpawner can
        ' spawn and despawn, but nothing moves or hides a drone while it's
        ' still "in play" (see the root README's "Known limitations").
        ' TODO: once a scene command can occlude a target (rather than
        ' despawning it) and an engage command channel exists, spawn a
        ' target, occlude it for more than 30 frames, and assert no ENGAGE
        ' is issued (or that its track id is dropped rather than coasted
        ' indefinitely).
        TraceTo("GNC-04")
    End Sub

    ' GNC-05: The guidance system shall incorporate the interceptor's
    ' characterized actuator response lag (tau), measured via step-response
    ' bench test, into a guidance law formulation for the terminal phase.
    Sub TC05()
        ' TODO: once a step-response bench test and a guidance law
        ' formulation exist, verify the calculation - not an emulation
        ' script.
        TraceTo("GNC-05")
    End Sub

    ' GNC-06: The relative-state estimator shall be formally derived from
    ' the vehicle and sensor dynamics models (EKF/UKF formulation).
    Sub TC06()
        ' TODO: once an estimator is derived and documented, verify by
        ' design review/inspection - not an emulation script.
        TraceTo("GNC-06")
    End Sub

    ' GNC-07: A comparative trade study shall be performed and documented
    ' between classical proportional navigation and the derived optimal
    ' guidance law, quantifying miss-distance and control-effort
    ' differences across the full characterized actuator-lag - Monte Carlo
    ' simulation from SITL.
    Sub TC07()
        ' TODO: once both guidance laws and a SITL Monte Carlo setup exist,
        ' run the trade study - not an emulation script.
        TraceTo("GNC-07")
    End Sub

    ' GNC-8: The guidance system shall maintain performance under wind
    ' disturbance up to 4.5 +/- 1 m/s.
    Sub TC08()
        ' TODO: once a guidance system and a wind-disturbance model (SITL
        ' or physical, per SYS-06) exist, verify performance under that
        ' disturbance.
        TraceTo("GNC-8")
    End Sub

    ' GNC-9: The guidance system's commanded acceleration shall be
    ' saturated at or below the interceptor's measured maximum sustained
    ' lateral acceleration.
    Sub TC09()
        ' TODO: once a guidance system and a measured max lateral
        ' acceleration (per SYS-05) exist, verify the commanded-acceleration
        ' saturation.
        TraceTo("GNC-9")
    End Sub
End Module

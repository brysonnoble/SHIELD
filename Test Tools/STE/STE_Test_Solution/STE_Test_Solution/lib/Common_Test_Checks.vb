Imports System.Globalization
Imports System.Text.RegularExpressions
Imports System.Threading

' Pass/fail assertions a test case can make against the current test's
' captured Python console output (PythonOutputSnapshot(), populated by
' BeginTest() in Common_Test_Functions.vb - the raw lines themselves go to
' that run's separate "_python_output.log", not the main log). Call these
' after TraceTo() so a failure lands under the requirement it was checking.
Public Module Common_Test_Checks
    ' Logs "Output Check <n>: PASS", <n> being a 1-based counter of checks
    ' made so far in the current test case (reset by TestCaseBegin()).
    Public Sub Pass()
        Dim n As Integer = Common_Test_Functions.NextCheckNumber()
        Common_Test_Functions.WriteLog($"Output Check {n}: PASS")
    End Sub

    ' Logs "Output Check <n>: **FAIL** Expected Value: <expectedValue>,
    ' Actual Value: <actualValue>" and marks the current test case failed
    ' (for TestCaseEnd()/EndTest()'s summary), then returns normally -
    ' unlike an ABORT (an unhandled exception), a FAIL does NOT stop the
    ' rest of the calling TCxx() from running. That's deliberate: a test
    ' case that makes several checks (e.g. one per drone it spawns) should
    ' still run every remaining check and report all of their results,
    ' rather than stopping at the first failure and leaving the rest
    ' unverified. RunTestCase() still moves on to the script's next test
    ' case only after the current TCxx() actually returns (whether it ended
    ' in PASS or FAIL) or throws (ABORT) - a FAIL alone never advances past
    ' the test case it happened in.
    Public Sub Fail(expectedValue As Object, actualValue As Object)
        Dim n As Integer = Common_Test_Functions.NextCheckNumber()
        Common_Test_Functions.WriteLog($"Output Check {n}: **FAIL** Expected Value: {expectedValue}, Actual Value: {actualValue}")
        Common_Test_Functions.MarkCurrentTestCaseFailed()
    End Sub

    ' How many lines the current test case's Python process has printed so
    ' far. Pass the returned value as a later check's sinceLine argument to
    ' scope that check to output printed from this moment on, ignoring
    ' everything before it. Needed whenever a single test case changes the
    ' scene more than once (spawn, despawn, spawn again): without it, a
    ' detection from an earlier part of the test case still satisfies
    ' AssertTargetDetected, and no "nothing is detected now" check could
    ' ever hold. Take the mark AFTER the scene change has settled - frames
    ' already in the pipeline when the command was sent still print their
    ' detections a moment later (see Common_Test_Functions.Wait()).
    Public Function OutputMark() As Integer
        Return Common_Test_Functions.PythonOutputSnapshot().Length
    End Function

    ' Polls the captured Python output every 200ms until a line matching
    ' pattern (a .NET regex) appears or timeoutSeconds elapses, and returns
    ' the match (or Nothing on timeout). Only looks at lines newer than the
    ' last check each poll, so a slow-to-appear match still isn't missed.
    Public Function WaitForLogMatch(pattern As String, timeoutSeconds As Double) As Match
        Return WaitForLogMatchSince(pattern, timeoutSeconds, 0)
    End Function

    ' WaitForLogMatch restricted to output printed since an OutputMark() -
    ' lines before sinceLine are never examined, so an earlier part of the
    ' same test case can't satisfy the match.
    Public Function WaitForLogMatchSince(pattern As String, timeoutSeconds As Double, sinceLine As Integer) As Match
        Dim regex As New Regex(pattern)
        Dim deadline As DateTime = DateTime.Now.AddSeconds(timeoutSeconds)
        Dim checkedUpTo As Integer = sinceLine
        Do
            Dim lines As String() = Common_Test_Functions.PythonOutputSnapshot()
            For i As Integer = checkedUpTo To lines.Length - 1
                Dim m As Match = regex.Match(lines(i))
                If m.Success Then
                    Return m
                End If
            Next
            checkedUpTo = Math.Max(checkedUpTo, lines.Length)
            If DateTime.Now >= deadline Then
                Exit Do
            End If
            Thread.Sleep(200)
        Loop
        Return Nothing
    End Function

    ' Like WaitForLogMatch, but returns the MOST RECENT match instead of the
    ' first - for a rolling reading like "--profile"'s per-frame timing
    ' line, where an early sample (e.g. a one-off cold-start/model-warm-up
    ' spike right after TestCaseBegin()'s pipeline relaunch) shouldn't
    ' outweigh a later, steadier one just because it happened to be printed
    ' first. Still waits up to timeoutSeconds if no match exists yet.
    Public Function WaitForLatestLogMatch(pattern As String, timeoutSeconds As Double) As Match
        Return WaitForLatestLogMatchSince(pattern, timeoutSeconds, 0)
    End Function

    ' WaitForLatestLogMatch restricted to output printed since an
    ' OutputMark() - lines before sinceLine are never examined, so a reading
    ' left over from before a scene change (e.g. a still-warm profile
    ' average taken with fewer targets in frame) can't satisfy a check meant
    ' for the scene as it is now. Without this, the plain version would
    ' return an already-existing match on its very first poll rather than
    ' waiting for a fresh one to appear after the change.
    Public Function WaitForLatestLogMatchSince(pattern As String, timeoutSeconds As Double, sinceLine As Integer) As Match
        Dim regex As New Regex(pattern)
        Dim deadline As DateTime = DateTime.Now.AddSeconds(timeoutSeconds)
        Do
            Dim lines As String() = Common_Test_Functions.PythonOutputSnapshot()
            Dim latest As Match = Nothing
            For i As Integer = sinceLine To lines.Length - 1
                Dim m As Match = regex.Match(lines(i))
                If m.Success Then
                    latest = m
                End If
            Next
            If latest IsNot Nothing Then
                Return latest
            End If
            If DateTime.Now >= deadline Then
                Exit Do
            End If
            Thread.Sleep(200)
        Loop
        Return Nothing
    End Function

    ' Fails unless a captured Python console line matches pattern within
    ' timeoutSeconds.
    Public Sub AssertLogMatches(pattern As String, timeoutSeconds As Double)
        Dim m As Match = WaitForLogMatch(pattern, timeoutSeconds)
        If m Is Nothing Then
            Fail(pattern, "no match")
        Else
            Pass()
        End If
    End Sub

    ' Fails unless the detector reports a target of the given class (as
    ' printed by object_detection.py's "target id=... class=... conf=..."
    ' line) within timeoutSeconds - i.e. something was actually spawned and
    ' recognized, not just that a drone exists in the scene.
    Public Sub AssertTargetDetected(className As String, timeoutSeconds As Double)
        AssertTargetDetectedSince(className, timeoutSeconds, 0)
    End Sub

    ' AssertTargetDetected restricted to output printed since an
    ' OutputMark() - for a test case that spawns, despawns and spawns again,
    ' where a detection of the drone this test case spawned two spawns ago
    ' would otherwise pass this check without the current target ever having
    ' been seen.
    Public Sub AssertTargetDetectedSince(className As String, timeoutSeconds As Double, sinceLine As Integer)
        Dim m As Match = WaitForLogMatchSince($"class={Regex.Escape(className)}\b", timeoutSeconds, sinceLine)
        If m Is Nothing Then
            Fail(className, "not detected")
        Else
            Pass()
        End If
    End Sub

    ' The inverse of AssertTargetDetectedSince: fails if ANY target of the
    ' given class is reported over the next durationSeconds, and passes only
    ' if the window stays clean the whole way through. For checking the
    ' detector doesn't report a target in an empty scene (a false positive),
    ' so it always waits out the full window rather than returning early.
    ' sinceLine must be an OutputMark() taken after the scene was actually
    ' emptied and the in-flight frames from before that had time to print -
    ' otherwise this fails on a stale detection of a drone that's already
    ' gone rather than on a real false positive.
    Public Sub AssertNoTargetDetectedSince(className As String, durationSeconds As Double, sinceLine As Integer)
        Dim regex As New Regex($"class={Regex.Escape(className)}\b")
        Dim deadline As DateTime = DateTime.Now.AddSeconds(durationSeconds)
        Dim checkedUpTo As Integer = sinceLine
        Do
            Dim lines As String() = Common_Test_Functions.PythonOutputSnapshot()
            For i As Integer = checkedUpTo To lines.Length - 1
                If regex.IsMatch(lines(i)) Then
                    Fail($"no {className} detections", lines(i))
                    Return
                End If
            Next
            checkedUpTo = Math.Max(checkedUpTo, lines.Length)
            If DateTime.Now >= deadline Then
                Exit Do
            End If
            Thread.Sleep(200)
        Loop
        Pass()
    End Sub

    ' Collects every "class=<className> ... conf=<value>" reading seen over
    ' the next durationSeconds and returns the lowest confidence value, or
    ' Nothing if that class was never detected in the window.
    Public Function MinConfidenceOverWindow(className As String, durationSeconds As Double) As Double?
        Return MinConfidenceOverWindowSince(className, durationSeconds, 0)
    End Function

    ' MinConfidenceOverWindow restricted to output printed since an
    ' OutputMark(), so a test case that spawns more than one target in turn
    ' reads each one's own confidence rather than the lowest anything has
    ' scored since the test case began.
    Public Function MinConfidenceOverWindowSince(className As String, durationSeconds As Double, sinceLine As Integer) As Double?
        Dim regex As New Regex($"class={Regex.Escape(className)}\b.*?conf=([\d.]+)")
        Dim deadline As DateTime = DateTime.Now.AddSeconds(durationSeconds)
        Dim checkedUpTo As Integer = sinceLine
        Dim minSeen As Double? = Nothing
        Do
            Dim lines As String() = Common_Test_Functions.PythonOutputSnapshot()
            For i As Integer = checkedUpTo To lines.Length - 1
                Dim m As Match = regex.Match(lines(i))
                If m.Success Then
                    Dim conf As Double = Double.Parse(m.Groups(1).Value, CultureInfo.InvariantCulture)
                    If minSeen Is Nothing OrElse conf < minSeen.Value Then
                        minSeen = conf
                    End If
                End If
            Next
            checkedUpTo = Math.Max(checkedUpTo, lines.Length)
            If DateTime.Now >= deadline Then
                Exit Do
            End If
            Thread.Sleep(200)
        Loop
        Return minSeen
    End Function

    ' AVS-03: fails unless every observed confidence reading for className,
    ' over the next durationSeconds, is at least threshold (e.g. 0.25).
    Public Sub AssertMinConfidenceAtLeast(className As String, threshold As Double, durationSeconds As Double)
        AssertMinConfidenceAtLeastSince(className, threshold, durationSeconds, 0)
    End Sub

    ' AssertMinConfidenceAtLeast restricted to output printed since an
    ' OutputMark() - for a test case that checks one target after another,
    ' where an earlier target's readings would otherwise decide this one's
    ' result (and its "no detections" case could never be reached once
    ' anything had been detected at all).
    Public Sub AssertMinConfidenceAtLeastSince(className As String, threshold As Double, durationSeconds As Double, sinceLine As Integer)
        Dim minSeen As Double? = MinConfidenceOverWindowSince(className, durationSeconds, sinceLine)
        If minSeen Is Nothing Then
            Fail(threshold, "no detections")
        ElseIf minSeen.Value < threshold Then
            Fail(threshold, minSeen.Value)
        Else
            Pass()
        End If
    End Sub

    ' AVS-04: fails unless the pipeline's most recently reported average
    ' per-frame detect time (from __main__.py's "--profile" output, enabled
    ' by TestCaseBegin()'s pipeline relaunch) is below maxMs within
    ' timeoutSeconds. That output only appears every 30 frames, so
    ' timeoutSeconds should give the pipeline time to reach one if none has
    ' been captured yet.
    Public Sub AssertDetectLatencyBelow(maxMs As Double, timeoutSeconds As Double)
        AssertDetectLatencyBelowSince(maxMs, timeoutSeconds, 0)
    End Sub

    ' AssertDetectLatencyBelow restricted to output printed since an
    ' OutputMark() - for a test case that checks latency more than once as
    ' the scene changes (e.g. after each of several spawns), where a profile
    ' line left over from before the latest change would otherwise pass the
    ' check without ever measuring a frame that had the current scene in it.
    Public Sub AssertDetectLatencyBelowSince(maxMs As Double, timeoutSeconds As Double, sinceLine As Integer)
        Dim m As Match = WaitForLatestLogMatchSince("detect=([\d.]+)ms", timeoutSeconds, sinceLine)
        If m Is Nothing Then
            Fail(maxMs, "no timing data")
            Return
        End If
        Dim detectMs As Double = Double.Parse(m.Groups(1).Value, CultureInfo.InvariantCulture)
        If detectMs > maxMs Then
            Fail(maxMs, detectMs)
        Else
            Pass()
        End If
    End Sub
End Module

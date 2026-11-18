Imports System.Globalization
Imports System.Linq
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
        Common_Test_Functions.ThrowIfPipelineExited()
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
        Common_Test_Functions.ThrowIfPipelineExited()
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

    ' Collects every "target id=<n> class=<className> ... pos=(<x>,<y>)"
    ' line printed since an OutputMark(), over the next durationSeconds, and
    ' returns each track ID's smoothed pixel positions in the order they
    ' were printed. Empty if that class was never detected in the window.
    ' A durationSeconds of 0 reads what has already been printed without
    ' waiting - for a second check over a window an earlier check already
    ' waited out (same sinceLine).
    Public Function TrackPositionsOverWindowSince(className As String, durationSeconds As Double, sinceLine As Integer) As Dictionary(Of Integer, List(Of (X As Double, Y As Double)))
        Dim regex As New Regex($"target id=(\d+) class={Regex.Escape(className)}\b.*?pos=\((-?[\d.]+),(-?[\d.]+)\)")
        Dim tracks As New Dictionary(Of Integer, List(Of (X As Double, Y As Double)))
        Dim deadline As DateTime = DateTime.Now.AddSeconds(durationSeconds)
        Dim checkedUpTo As Integer = sinceLine
        Do
            Dim lines As String() = Common_Test_Functions.PythonOutputSnapshot()
            For i As Integer = checkedUpTo To lines.Length - 1
                Dim m As Match = regex.Match(lines(i))
                If m.Success Then
                    Dim id As Integer = Integer.Parse(m.Groups(1).Value, CultureInfo.InvariantCulture)
                    If Not tracks.ContainsKey(id) Then
                        tracks(id) = New List(Of (X As Double, Y As Double))
                    End If
                    tracks(id).Add((Double.Parse(m.Groups(2).Value, CultureInfo.InvariantCulture),
                                    Double.Parse(m.Groups(3).Value, CultureInfo.InvariantCulture)))
                End If
            Next
            checkedUpTo = Math.Max(checkedUpTo, lines.Length)
            If DateTime.Now >= deadline Then
                Exit Do
            End If
            Thread.Sleep(200)
        Loop
        Return tracks
    End Function

    ' AVS-02 (track): fails unless exactly expectedCount distinct track IDs
    ' of className are reported over the next durationSeconds. One target
    ' that drops out and is re-acquired under a new ID counts twice, so with
    ' sinceLine taken before the spawn this also checks that each target
    ' kept a single ID for the whole window.
    Public Sub AssertTrackCountSince(className As String, expectedCount As Integer, durationSeconds As Double, sinceLine As Integer)
        Dim tracks = TrackPositionsOverWindowSince(className, durationSeconds, sinceLine)
        If tracks.Count <> expectedCount Then
            Fail($"{expectedCount} track ID(s)", $"{tracks.Count} ({String.Join(", ", tracks.Keys)})")
        Else
            Pass()
        End If
    End Sub

    ' AVS-02 (track): fails if any track ID's position jumps more than
    ' maxJumpPixels between two consecutive reports over the next
    ' durationSeconds - i.e. the ID was handed from one target to another
    ' (an ID swap) rather than following the same target. Choose
    ' maxJumpPixels well under the pixel spacing between the targets in the
    ' scene but well over how far one target moves between frames. Fails
    ' with "no detections" if className is never reported.
    Public Sub AssertNoTrackJumpsSince(className As String, maxJumpPixels As Double, durationSeconds As Double, sinceLine As Integer)
        Dim tracks = TrackPositionsOverWindowSince(className, durationSeconds, sinceLine)
        If tracks.Count = 0 Then
            Fail($"jumps <= {maxJumpPixels} px", "no detections")
            Return
        End If
        For Each track In tracks
            For i As Integer = 1 To track.Value.Count - 1
                Dim a = track.Value(i - 1)
                Dim b = track.Value(i)
                Dim jump As Double = Math.Sqrt((b.X - a.X) ^ 2 + (b.Y - a.Y) ^ 2)
                If jump > maxJumpPixels Then
                    Fail($"jumps <= {maxJumpPixels} px", $"track {track.Key} jumped {jump:F0} px from ({a.X:F0},{a.Y:F0}) to ({b.X:F0},{b.Y:F0})")
                    Return
                End If
            Next
        Next
        Pass()
    End Sub

    ' AVS-03: fails if className goes unreported for longer than
    ' maxGapSeconds at any point over the next durationSeconds, counting
    ' from when this is called. The detector only reports targets at or
    ' above config.CONFIDENCE_THRESHOLD (0.25), so a target whose
    ' confidence dips below the floor drops out of the output entirely -
    ' this gap, not a low "conf=" reading, is how such a dip shows up.
    ' Call it once the target is already being reported (e.g. right after
    ' AssertTargetDetectedSince()), or the time to first acquisition counts
    ' as a gap. Always waits out the full window.
    Public Sub AssertTargetContinuouslyDetectedSince(className As String, maxGapSeconds As Double, durationSeconds As Double, sinceLine As Integer)
        Dim regex As New Regex($"class={Regex.Escape(className)}\b")
        Dim start As DateTime = DateTime.Now
        Dim deadline As DateTime = start.AddSeconds(durationSeconds)
        Dim lastSeen As DateTime = start
        Dim longestGap As Double = 0
        Dim checkedUpTo As Integer = sinceLine
        Do
            Dim lines As String() = Common_Test_Functions.PythonOutputSnapshot()
            Dim now As DateTime = DateTime.Now
            Dim seen As Boolean = False
            For i As Integer = checkedUpTo To lines.Length - 1
                If regex.IsMatch(lines(i)) Then
                    seen = True
                    Exit For
                End If
            Next
            checkedUpTo = Math.Max(checkedUpTo, lines.Length)
            longestGap = Math.Max(longestGap, (now - lastSeen).TotalSeconds)
            If seen Then
                lastSeen = now
            End If
            If now >= deadline Then
                Exit Do
            End If
            Thread.Sleep(200)
        Loop
        If longestGap > maxGapSeconds Then
            Fail($"no gap > {maxGapSeconds} s", $"{className} unreported for {longestGap:F1} s")
        Else
            Pass()
        End If
    End Sub

    ' --- Track ID persistence ----------------------------------------------

    ' One "target id=..." line the pipeline printed: its track ID, pixel
    ' position, confidence and stereo range (Nothing when "dist=n/a").
    Public Structure TargetReport
        Public Id As Integer
        Public X As Double
        Public Y As Double
        Public Confidence As Double
        Public Distance As Double?
    End Structure

    ' Every className report printed since an OutputMark(), over the next
    ' durationSeconds (0 = only what's already printed), plus how many
    ' frames the pipeline processed in that time, from the "frames=" count
    ' its 1 Hz heartbeat carries. FramedReports counts only the reports
    ' printed between the first and last heartbeat of the window, so that
    ' FramedReports / Frames is a detection rate over the same frames.
    Public Function TargetReportsOverWindowSince(className As String, durationSeconds As Double, sinceLine As Integer) _
            As (Reports As List(Of TargetReport), Frames As Integer, FramedReports As List(Of TargetReport))
        Dim targetRegex As New Regex(
            $"target id=(\d+) class={Regex.Escape(className)}\b.*?conf=([\d.]+) pos=\((-?[\d.]+),(-?[\d.]+)\)(?:.*?dist=([\d.]+)m)?")
        Dim heartbeatRegex As New Regex("HEARTBEAT seq=\d+ .*?frames=(\d+)")
        Thread.Sleep(CInt(durationSeconds * 1000))

        Dim lines As String() = Common_Test_Functions.PythonOutputSnapshot()
        Dim reports As New List(Of TargetReport)
        Dim framed As New List(Of TargetReport)
        Dim firstFrames As Integer = -1
        Dim lastFrames As Integer = -1
        Dim sinceLastHeartbeat As New List(Of TargetReport)
        For i As Integer = sinceLine To lines.Length - 1
            Dim hb As Match = heartbeatRegex.Match(lines(i))
            If hb.Success Then
                Dim frames As Integer = Integer.Parse(hb.Groups(1).Value, CultureInfo.InvariantCulture)
                If firstFrames < 0 Then
                    firstFrames = frames
                Else
                    framed.AddRange(sinceLastHeartbeat)
                End If
                sinceLastHeartbeat.Clear()
                lastFrames = frames
                Continue For
            End If
            Dim m As Match = targetRegex.Match(lines(i))
            If Not m.Success Then
                Continue For
            End If
            Dim report As New TargetReport With {
                .Id = Integer.Parse(m.Groups(1).Value, CultureInfo.InvariantCulture),
                .Confidence = Double.Parse(m.Groups(2).Value, CultureInfo.InvariantCulture),
                .X = Double.Parse(m.Groups(3).Value, CultureInfo.InvariantCulture),
                .Y = Double.Parse(m.Groups(4).Value, CultureInfo.InvariantCulture),
                .Distance = If(m.Groups(5).Success, Double.Parse(m.Groups(5).Value, CultureInfo.InvariantCulture), CType(Nothing, Double?))
            }
            reports.Add(report)
            If firstFrames >= 0 Then
                sinceLastHeartbeat.Add(report)
            End If
        Next
        Return (reports, Math.Max(0, lastFrames - firstFrames), framed)
    End Function

    ' AVS-02 (track): how reliably each target keeps one track ID. Collects
    ' className reports over the next durationSeconds and hands each to the
    ' target assign() says it belongs to (an index into targetNames, or -1
    ' to skip it) - e.g. by pixel row when targets fly in separate lanes, or
    ' by stereo range when one passes in front of another. For each target
    ' it logs:
    '   - ID switches: how many times the ID changed between consecutive
    '     reports of that target (a re-acquisition under a new ID, or an ID
    '     taken over from another target)
    '   - majority ID share: the share of its reports carrying its most
    '     common ID
    '   - detection rate: its reports per frame processed, which tells a
    '     switch caused by the detector losing the target apart from one
    '     caused by the tracker
    ' and fails that target if it was never reported or switched IDs more
    ' than maxSwitches times. One Pass/Fail per target.
    Public Sub AssertIdPersistenceSince(className As String, targetNames As String(),
                                        assign As Func(Of TargetReport, Integer),
                                        maxSwitches As Integer, durationSeconds As Double, sinceLine As Integer)
        Dim window = TargetReportsOverWindowSince(className, durationSeconds, sinceLine)
        For t As Integer = 0 To targetNames.Length - 1
            Dim index As Integer = t
            Dim reports As List(Of TargetReport) = window.Reports.Where(Function(r) assign(r) = index).ToList()
            If reports.Count = 0 Then
                Common_Test_Functions.WriteLog($"ID persistence, {targetNames(t)}: not detected")
                Fail($"{targetNames(t)}: <= {maxSwitches} ID switch(es)", "not detected")
                Continue For
            End If

            Dim switches As Integer = 0
            For i As Integer = 1 To reports.Count - 1
                If reports(i).Id <> reports(i - 1).Id Then
                    switches += 1
                End If
            Next
            Dim byId = reports.GroupBy(Function(r) r.Id).OrderByDescending(Function(g) g.Count()).ToList()
            Dim majorityShare As Double = byId(0).Count() / reports.Count
            Dim framedCount As Integer = window.FramedReports.Where(Function(r) assign(r) = index).Count()
            Dim rateText As String = If(window.Frames > 0,
                String.Format(CultureInfo.InvariantCulture, "{0:0}%", 100.0 * framedCount / window.Frames),
                "n/a")
            Common_Test_Functions.WriteLog(String.Format(CultureInfo.InvariantCulture,
                "ID persistence, {0}: {1} reports, detection rate {2}, IDs [{3}], {4} switch(es), majority ID {5} on {6:0.0}%",
                targetNames(t), reports.Count, rateText,
                String.Join(", ", byId.Select(Function(g) $"{g.Key} x{g.Count()}")),
                switches, byId(0).Key, 100 * majorityShare))

            If switches <= maxSwitches Then
                Pass()
            Else
                Fail($"{targetNames(t)}: <= {maxSwitches} ID switch(es)",
                     String.Format(CultureInfo.InvariantCulture, "{0} switch(es), majority ID on {1:0.0}% of reports", switches, 100 * majorityShare))
            End If
        Next
    End Sub

    ' --- Drone <-> GCS command link (see Common_Test_Functions.vb) ----------

    ' AVS-05: sends a command over the GCS link and fails unless the drone's
    ' reply starts with expectedReply, e.g. "ACK ENGAGE 1" or
    ' "NAK ENGAGE 999 no such target".
    Public Sub AssertGcsReply(command As String, expectedReply As String)
        Dim reply As String = Common_Test_Functions.SendGcsCommand(command)
        If reply IsNot Nothing AndAlso reply.StartsWith(expectedReply, StringComparison.Ordinal) Then
            Pass()
        Else
            Fail(expectedReply, If(reply, "no reply"))
        End If
    End Sub

    ' Fails unless the drone reports expectedState ("TRACKING", "ENGAGED",
    ' "LANDING" or "SHUTDOWN") with expectedTarget engaged ("-" for none).
    Public Sub AssertDroneState(expectedState As String, Optional expectedTarget As String = "-")
        Dim current = Common_Test_Functions.GetDroneState()
        Dim expected As String = $"state={expectedState} target={expectedTarget}"
        If current Is Nothing Then
            Fail(expected, "no STATE line")
            Return
        End If
        Dim actual As String = $"state={current.Value.State} target={current.Value.Target}"
        If actual = expected Then
            Pass()
        Else
            Fail(expected, actual)
        End If
    End Sub

    ' AVS-05-01: listens to the GCS link for durationSeconds and makes three
    ' checks on the heartbeat, timed on arrival at this end:
    '   1. the average period is within meanToleranceSeconds of 1/expectedHz
    '   2. no single gap is off by more than maxDeviationSeconds
    '   3. the sequence numbers run on without a gap (no beat was skipped)
    Public Sub AssertHeartbeatRate(expectedHz As Double, durationSeconds As Double,
                                   meanToleranceSeconds As Double, maxDeviationSeconds As Double)
        Dim beats = Common_Test_Functions.CollectHeartbeats(durationSeconds)
        Dim period As Double = 1.0 / expectedHz
        If beats.Count < 2 Then
            Fail($"{expectedHz} Hz", $"{beats.Count} heartbeat(s) in {durationSeconds}s")
            Fail($"every gap {period:0.00}s +/- {maxDeviationSeconds:0.00}s", "too few heartbeats")
            Fail("consecutive sequence numbers", "too few heartbeats")
            Return
        End If

        Dim gaps As New List(Of Double)
        For i As Integer = 1 To beats.Count - 1
            gaps.Add(beats(i).Seconds - beats(i - 1).Seconds)
        Next
        Dim meanPeriod As Double = (beats(beats.Count - 1).Seconds - beats(0).Seconds) / gaps.Count
        Common_Test_Functions.WriteLog(String.Format(CultureInfo.InvariantCulture,
            "Heartbeat gaps (s): {0}", String.Join(", ", gaps.Select(Function(g) g.ToString("0.000", CultureInfo.InvariantCulture)))))

        If Math.Abs(meanPeriod - period) <= meanToleranceSeconds Then
            Pass()
        Else
            Fail(String.Format(CultureInfo.InvariantCulture, "mean period {0:0.000}s", period),
                 String.Format(CultureInfo.InvariantCulture, "{0:0.000}s ({1:0.00} Hz)", meanPeriod, 1.0 / meanPeriod))
        End If

        Dim worstGap As Double = gaps.OrderByDescending(Function(g) Math.Abs(g - period)).First()
        If Math.Abs(worstGap - period) <= maxDeviationSeconds Then
            Pass()
        Else
            Fail(String.Format(CultureInfo.InvariantCulture, "every gap {0:0.00}s +/- {1:0.00}s", period, maxDeviationSeconds),
                 String.Format(CultureInfo.InvariantCulture, "a gap of {0:0.000}s", worstGap))
        End If

        Dim seqs As List(Of Integer) = beats.Select(Function(b) Integer.Parse(
            Common_Test_Functions.ParseGcsFields(b.Line).GetValueOrDefault("seq", "-1"), CultureInfo.InvariantCulture)).ToList()
        Dim firstSkip As Integer = Enumerable.Range(1, seqs.Count - 1).FirstOrDefault(Function(i) seqs(i) <> seqs(i - 1) + 1, -1)
        If firstSkip = -1 Then
            Pass()
        Else
            Fail("consecutive sequence numbers", $"seq {seqs(firstSkip - 1)} then {seqs(firstSkip)}")
        End If
    End Sub
End Module

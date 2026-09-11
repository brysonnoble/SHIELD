Imports System.Globalization
Imports System.Text.RegularExpressions
Imports System.Threading

' Pass/fail assertions a test case can make against the current test's
' captured Python console output (PythonOutputSnapshot(), populated by
' BeginTest() in Common_Test_Functions.vb - the raw lines themselves go to
' that run's separate "_python_output.log", not the main log). Call these
' after TraceTo() so a failure lands under the requirement it was checking.
Public Module Common_Test_Checks
    ' Thrown by Fail() so a failed assertion stops the test case instead of
    ' silently continuing, and so STE_Test_Solution.exe exits non-zero
    ' (an unhandled exception's default exit code) - the log's Output Check
    ' line (written before the throw) already has the Expected/Actual values.
    Public Class TestAssertionFailedException
        Inherits Exception
        Public Sub New(message As String)
            MyBase.New(message)
        End Sub
    End Class

    ' Logs "Output Check <n>: PASS", <n> being a 1-based counter of checks
    ' made so far in the current test case (reset by TestCaseBegin()).
    Public Sub Pass()
        Dim n As Integer = Common_Test_Functions.NextCheckNumber()
        Common_Test_Functions.WriteLog($"Output Check {n}: PASS")
    End Sub

    ' Logs "Output Check <n>: **FAIL** Expected Value: <expectedValue>,
    ' Actual Value: <actualValue>", marks the current test case failed (for
    ' TestCaseEnd()/EndTest()'s summary), and throws so the rest of the
    ' calling TCxx() doesn't keep running against a failed assertion -
    ' RunTestCase() catches this and still runs TestCaseEnd(), then moves on
    ' to the script's next test case.
    Public Sub Fail(expectedValue As Object, actualValue As Object)
        Dim n As Integer = Common_Test_Functions.NextCheckNumber()
        Common_Test_Functions.WriteLog($"Output Check {n}: **FAIL** Expected Value: {expectedValue}, Actual Value: {actualValue}")
        Common_Test_Functions.MarkCurrentTestCaseFailed()
        Throw New TestAssertionFailedException($"Expected {expectedValue}, got {actualValue}")
    End Sub

    ' Polls the captured Python output every 200ms until a line matching
    ' pattern (a .NET regex) appears or timeoutSeconds elapses, and returns
    ' the match (or Nothing on timeout). Only looks at lines newer than the
    ' last check each poll, so a slow-to-appear match still isn't missed.
    Public Function WaitForLogMatch(pattern As String, timeoutSeconds As Double) As Match
        Dim regex As New Regex(pattern)
        Dim deadline As DateTime = DateTime.Now.AddSeconds(timeoutSeconds)
        Dim checkedUpTo As Integer = 0
        Do
            Dim lines As String() = Common_Test_Functions.PythonOutputSnapshot()
            For i As Integer = checkedUpTo To lines.Length - 1
                Dim m As Match = regex.Match(lines(i))
                If m.Success Then
                    Return m
                End If
            Next
            checkedUpTo = lines.Length
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
        Dim regex As New Regex(pattern)
        Dim deadline As DateTime = DateTime.Now.AddSeconds(timeoutSeconds)
        Do
            Dim lines As String() = Common_Test_Functions.PythonOutputSnapshot()
            Dim latest As Match = Nothing
            For Each line As String In lines
                Dim m As Match = regex.Match(line)
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
        Dim m As Match = WaitForLogMatch($"class={Regex.Escape(className)}\b", timeoutSeconds)
        If m Is Nothing Then
            Fail(className, "not detected")
        Else
            Pass()
        End If
    End Sub

    ' Collects every "class=<className> ... conf=<value>" reading seen over
    ' the next durationSeconds and returns the lowest confidence value, or
    ' Nothing if that class was never detected in the window.
    Public Function MinConfidenceOverWindow(className As String, durationSeconds As Double) As Double?
        Dim regex As New Regex($"class={Regex.Escape(className)}\b.*?conf=([\d.]+)")
        Dim deadline As DateTime = DateTime.Now.AddSeconds(durationSeconds)
        Dim checkedUpTo As Integer = 0
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
            checkedUpTo = lines.Length
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
        Dim minSeen As Double? = MinConfidenceOverWindow(className, durationSeconds)
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
        Dim m As Match = WaitForLatestLogMatch("detect=([\d.]+)ms", timeoutSeconds)
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

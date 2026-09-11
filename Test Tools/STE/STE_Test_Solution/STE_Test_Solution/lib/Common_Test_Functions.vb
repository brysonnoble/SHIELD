Imports System.Collections.Concurrent
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Threading

Public Module Common_Test_Functions
    ' PASS: the test case ran to completion and every assertion held.
    ' FAIL: the test case ran to completion, but an assertion
    ' (Common_Test_Checks.Fail()) determined the system under test didn't
    ' meet the requirement.
    ' ABORT: the test case itself couldn't finish running - an unhandled
    ' exception (a real bug, a broken scene/network setup, etc.), as
    ' opposed to a deliberate assertion failure. Distinct from FAIL because
    ' it means nothing was actually verified either way.
    Public Enum TestCaseStatus
        Pass
        Fail
        Abort
    End Enum

    Private unityProcess As Process
    Private pythonProcess As Process
    Private pythonOutput As ConcurrentQueue(Of String)
    Private logWriter As StreamWriter
    Private pythonLogWriter As StreamWriter
    Private testCaseNumber As Integer
    Private testStartTime As DateTime

    ' Per-test-case bookkeeping for EndTest()'s summary. currentTestCase*
    ' track the test case in progress (reset by TestCaseBegin(), read/closed
    ' out by TestCaseEnd()); testCaseResults accumulates one finished
    ' (number, status, traces) entry per test case for EndTest() to print.
    ' currentCheckNumber numbers each Pass()/Fail() call within the current
    ' test case (Common_Test_Checks.vb's "Output Check <n>" lines).
    Private currentTestCaseStatus As TestCaseStatus
    Private currentTestCaseTraces As New List(Of String)
    Private currentCheckNumber As Integer
    Private testCaseResults As New List(Of (Number As Integer, Status As TestCaseStatus, Traces As List(Of String)))

    ' Guards WriteLog()'s Console/file writes - it's called both from the
    ' main test-script thread and from RunProcessCapturingOutput()'s
    ' background stdout/stderr reader threads (Python's console output is
    ' relayed through it), which could otherwise interleave two lines'
    ' output.
    Private ReadOnly logLock As New Object()

    ' Guards pythonLogWriter's writes (see WritePythonLog()) separately from
    ' logLock since they're different files - no need to serialize one
    ' behind the other.
    Private ReadOnly pythonLogLock As New Object()

    Public Sub BeginTest()
        testStartTime = DateTime.Now
        Dim runFolder As String = Path.Combine(
            LogRootDirectory, CurrentTestName, testStartTime.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture))
        Directory.CreateDirectory(runFolder)
        Dim logPath As String = Path.Combine(runFolder, CurrentTestName & ".log")
        Dim pythonLogPath As String = Path.Combine(runFolder, CurrentTestName & "_python_output.log")
        logWriter = New StreamWriter(logPath)
        logWriter.AutoFlush = True
        pythonLogWriter = New StreamWriter(pythonLogPath)
        pythonLogWriter.AutoFlush = True
        testCaseNumber = 0
        testCaseResults = New List(Of (Number As Integer, Status As TestCaseStatus, Traces As List(Of String)))

        ' Header: enough to identify this run without needing the folder
        ' path it's sitting in (name/timestamp) or to dig through
        ' Common_Test_Variables.vb for what it was actually exercising
        ' (platform/source/startup delay), if this log is later read on its
        ' own (e.g. attached to a bug report). Unity/Python themselves
        ' aren't launched here - see TestCaseBegin()/LaunchPipeline(): each
        ' test case gets its own fresh instance of both.
        WriteLog($"=== BeginTest: {CurrentTestName} ===")
        WriteLog($"Started: {testStartTime:yyyy-MM-dd HH:mm:ss} on {Environment.MachineName}")
        WriteLog($"Platform: {TestPlatform.Emulation} ({CInt(TestPlatform.Emulation)}), Source: unity")
        WriteLog($"Startup delay: {StartupDelaySeconds}s, Log file: {logPath}")
        WriteLog($"Raw pipeline output: {pythonLogPath}")
    End Sub

    Public Sub EndTest()
        ' Safety net - TestCaseEnd() already closes the pipeline after every
        ' test case, so this is normally a no-op by the time EndTest() runs.
        ClosePipeline()

        Dim passCount As Integer = testCaseResults.Where(Function(r) r.Status = TestCaseStatus.Pass).Count()
        Dim failCount As Integer = testCaseResults.Where(Function(r) r.Status = TestCaseStatus.Fail).Count()
        Dim abortCount As Integer = testCaseResults.Where(Function(r) r.Status = TestCaseStatus.Abort).Count()

        ' Worst-outcome-wins: an ABORT means a test case couldn't even run
        ' to a real pass/fail determination (a bug, not a verified
        ' shortfall), so it's called out over a plain FAIL even if some
        ' other test case failed cleanly.
        Dim overall As String
        If abortCount > 0 Then
            overall = "ABORT"
        ElseIf failCount > 0 Then
            overall = "FAIL"
        Else
            overall = "PASS"
        End If
        WriteLog($"=== EndTest: {CurrentTestName} - {overall} (Pass: {passCount}, Fail: {failCount}, Abort: {abortCount}, Total: {testCaseResults.Count}) ===")
        For Each result In testCaseResults
            Dim traces As String = If(result.Traces.Count > 0, " [" & String.Join(", ", result.Traces) & "]", "")
            WriteLog($"--- TestCase {result.Number}{traces}: {result.Status.ToString().ToUpperInvariant()} ---")
        Next
        WriteLog($"Duration: {(DateTime.Now - testStartTime).ToString("hh\:mm\:ss")}")

        logWriter?.Dispose()
        logWriter = Nothing
        pythonLogWriter?.Dispose()
        pythonLogWriter = Nothing
    End Sub

    ' Runs one test case (a TCxx() Sub taking no arguments) as
    ' TestCaseBegin() / testCase() / TestCaseEnd(). Catches a deliberate
    ' assertion failure (Common_Test_Checks.Fail(), already marked FAIL) as
    ' well as any other unhandled exception - including TestCaseBegin()'s
    ' own pipeline relaunch timing out (marked ABORT instead - see
    ' TestCaseStatus.Abort) - so one bad test case doesn't stop the rest of
    ' the script's test cases - or EndTest() and its final summary - from
    ' running. TCxx() itself should just TraceTo() and assert; call it via
    ' RunTestCase(AddressOf TCxx) from Main() instead of TCxx() directly.
    Public Sub RunTestCase(testCase As Action)
        Try
            TestCaseBegin()
            testCase()
        Catch ex As Common_Test_Checks.TestAssertionFailedException
            ' Already logged by Fail(); currentTestCaseStatus is already Fail.
        Catch ex As Exception
            WriteLog($"ABORT: Unhandled exception: {ex.Message}")
            MarkCurrentTestCaseAborted()
        Finally
            TestCaseEnd()
        End Try
    End Sub

    ' Call at the start of every test case (TCxx), before any TraceTo/other
    ' calls. Closes and relaunches Unity + Python fresh (LaunchPipeline())
    ' so every test case starts from the same known, empty scene rather than
    ' whatever a previous test case left behind, and marks the test case's
    ' start in the log. Normally called by RunTestCase(), not directly.
    Public Sub TestCaseBegin()
        testCaseNumber += 1
        currentTestCaseStatus = TestCaseStatus.Pass
        currentTestCaseTraces = New List(Of String)
        currentCheckNumber = 0
        WriteLog($"--- TestCase {testCaseNumber} Begin ---")
        LaunchPipeline()
    End Sub

    ' Call at the end of every test case (TCxx). Closes the pipeline
    ' TestCaseBegin() launched for this test case, logs its PASS/FAIL/ABORT
    ' result, and records that result for EndTest()'s summary. Normally
    ' called by RunTestCase(), not directly.
    Public Sub TestCaseEnd()
        ClosePipeline()
        Dim traces As New List(Of String)(currentTestCaseTraces)
        testCaseResults.Add((testCaseNumber, currentTestCaseStatus, traces))
        Dim tracesText As String = If(traces.Count > 0, " [" & String.Join(", ", traces) & "]", "")
        WriteLog($"--- TestCase {testCaseNumber}{tracesText}: {currentTestCaseStatus.ToString().ToUpperInvariant()} ---")
    End Sub

    ' Marks the currently-running test case (since the last TestCaseBegin())
    ' FAILed, for TestCaseEnd()/EndTest()'s summary. Called by
    ' Common_Test_Checks.Fail() - a test case doesn't need to call this
    ' itself.
    Public Sub MarkCurrentTestCaseFailed()
        currentTestCaseStatus = TestCaseStatus.Fail
    End Sub

    ' Marks the currently-running test case (since the last TestCaseBegin())
    ' ABORTed - it hit an unhandled exception rather than a deliberate
    ' assertion failure, so nothing was actually verified either way.
    ' Called by RunTestCase()'s catch-all - a test case doesn't need to call
    ' this itself.
    Public Sub MarkCurrentTestCaseAborted()
        currentTestCaseStatus = TestCaseStatus.Abort
    End Sub

    ' Increments and returns the current test case's check counter, for
    ' Common_Test_Checks.vb's Pass()/Fail() to number their "Output Check
    ' <n>" lines. Reset to 0 by TestCaseBegin().
    Public Function NextCheckNumber() As Integer
        currentCheckNumber += 1
        Return currentCheckNumber
    End Function

    ' Closes any already-running instance of the virtual camera player
    ' first - a leftover orphan from a previous crashed/killed run would
    ' otherwise still be holding the scene's TCP ports, so this test case's
    ' own Unity instance either fails to bind them or (worse) ends up
    ' talking to the stale instance - then launches a fresh Unity + Python,
    ' and waits until both are actually ready for commands. Throws (letting
    ' RunTestCase()'s catch-all mark the test case ABORTed) if Python's
    ' video connection to Unity is never confirmed within 30s.
    Private Sub LaunchPipeline()
        ClosePipeline()

        ' The raw pipeline log spans the whole script (opened once by
        ' BeginTest()), but the process it's capturing gets relaunched for
        ' every test case - mark where each one's output starts so the file
        ' doesn't read as one unbroken run.
        WritePythonLog($"=== TestCase {testCaseNumber} pipeline start ===")

        CloseExistingUnityPlayers()
        unityProcess = RunProcess(UNITY_PLAYER_PATH)

        ' Matches README.md's "Running against the Unity virtual camera":
        '   cd "SHIELD\SHIELD"
        '   .venv\Scripts\python __main__.py 0 --source unity
        ' "-u" disables Python's stdout buffering and "--profile" turns on
        ' its periodic per-stage timing line - both needed for the capture
        ' below to actually see detection/timing output promptly instead of
        ' it sitting in a buffer (see RunProcessCapturingOutput's remarks).
        ' Every line is captured for a test case to assert on
        ' (Common_Test_Checks.vb) and also written to pythonLogWriter (the
        ' separate "_python_output.log") - not the main log, which would
        ' otherwise be buried in a per-frame "target id=..." /
        ' "[SHIELD][profile]" line for every single frame processed.
        Dim pythonRun = RunProcessCapturingOutput(
            PYTHON_PATH,
            $"-u __main__.py {CInt(TestPlatform.Emulation)} --source unity --profile",
            SHIELD_DIRECTORY,
            Sub(line) WritePythonLog(line))
        pythonProcess = pythonRun.Process
        pythonOutput = pythonRun.Output

        ' Don't let the test case start until every launched program is
        ' actually up, not just started - Process.Start() returning only
        ' means the OS created the process, not that Unity has reached Play
        ' and bound its scene command listeners.
        WaitForListener(UNITY_HOST, UNITY_ENV_PORT)
        WaitForListener(UNITY_HOST, UNITY_SPAWN_PORT)

        ' The listeners above are Unity's scene-command ports, separate from
        ' the actual video link Python's own UnityStreamSource opens to
        ' Unity's CameraStreamer (port 5555) - confirm that one too, via
        ' Python's own "Connected to Unity at ..." line (video_source.py),
        ' rather than assuming it came up just because the scene-command
        ' ports did. By the time that line prints, the YOLO model has
        ' already loaded too (SHIELDDetector() runs before source.open() in
        ' __main__.py), so this is a strictly stronger readiness signal than
        ' the scene-command listeners alone.
        If Common_Test_Checks.WaitForLogMatch("Connected to Unity", 30) Is Nothing Then
            Throw New TimeoutException("Unity video connection not confirmed within 30s.")
        End If

        ' Confirms the video link and scene-command listeners are up, but
        ' not that everything else (the OpenCV preview window, Unity's scene
        ' fully rendering) has finished opening. Configurable in STE's
        ' Settings page.
        Thread.Sleep(StartupDelaySeconds * 1000)
    End Sub

    ' Closes this test case's Unity/Python processes, if running. Safe to
    ' call even if neither is running (e.g. TestCaseBegin()'s own
    ' LaunchPipeline() calls this first, before either exists yet).
    Private Sub ClosePipeline()
        CloseProcess(pythonProcess)
        CloseUnityPlayer(unityProcess)
        pythonProcess = Nothing
        unityProcess = Nothing
    End Sub

    ' Closes the Unity virtual camera player ONLY via its own "QUIT" scene
    ' command (CameraController.cs's Application.Quit()) - never via an
    ' OS-level close/kill. TerminateProcess against a process that still has
    ' a live Direct3D/OpenGL device open has caused a GPU driver crash
    ' (BSOD) on this project's hardware, including through
    ' CloseProcess()'s own "graceful CloseMainWindow(), then Kill() if that
    ' doesn't work" fallback - Kill() itself is the problem, not just an
    ' outright Kill() with no attempt at grace first. So if Unity doesn't
    ' exit within 10s of being asked to QUIT, this leaves it running rather
    ' than escalating to a kill, and marks the current test case ABORTed
    ' instead - the run can't verify the environment is clean, but at least
    ' it doesn't risk crashing the machine to find out.
    Private Sub CloseUnityPlayer(process As Process)
        If process Is Nothing OrElse process.HasExited Then
            Return
        End If
        Try
            SendTcpCommand(UNITY_HOST, UNITY_CAMERA_PORT, "QUIT", connectRetries:=1, retryDelayMs:=0)
        Catch ex As Exception
            WriteLog($"WARNING: Could not send QUIT to Unity: {ex.Message}")
        End Try
        If Not process.WaitForExit(10000) Then
            WriteLog(
                "ABORT: Unity did not exit within 10s of being asked to QUIT. " &
                "Leaving it running rather than force-killing it (a past cause " &
                "of a GPU driver crash/BSOD on this hardware) - close it " &
                "manually before the next run.")
            MarkCurrentTestCaseAborted()
        End If
    End Sub

    ' Closes any already-running instance of the virtual camera player the
    ' same graceful way as CloseUnityPlayer() - a leftover orphan from a
    ' previous crashed/killed run would otherwise still be holding the
    ' scene's TCP ports, so this test case's own Unity instance either fails
    ' to bind them or (worse) ends up talking to the stale instance. Throws
    ' if an orphan wouldn't close (already marked ABORT and logged by
    ' CloseUnityPlayer()), so LaunchPipeline() doesn't go on to launch a
    ' second Unity instance alongside a first one still sitting there.
    Private Sub CloseExistingUnityPlayers()
        Dim anyStillRunning As Boolean = False
        For Each proc As Process In FindProcessesByExePath(UNITY_PLAYER_PATH)
            Try
                CloseUnityPlayer(proc)
                If Not proc.HasExited Then
                    anyStillRunning = True
                End If
            Finally
                proc.Dispose()
            End Try
        Next
        If anyStillRunning Then
            Throw New InvalidOperationException("A previous Unity player instance is still running and would not close.")
        End If
    End Sub

    ' Call once per requirement a test case exercises, after TestCaseBegin()
    ' (i.e. anywhere in a TCxx() body). Logs that requirement as covered by
    ' the current test case and includes it in that test case's line of
    ' EndTest()'s summary.
    Public Sub TraceTo(requirementName As String)
        currentTestCaseTraces.Add(requirementName)
        WriteLog($"Trace: {requirementName}")
    End Sub

    ' Public so Common_Test_Checks.vb's Pass()/Fail() can log into the same
    ' run log a test case's assertions ran against.
    Public Sub WriteLog(message As String)
        Dim line As String = $"[{DateTime.Now:HH:mm:ss}] {message}"
        SyncLock logLock
            Console.WriteLine(line)
            logWriter?.WriteLine(line)
        End SyncLock
    End Sub

    ' Writes one line of the Python pipeline's raw console output (every
    ' per-frame "target id=..." and "[SHIELD][profile]" line) to that run's
    ' separate "_python_output.log" instead of the main log - called from
    ' RunProcessCapturingOutput()'s background stdout/stderr reader threads,
    ' so it needs its own lock rather than sharing WriteLog()'s.
    Private Sub WritePythonLog(line As String)
        Dim timestamped As String = $"[{DateTime.Now:HH:mm:ss}] {line}"
        SyncLock pythonLogLock
            pythonLogWriter?.WriteLine(timestamped)
        End SyncLock
    End Sub

    ' A snapshot of every line the current test case's Python process has
    ' printed so far (TestCaseBegin() must have run first). Used by
    ' Common_Test_Checks.vb to search for expected output without racing
    ' the background reader thread that's still appending to the live queue.
    Public Function PythonOutputSnapshot() As String()
        Return If(pythonOutput Is Nothing, Array.Empty(Of String)(), pythonOutput.ToArray())
    End Function

    ' Sends a command over TCP to the Unity scene to switch the day/night skybox.
    Public Sub EditVirtualEnvironment(virtualEnvironment As Common_Test_Variables.VirtualEnvironment)
        SendTcpCommand(UNITY_HOST, UNITY_ENV_PORT, "ENV " & virtualEnvironment.ToString().ToUpperInvariant())
    End Sub

    ' Sends a command over TCP to the Unity scene to instantiate one of the
    ' three drones (DroneType.Quad/Toad/BumbleBee) at the given world coordinates.
    Public Sub InstDrone(droneType As Common_Test_Variables.DroneType, x As Double, y As Double, z As Double)
        Dim command As String = String.Format(CultureInfo.InvariantCulture,
                                               "SPAWN {0} {1} {2} {3}",
                                               droneType.ToString(), x, y, z)
        SendTcpCommand(UNITY_HOST, UNITY_SPAWN_PORT, command)
    End Sub
End Module

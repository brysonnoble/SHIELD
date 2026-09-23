Imports System.Collections.Concurrent
Imports System.Net.Sockets
Imports System.Text
Imports System.Threading

Public Module Common_Core_Functions
    ' This function is used to run a process given a file path and optional
    ' arguments, returning the started Process so the caller can wait on or
    ' close it later (see EndTest()).
    Public Function RunProcess(filePath As String,
                    Optional arguments As String = "",
                    Optional workingDirectory As String = "") As Process
        Dim process As New Process()
        process.StartInfo.FileName = filePath
        process.StartInfo.Arguments = arguments
        process.StartInfo.UseShellExecute = False
        If workingDirectory <> "" Then
            process.StartInfo.WorkingDirectory = workingDirectory
        End If
        process.Start()
        Return process
    End Function

    ' Like RunProcess, but redirects stdout/stderr instead of letting the
    ' child inherit the console, so a test case can search what the process
    ' printed (see Common_Test_Checks.vb) instead of only eyeballing the
    ' log. Every line is queued (for later/repeated searching) and also
    ' handed to onLine as it arrives, if given, e.g. to relay it into
    ' WriteLog() so it still shows up in the run's log/console exactly like
    ' an unredirected process's output would have.
    '
    ' The launched process should disable its own stdout buffering (e.g.
    ' Python's "-u" flag) - otherwise output can sit in the child's buffer
    ' for a long time before a test's WaitForLogMatch-style check ever sees
    ' it, since a pipe (unlike a real console) doesn't get line-buffered by
    ' default.
    '
    ' redirectStandardInput also gives the caller a stdin pipe
    ' (Process.StandardInput) - e.g. to ask the process to quit on its own
    ' (see Common_Test_Functions.vb's ClosePythonPipeline()) rather than
    ' killing it.
    Public Function RunProcessCapturingOutput(filePath As String,
                    Optional arguments As String = "",
                    Optional workingDirectory As String = "",
                    Optional onLine As Action(Of String) = Nothing,
                    Optional redirectStandardInput As Boolean = False) _
                    As (Process As Process, Output As ConcurrentQueue(Of String))
        Dim process As New Process()
        process.StartInfo.FileName = filePath
        process.StartInfo.Arguments = arguments
        process.StartInfo.UseShellExecute = False
        process.StartInfo.RedirectStandardOutput = True
        process.StartInfo.RedirectStandardError = True
        process.StartInfo.RedirectStandardInput = redirectStandardInput
        process.StartInfo.CreateNoWindow = True
        If workingDirectory <> "" Then
            process.StartInfo.WorkingDirectory = workingDirectory
        End If

        Dim output As New ConcurrentQueue(Of String)
        Dim handler As DataReceivedEventHandler = Sub(sender As Object, e As DataReceivedEventArgs)
                                                       If e.Data Is Nothing Then Return
                                                       output.Enqueue(e.Data)
                                                       onLine?.Invoke(e.Data)
                                                   End Sub
        AddHandler process.OutputDataReceived, handler
        AddHandler process.ErrorDataReceived, handler

        process.Start()
        process.BeginOutputReadLine()
        process.BeginErrorReadLine()
        Return (process, output)
    End Function

    ' Closes a process previously started by RunProcess, along with any
    ' processes it spawned (e.g. Unity's crash handler, Python's own child
    ' processes). Safe to call on a process that's already exited or Nothing.
    '
    ' Tries a graceful WM_CLOSE first. Unity (and anything else with a GPU
    ' device open) needs to run its own shutdown path to release Direct3D/
    ' OpenGL resources cleanly - an outright Kill (TerminateProcess) skips
    ' that, and killing a live graphics process this way has been observed to
    ' crash the GPU driver (BSOD) instead of just closing the window. Only
    ' force-kill if it doesn't exit on its own.
    Public Sub CloseProcess(process As Process)
        If process Is Nothing OrElse process.HasExited Then
            Return
        End If
        Try
            If process.CloseMainWindow() Then
                process.WaitForExit(3000)
            End If
            If Not process.HasExited Then
                process.Kill(entireProcessTree:=True)
                process.WaitForExit(5000)
            End If
        Catch
        End Try
    End Sub

    ' Finds every already-running process launched from the given
    ' executable path, matched by image name (Process.GetProcessesByName
    ' ignores the path, just the file name without ".exe"). Used to find a
    ' leftover orphan from a previous crashed/killed run - e.g. one still
    ' holding a scene's TCP ports - before launching a fresh instance.
    ' Caller is responsible for closing (see Common_Test_Functions.vb's
    ' CloseUnityPlayer(), which favors a graceful in-app quit over an
    ' OS-level close/kill for a GPU-device-holding process like Unity) and
    ' disposing each one.
    Public Function FindProcessesByExePath(exePath As String) As Process()
        Dim name As String = System.IO.Path.GetFileNameWithoutExtension(exePath)
        Return Process.GetProcessesByName(name)
    End Function

    ' Repeatedly attempts a TCP connection to host:port until one succeeds or
    ' connectRetries is exhausted, then returns the open connection. Used to
    ' wait for a just-launched program's listener to actually come up, since
    ' Process.Start() returning only means the OS created the process, not
    ' that it's finished initializing.
    Private Function ConnectWithRetry(host As String, port As Integer,
                                       connectRetries As Integer, retryDelayMs As Integer) As TcpClient
        Dim lastException As SocketException = Nothing
        For attempt As Integer = 1 To connectRetries
            Try
                Dim client As New TcpClient()
                client.Connect(host, port)
                Return client
            Catch ex As SocketException
                lastException = ex
                Thread.Sleep(retryDelayMs)
            End Try
        Next
        Throw lastException
    End Function

    ' Blocks until host:port is accepting connections, then disconnects.
    ' Mirrors config.py's UNITY_CONNECT_RETRIES/UNITY_RETRY_DELAY_SEC on the
    ' Python side of this same "just launched, not ready yet" race.
    Public Sub WaitForListener(host As String, port As Integer,
                                Optional connectRetries As Integer = 15,
                                Optional retryDelayMs As Integer = 1000)
        ConnectWithRetry(host, port, connectRetries, retryDelayMs).Dispose()
    End Sub

    ' Opens a TCP connection to a Unity scene command listener, sends a single
    ' newline-terminated text command, and closes the connection. Retries the
    ' connection (not the send) for the same reason as WaitForListener.
    Public Sub SendTcpCommand(host As String, port As Integer, command As String,
                               Optional connectRetries As Integer = 15,
                               Optional retryDelayMs As Integer = 1000)
        Using client As TcpClient = ConnectWithRetry(host, port, connectRetries, retryDelayMs)
            Using stream As NetworkStream = client.GetStream()
                Dim bytes As Byte() = Encoding.ASCII.GetBytes(command & vbLf)
                stream.Write(bytes, 0, bytes.Length)
            End Using
        End Using
    End Sub
End Module

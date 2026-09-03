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

    ' Closes a process previously started by RunProcess, along with any
    ' processes it spawned (e.g. Unity's crash handler, Python's own child
    ' processes). Safe to call on a process that's already exited or Nothing.
    Public Sub CloseProcess(process As Process)
        If process Is Nothing OrElse process.HasExited Then
            Return
        End If
        Try
            process.Kill(entireProcessTree:=True)
            process.WaitForExit(5000)
        Catch
        End Try
    End Sub

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

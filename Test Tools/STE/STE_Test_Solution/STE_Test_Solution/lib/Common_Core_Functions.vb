Public Module Common_Core_Functions
    ' This function is used to run a process given a file path and optional arguments.
    Public Sub RunProcess(filePath As String,
                    Optional arguments As String = "")
        Dim process As New Process()
        process.StartInfo.FileName = filePath
        process.StartInfo.Arguments = arguments
        process.Start()
    End Sub
End Module

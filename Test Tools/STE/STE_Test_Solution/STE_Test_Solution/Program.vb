Imports System.Linq
Imports System.Reflection

' Real entry point for the compiled test runner. Every script under
' Test Scripts\ (as discovered by STE's HomePage.xaml.cs) defines its own
' parameterless "Sub Main" following the Example_Test.vb pattern; this
' dispatches to the one named on the command line instead of relying on
' the project's StartupObject to pick a single, fixed script.
'
' Usage: STE_Test_Solution.exe <TestName> [StartupDelaySeconds]
' <TestName> is the script's path relative to Test Scripts\, without the
' .vb extension (e.g. "AVS\Drone_Spawn_Test") - the same string
' HomePage.xaml.cs shows in the STE test list. <StartupDelaySeconds>, if
' given, overrides Common_Test_Variables.StartupDelaySeconds's default -
' HomePage.xaml.cs passes STE's Settings-page value here.
Module Program
    Sub Main(args As String())
        If args.Length < 1 Then
            Console.Error.WriteLine("Usage: STE_Test_Solution.exe <TestName> [StartupDelaySeconds]")
            Environment.Exit(1)
            Return
        End If

        Dim testName As String = args(0)
        Dim moduleName As String = testName.Split("\"c, "/"c).Last()

        If args.Length >= 2 Then
            Dim delaySeconds As Integer
            If Integer.TryParse(args(1), delaySeconds) AndAlso delaySeconds >= 0 Then
                Common_Test_Variables.StartupDelaySeconds = delaySeconds
            End If
        End If

        Dim testModule As Type = Assembly.GetExecutingAssembly().GetTypes().
            FirstOrDefault(Function(t) t.Name = moduleName)

        If testModule Is Nothing Then
            Console.Error.WriteLine($"Test '{testName}' not found (no module named '{moduleName}').")
            Environment.Exit(1)
            Return
        End If

        Dim entryPoint As MethodInfo = testModule.GetMethod("Main", BindingFlags.Public Or BindingFlags.Static, Nothing, Type.EmptyTypes, Nothing)
        If entryPoint Is Nothing Then
            Console.Error.WriteLine($"Test '{testName}' has no parameterless Sub Main.")
            Environment.Exit(1)
            Return
        End If

        ' While this is running, this process (and the Unity/Python child
        ' processes BeginTest() launched) stay alive naturally - HomePage.
        ' xaml.cs's Stop button can kill this whole tree
        ' (Process.Kill(entireProcessTree:=True)) at any point during the
        ' run. EndTest() closes Unity/Python itself once the script
        ' completes normally, so this process then exits on its own too.
        entryPoint.Invoke(Nothing, Nothing)
    End Sub
End Module

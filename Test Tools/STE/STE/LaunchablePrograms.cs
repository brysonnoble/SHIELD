using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace STE
{
    // How HomePage's Stop button closes a launched program. Anything holding
    // a live GPU device (the Unity player's Direct3D device, the Python
    // pipeline's CUDA context) must only ever be asked to quit - killing it
    // (TerminateProcess) has crashed the GPU driver (BSOD) on this project's
    // hardware. Only a process with no GPU state may be killed.
    public enum StopMethod
    {
        // Kill the process tree outright - only for non-GPU processes
        // (e.g. "dotnet build").
        Kill,

        // Send "QUIT" to the Unity player's CameraController command port
        // (Application.Quit()), falling back to WM_CLOSE if it isn't
        // listening yet - Common_Test_Functions.vb's CloseUnityPlayer().
        UnityQuitCommand,

        // Write "QUIT" to the process's stdin - needs SHIELD's
        // __main__.py --quit-on-stdin (Common_Test_Functions.vb's
        // ClosePythonPipeline()).
        StdinQuit,
    }

    // One program the "Launch Programs" button can start directly (without
    // going through STE_Test_Solution.exe / a test script). Mirrors the
    // Unity/Python processes Common_Test_Functions.vb's BeginTest() launches,
    // so a test run and a manual "Launch Programs" click start the same
    // things the same way.
    public class LaunchableProgram
    {
        // Must match Common_Test_Variables.vb's UNITY_HOST/UNITY_CAMERA_PORT
        // (CameraController.cs's "port" field).
        private const string UnityHost = "127.0.0.1";
        private const int UnityCameraPort = 5558;

        // How long a GPU process gets to exit after being asked to quit
        // before Stop gives up on it and leaves it running.
        private static readonly TimeSpan GracefulExitTimeout = TimeSpan.FromSeconds(15);

        public string Name { get; }
        public string FileName { get; }
        public IReadOnlyList<string> Arguments { get; }
        public string WorkingDirectory { get; }
        public StopMethod StopMethod { get; }

        public LaunchableProgram(string name, string fileName, StopMethod stopMethod, IReadOnlyList<string> arguments = null, string workingDirectory = "")
        {
            Name = name;
            FileName = fileName;
            StopMethod = stopMethod;
            Arguments = arguments ?? Array.Empty<string>();
            WorkingDirectory = workingDirectory;
        }

        public Process Start()
        {
            var process = new Process();
            process.StartInfo.FileName = FileName;
            foreach (string argument in Arguments)
                process.StartInfo.ArgumentList.Add(argument);
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardInput = StopMethod == StopMethod.StdinQuit;
            if (!string.IsNullOrEmpty(WorkingDirectory))
                process.StartInfo.WorkingDirectory = WorkingDirectory;
            process.Start();
            return process;
        }

        // Closes a process Start() returned, per StopMethod. A GPU process
        // that doesn't exit within GracefulExitTimeout of being asked is left
        // running (and logged) rather than killed - better an open window
        // to close by hand than a BSOD.
        public async Task StopAsync(Process process)
        {
            try
            {
                if (process.HasExited)
                    return;

                switch (StopMethod)
                {
                    case StopMethod.Kill:
                        process.Kill(entireProcessTree: true);
                        return;

                    case StopMethod.UnityQuitCommand:
                        if (!await TrySendUnityQuitAsync())
                            process.CloseMainWindow();
                        break;

                    case StopMethod.StdinQuit:
                        process.StandardInput.WriteLine("QUIT");
                        process.StandardInput.Close();
                        break;
                }

                if (!await WaitForExitAsync(process, GracefulExitTimeout))
                {
                    Debug.WriteLine(
                        $"[STE] '{Name}' did not exit within {GracefulExitTimeout.TotalSeconds}s of being asked to quit. " +
                        "Leaving it running rather than force-killing it (a GPU process - a past cause of a " +
                        "GPU driver crash/BSOD on this hardware) - close it manually.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[STE] Failed to stop '{Name}': {ex.Message}");
            }
        }

        private static async Task<bool> TrySendUnityQuitAsync()
        {
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(UnityHost, UnityCameraPort);
                byte[] bytes = Encoding.ASCII.GetBytes("QUIT\n");
                await client.GetStream().WriteAsync(bytes, 0, bytes.Length);
                return true;
            }
            catch (SocketException)
            {
                return false;
            }
        }

        // True if the process exited within the timeout. Shared with
        // HomePage's stop of the test runner.
        public static async Task<bool> WaitForExitAsync(Process process, TimeSpan timeout)
        {
            using var cts = new CancellationTokenSource(timeout);
            try
            {
                await process.WaitForExitAsync(cts.Token);
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }
    }

    public static class LaunchablePrograms
    {
        // STE_Test_Solution.vbproj wildcard-includes every script under
        // Test Scripts\, so editing or adding one requires rebuilding this
        // .sln before it takes effect. Shared by the "Build Test Solution"
        // entry below (manual, via Settings' Launch Programs button) and
        // HomePage.RunSelectedTests() (automatic, before every Run).
        public static readonly string TestSolutionSolutionPath = GetTestSolutionSolutionPath();

        public static readonly IReadOnlyList<LaunchableProgram> All = BuildList();

        private static string GetTestSolutionSolutionPath([CallerFilePath] string sourceFilePath = "")
        {
            // sourceFilePath = ...\SHIELD\Test Tools\STE\STE\LaunchablePrograms.cs
            string projectDirectory = Path.GetDirectoryName(sourceFilePath);
            string steToolsDirectory = Path.GetFullPath(Path.Combine(projectDirectory, ".."));
            return Path.Combine(steToolsDirectory, "STE_Test_Solution", "STE_Test_Solution.sln");
        }

        private static IReadOnlyList<LaunchableProgram> BuildList([CallerFilePath] string sourceFilePath = "")
        {
            // sourceFilePath = ...\SHIELD\Test Tools\STE\STE\LaunchablePrograms.cs
            string projectDirectory = Path.GetDirectoryName(sourceFilePath);
            string shieldDirectory = Path.GetFullPath(Path.Combine(projectDirectory, "..", "..", ".."));
            string shieldAppDirectory = Path.Combine(shieldDirectory, "SHIELD", "SHIELD");

            return new List<LaunchableProgram>
            {
                new LaunchableProgram(
                    name: "SHIELD Virtual Camera",
                    fileName: Path.Combine(shieldDirectory, "Test Tools", "SHIELD Virtual Camera", "SHIELD Virtual Camera.exe"),
                    stopMethod: StopMethod.UnityQuitCommand),

                // Matches README.md's "Running against the Unity virtual camera":
                //   cd "SHIELD\SHIELD"
                //   .venv\Scripts\python __main__.py 0 --source unity
                // plus --quit-on-stdin so Stop can close it without a kill.
                new LaunchableProgram(
                    name: "SHIELD Ground Control System",
                    fileName: Path.Combine(shieldAppDirectory, ".venv", "Scripts", "python.exe"),
                    stopMethod: StopMethod.StdinQuit,
                    arguments: new[] { "__main__.py", "0", "--source", "unity", "--quit-on-stdin" },
                    workingDirectory: shieldAppDirectory),

                // Manual equivalent of the automatic rebuild HomePage.
                // RunSelectedTests() does before every Run - lets a test
                // script's compile errors be checked without starting a run.
                new LaunchableProgram(
                    name: "Build Test Solution",
                    fileName: "dotnet",
                    stopMethod: StopMethod.Kill,
                    arguments: new[] { "build", TestSolutionSolutionPath, "-v", "minimal" }),
            };
        }
    }
}

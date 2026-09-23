using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.ApplicationSettings;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace STE
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class HomePage : Page, INotifyPropertyChanged
    {
        private static readonly string TestScriptsRoot = GetTestScriptsRoot();
        private const int MaxSubdirectoryDepth = 2;
        private const string ExcludedFileName = "Example_Test.vb";

        public event PropertyChangedEventHandler PropertyChanged;

        public ObservableCollection<BoolStringClass> TestList { get; set; }

        private bool _testScriptSelected;
        public bool TestScriptSelected
        {
            get => _testScriptSelected;
            private set
            {
                if (_testScriptSelected != value)
                {
                    _testScriptSelected = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TestScriptSelected)));
                }
            }
        }

        private bool _testScriptRunning;
        public bool TestScriptRunning
        {
            get => _testScriptRunning;
            private set
            {
                if (_testScriptRunning != value)
                {
                    _testScriptRunning = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TestScriptRunning)));
                    UpdateAnyRunning();
                }
            }
        }

        private bool _programsRunning;
        public bool ProgramsRunning
        {
            get => _programsRunning;
            private set
            {
                if (_programsRunning != value)
                {
                    _programsRunning = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ProgramsRunning)));
                    UpdateAnyRunning();
                }
            }
        }

        // Backs the Stop button's IsEnabled (either a test run or a manual
        // "Launch Programs" click can leave processes for Stop to kill) and
        // Launch Programs' IsEnabled (don't launch a second batch on top of
        // an already-running one).
        private bool _anyRunning;
        public bool AnyRunning
        {
            get => _anyRunning;
            private set
            {
                if (_anyRunning != value)
                {
                    _anyRunning = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AnyRunning)));
                }
            }
        }

        private void UpdateAnyRunning()
        {
            AnyRunning = TestScriptRunning || ProgramsRunning;
        }

        // The "dotnet build" RunSelectedTests() runs first - no GPU state, so
        // Stop may kill it outright.
        private Process _runningBuildProcess;

        // The STE_Test_Solution.exe run in progress, if any. Never killed
        // while it may still own Unity/Python - see StopTestRunnerAsync().
        private Process _runningTestProcess;

        private readonly List<(LaunchableProgram Program, Process Process)> _launchedPrograms = new List<(LaunchableProgram, Process)>();

        // Set while StopRunningTest() is waiting on processes to exit, so
        // Run/Launch Programs (or a second Stop) can't start a new Unity/
        // Python alongside ones still shutting down.
        private bool _stopInProgress;

        public HomePage()
        {
            InitializeComponent();

            TestList = new ObservableCollection<BoolStringClass>();
            foreach (string file in GetTestScriptFiles(TestScriptsRoot, MaxSubdirectoryDepth).OrderBy(f => f))
            {
                string relativePath = Path.GetRelativePath(TestScriptsRoot, file);
                string displayName = Path.ChangeExtension(relativePath, null);
                var testScript = new BoolStringClass { IsSelected = false, Text = displayName };
                testScript.PropertyChanged += TestScript_PropertyChanged;
                RefreshLastRunInfo(testScript);
                TestList.Add(testScript);
            }

            this.DataContext = this;
        }

        private void TestScript_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(BoolStringClass.IsSelected))
            {
                TestScriptSelected = TestList.Any(t => t.IsSelected);
            }
        }

        private static string GetTestScriptsRoot([CallerFilePath] string sourceFilePath = "")
        {
            // sourceFilePath = ...\SHIELD\Test Tools\STE\STE\HomePage.xaml.cs
            string projectDirectory = Path.GetDirectoryName(sourceFilePath);
            string shieldDirectory = Path.GetFullPath(Path.Combine(projectDirectory, "..", "..", ".."));
            return Path.Combine(shieldDirectory, "Test Scripts");
        }

        // Locates the compiled STE_Test_Solution.exe (the test dispatcher, see
        // Program.vb) next to this source file rather than hardcoding a
        // configuration, since STE and STE_Test_Solution are built separately
        // and may not share a Debug/Release build at any given time.
        private static string GetTestSolutionExePath([CallerFilePath] string sourceFilePath = "")
        {
            // sourceFilePath = ...\SHIELD\Test Tools\STE\STE\HomePage.xaml.cs
            string steToolsDirectory = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFilePath), ".."));
            string testSolutionBinDirectory = Path.Combine(steToolsDirectory, "STE_Test_Solution", "STE_Test_Solution", "bin");

            if (!Directory.Exists(testSolutionBinDirectory))
                return null;

            return Directory.EnumerateFiles(testSolutionBinDirectory, "STE_Test_Solution.exe", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }

        private static IEnumerable<string> GetTestScriptFiles(string rootPath, int maxDepth)
        {
            if (!Directory.Exists(rootPath))
                yield break;

            var directories = new Queue<(string Path, int Depth)>();
            directories.Enqueue((rootPath, 0));

            while (directories.Count > 0)
            {
                (string currentPath, int depth) = directories.Dequeue();

                foreach (string file in Directory.EnumerateFiles(currentPath, "*.vb"))
                {
                    if (!string.Equals(Path.GetFileName(file), ExcludedFileName, StringComparison.OrdinalIgnoreCase))
                        yield return file;
                }

                if (depth < maxDepth)
                {
                    foreach (string subDirectory in Directory.EnumerateDirectories(currentPath))
                        directories.Enqueue((subDirectory, depth + 1));
                }
            }
        }

        // Mirrors Program.vb's moduleName derivation: a test's log folder
        // (see Common_Test_Functions.BeginTest) is named after the script's
        // module name alone, not its full path relative to Test Scripts\ -
        // e.g. "AVS\AVS_Detection_Test" logs under "AVS_Detection_Test\".
        private static string GetModuleName(string testDisplayName)
        {
            return testDisplayName.Split('\\', '/').Last();
        }

        // Reads the most recent run's log for one test (if any) and updates
        // its LastDuration/LastResult for the ListBox's columns. Called once
        // per test at startup and again for each test right after it finishes
        // running, so the columns always reflect the last completed run.
        private static void RefreshLastRunInfo(BoolStringClass test)
        {
            string lastDuration = "";
            string lastResult = "";
            try
            {
                string testLogDirectory = Path.Combine(AppSettings.LogDirectory, GetModuleName(test.Text));
                string latestRunDirectory = Directory.Exists(testLogDirectory)
                    ? Directory.EnumerateDirectories(testLogDirectory).OrderByDescending(d => d).FirstOrDefault()
                    : null;

                if (latestRunDirectory != null)
                {
                    string logPath = Path.Combine(latestRunDirectory, GetModuleName(test.Text) + ".log");
                    if (File.Exists(logPath))
                    {
                        foreach (string line in File.ReadLines(logPath))
                        {
                            // "=== EndTest: <name> - PASS (Pass: 1, Fail: 0, Abort: 0, Total: 1) ==="
                            if (line.Contains("=== EndTest:"))
                            {
                                string[] parts = line.Split(" - ", 2, StringSplitOptions.None);
                                if (parts.Length == 2)
                                {
                                    string resultPart = parts[1].Trim();
                                    int spaceIndex = resultPart.IndexOf(' ');
                                    lastResult = spaceIndex >= 0 ? resultPart.Substring(0, spaceIndex) : resultPart;
                                }
                            }
                            // "Duration: hh:mm:ss"
                            else if (line.Contains("Duration: "))
                            {
                                int index = line.IndexOf("Duration: ", StringComparison.Ordinal);
                                lastDuration = line.Substring(index + "Duration: ".Length).Trim();
                            }
                        }
                    }
                }
            }
            catch (IOException) { }

            test.LastDuration = lastDuration;
            test.LastResult = lastResult;
        }

        private void OpenSettingsPage(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            this.Frame.Navigate(typeof(SettingsPage));
        }

        // Launches STE_Test_Solution.exe once per checked test, in order,
        // passing each test's name (its path relative to Test Scripts\,
        // without the extension) so Program.vb's dispatcher can find and
        // invoke that script's Sub Main. Runs are sequential rather than
        // parallel since the Unity-side TCP listeners (SceneSelector,
        // DroneSpawner, CameraStreamer) each accept only one connection at a
        // time.
        private async void RunSelectedTests(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            if (TestScriptRunning || _stopInProgress)
                return;

            List<string> selectedTests = TestList.Where(t => t.IsSelected).Select(t => t.Text).ToList();
            if (selectedTests.Count == 0)
                return;

            TestScriptRunning = true;
            try
            {
                // Rebuild first so a Run always reflects the latest edits to
                // any test script - STE_Test_Solution.vbproj wildcard-includes
                // every .vb file under Test Scripts\, so a stale build would
                // otherwise silently run old test-case code.
                bool buildSucceeded = await BuildTestSolution();
                if (!TestScriptRunning)
                    return; // Stop was pressed during the build

                if (!buildSucceeded)
                {
                    Debug.WriteLine("[HomePage] Failed to build STE_Test_Solution - see build output above.");
                    return;
                }

                string testSolutionExePath = GetTestSolutionExePath();
                if (testSolutionExePath == null)
                {
                    Debug.WriteLine("[HomePage] Could not find STE_Test_Solution.exe after build.");
                    return;
                }

                foreach (string testName in selectedTests)
                {
                    using (var process = new Process())
                    {
                        process.StartInfo.FileName = testSolutionExePath;
                        process.StartInfo.ArgumentList.Add(testName);
                        process.StartInfo.ArgumentList.Add(AppSettings.StartupDelaySeconds.ToString());
                        process.StartInfo.ArgumentList.Add(AppSettings.LogDirectory);
                        process.StartInfo.UseShellExecute = false;
                        // Stop's channel to the runner (Program.vb's
                        // StartStopListener()).
                        process.StartInfo.RedirectStandardInput = true;

                        _runningTestProcess = process;
                        process.Start();
                        await process.WaitForExitAsync();
                        _runningTestProcess = null;
                    }

                    BoolStringClass finishedTest = TestList.FirstOrDefault(t => t.Text == testName);
                    if (finishedTest != null)
                        RefreshLastRunInfo(finishedTest);

                    if (!TestScriptRunning)
                        break; // Stop was pressed
                }
            }
            finally
            {
                _runningBuildProcess = null;
                _runningTestProcess = null;
                TestScriptRunning = false;
            }
        }

        // Runs the same "dotnet build" LaunchablePrograms' "Build Test
        // Solution" entry does manually (Settings' Launch Programs button),
        // but automatically and unconditionally on every Run - independent
        // of whether that entry is checked in Settings, matching how the
        // Programs list elsewhere only ever gates the manual button.
        private async Task<bool> BuildTestSolution()
        {
            using (var process = new Process())
            {
                process.StartInfo.FileName = "dotnet";
                process.StartInfo.ArgumentList.Add("build");
                process.StartInfo.ArgumentList.Add(LaunchablePrograms.TestSolutionSolutionPath);
                process.StartInfo.ArgumentList.Add("-v");
                process.StartInfo.ArgumentList.Add("minimal");
                process.StartInfo.UseShellExecute = false;

                _runningBuildProcess = process;
                process.Start();
                await process.WaitForExitAsync();
                _runningBuildProcess = null;
                return process.ExitCode == 0;
            }
        }

        // Starts every program checked in Settings' program list directly
        // (Process.Start, not through STE_Test_Solution.exe), independent of
        // whatever's checked in TestList - this never runs a test script.
        private void LaunchPrograms(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            if (AnyRunning || _stopInProgress)
                return;

            List<LaunchableProgram> selectedPrograms = LaunchablePrograms.All
                .Where(p => AppSettings.IsProgramSelected(p.Name))
                .ToList();

            if (selectedPrograms.Count == 0)
                return;

            foreach (LaunchableProgram program in selectedPrograms)
            {
                try
                {
                    _launchedPrograms.Add((program, program.Start()));
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[HomePage] Failed to launch '{program.Name}': {ex.Message}");
                }
            }

            ProgramsRunning = _launchedPrograms.Count > 0;
        }

        // How long the test runner gets to close Unity/Python itself and
        // exit after "STOP" - comfortably more than its own worst case
        // (ClosePythonPipeline()'s 15s + CloseUnityPlayer()'s 10s, possibly
        // after the test thread's own in-progress close of the same).
        private static readonly TimeSpan TestRunnerStopTimeout = TimeSpan.FromSeconds(60);

        // Never kills anything holding a live GPU device (Unity's Direct3D
        // device, the Python pipeline's CUDA context) - TerminateProcess on
        // one has crashed the GPU driver (BSOD) on this project's hardware.
        // This used to CloseMainWindow() and then Kill(entireProcessTree) -
        // and since neither STE_Test_Solution.exe (a console app) nor the
        // venv's python.exe launcher has a main window, that meant an
        // immediate tree kill of the test runner along with the Unity/Python
        // it had launched. Now each process is asked to quit its own way (see
        // StopTestRunnerAsync() and LaunchableProgram.StopAsync()).
        private async void StopRunningTest(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            if (_stopInProgress)
                return;
            _stopInProgress = true;
            try
            {
                // Also what RunSelectedTests() checks to not start the next
                // selected test once this one's runner exits.
                TestScriptRunning = false;

                var stops = new List<Task>();

                Process build = _runningBuildProcess;
                if (build != null)
                {
                    try { build.Kill(entireProcessTree: true); }
                    catch (Exception ex) { Debug.WriteLine($"[HomePage] Failed to stop the build: {ex.Message}"); }
                }

                Process testRunner = _runningTestProcess;
                if (testRunner != null)
                    stops.Add(StopTestRunnerAsync(testRunner));

                foreach ((LaunchableProgram program, Process process) in _launchedPrograms)
                    stops.Add(program.StopAsync(process));
                _launchedPrograms.Clear();

                await Task.WhenAll(stops);
                ProgramsRunning = false;
            }
            finally
            {
                _stopInProgress = false;
            }
        }

        // Asks the test runner to stop (Program.vb's StartStopListener() ->
        // Common_Test_Functions.StopTest()), which closes Unity/Python
        // gracefully, logs the run as STOPPED and exits. If it somehow hasn't
        // exited within TestRunnerStopTimeout, kills the runner process
        // ALONE (it holds no GPU state itself) - never its tree, so any
        // Unity/Python it couldn't close are left running to close by hand.
        private static async Task StopTestRunnerAsync(Process testRunner)
        {
            try
            {
                if (testRunner.HasExited)
                    return;

                testRunner.StandardInput.WriteLine("STOP");
                testRunner.StandardInput.Flush();

                if (!await LaunchableProgram.WaitForExitAsync(testRunner, TestRunnerStopTimeout))
                {
                    Debug.WriteLine(
                        $"[HomePage] Test runner did not exit within {TestRunnerStopTimeout.TotalSeconds}s of STOP - " +
                        "killing it alone (not its Unity/Python children; close those manually if still open).");
                    testRunner.Kill(entireProcessTree: false);
                }
            }
            catch (InvalidOperationException)
            {
                // RunSelectedTests() already saw it exit and disposed it.
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HomePage] Failed to stop the test runner: {ex.Message}");
            }
        }

        public class BoolStringClass : INotifyPropertyChanged
        {
            public event PropertyChangedEventHandler PropertyChanged;

            public string Text { get; set; }

            private bool _isSelected;
            public bool IsSelected
            {
                get => _isSelected;
                set
                {
                    if (_isSelected != value)
                    {
                        _isSelected = value;
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
                    }
                }
            }

            // How long the last completed run of this test took (hh:mm:ss,
            // from Common_Test_Functions.EndTest's log line), and whether
            // that run's overall result was PASS, FAIL, or ABORT. Both are
            // "" until a run has actually completed with a log to read.
            private string _lastDuration = "";
            public string LastDuration
            {
                get => _lastDuration;
                set
                {
                    if (_lastDuration != value)
                    {
                        _lastDuration = value;
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LastDuration)));
                    }
                }
            }

            private string _lastResult = "";
            public string LastResult
            {
                get => _lastResult;
                set
                {
                    if (_lastResult != value)
                    {
                        _lastResult = value;
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LastResult)));
                    }
                }
            }
        }
    }
}

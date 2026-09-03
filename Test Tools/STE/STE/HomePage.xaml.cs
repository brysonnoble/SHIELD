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
        private static readonly string TestSolutionExePath = GetTestSolutionExePath();
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
                }
            }
        }

        private Process _runningProcess;

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
            if (TestScriptRunning)
                return;

            if (TestSolutionExePath == null)
            {
                Debug.WriteLine("[HomePage] Could not find STE_Test_Solution.exe. Build the STE_Test_Solution project first.");
                return;
            }

            List<string> selectedTests = TestList.Where(t => t.IsSelected).Select(t => t.Text).ToList();
            if (selectedTests.Count == 0)
                return;

            TestScriptRunning = true;
            try
            {
                foreach (string testName in selectedTests)
                {
                    using (var process = new Process())
                    {
                        process.StartInfo.FileName = TestSolutionExePath;
                        process.StartInfo.ArgumentList.Add(testName);
                        process.StartInfo.ArgumentList.Add(AppSettings.StartupDelaySeconds.ToString());
                        process.StartInfo.UseShellExecute = false;

                        _runningProcess = process;
                        process.Start();
                        await process.WaitForExitAsync();
                    }

                    if (!TestScriptRunning)
                        break; // Stop was pressed
                }
            }
            finally
            {
                _runningProcess = null;
                TestScriptRunning = false;
            }
        }

        private void StopRunningTest(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            TestScriptRunning = false;
            try { _runningProcess?.Kill(entireProcessTree: true); } catch { }
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
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;

namespace STE
{
    // One program the "Launch Programs" button can start directly (without
    // going through STE_Test_Solution.exe / a test script). Mirrors the
    // Unity/Python processes Common_Test_Functions.vb's BeginTest() launches,
    // so a test run and a manual "Launch Programs" click start the same
    // things the same way.
    public class LaunchableProgram
    {
        public string Name { get; }
        public string FileName { get; }
        public IReadOnlyList<string> Arguments { get; }
        public string WorkingDirectory { get; }

        public LaunchableProgram(string name, string fileName, IReadOnlyList<string> arguments = null, string workingDirectory = "")
        {
            Name = name;
            FileName = fileName;
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
            if (!string.IsNullOrEmpty(WorkingDirectory))
                process.StartInfo.WorkingDirectory = WorkingDirectory;
            process.Start();
            return process;
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
                    fileName: Path.Combine(shieldDirectory, "Test Tools", "SHIELD Virtual Camera", "SHIELD Virtual Camera.exe")),

                // Matches README.md's "Running against the Unity virtual camera":
                //   cd "SHIELD\SHIELD"
                //   .venv\Scripts\python __main__.py 0 --source unity
                new LaunchableProgram(
                    name: "SHIELD Ground Control System",
                    fileName: Path.Combine(shieldAppDirectory, ".venv", "Scripts", "python.exe"),
                    arguments: new[] { "__main__.py", "0", "--source", "unity" },
                    workingDirectory: shieldAppDirectory),

                // Manual equivalent of the automatic rebuild HomePage.
                // RunSelectedTests() does before every Run - lets a test
                // script's compile errors be checked without starting a run.
                new LaunchableProgram(
                    name: "Build Test Solution",
                    fileName: "dotnet",
                    arguments: new[] { "build", TestSolutionSolutionPath, "-v", "minimal" }),
            };
        }
    }
}

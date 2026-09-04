using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace STE
{
    // Simple persisted app settings, stored as plain files under %LOCALAPPDATA%
    // rather than Windows.Storage.ApplicationData - that API needs package
    // identity, which this unpackaged (WindowsAppSDKSelfContained) app doesn't
    // have.
    public static class AppSettings
    {
        public const int DefaultStartupDelaySeconds = 15;

        private static readonly string SettingsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SHIELD STE");
        private static readonly string StartupDelayPath = Path.Combine(SettingsDirectory, "StartupDelaySeconds.txt");
        private static readonly string SelectedProgramsPath = Path.Combine(SettingsDirectory, "SelectedPrograms.txt");

        // Seconds STE_Test_Solution.exe waits, after launching Unity/Python
        // and confirming Unity's TCP listeners are up, before a test's first
        // test case runs - gives everything else (Python's model load, its
        // OpenCV preview window, Unity's scene) time to finish opening.
        public static int StartupDelaySeconds
        {
            get
            {
                try
                {
                    if (File.Exists(StartupDelayPath)
                        && int.TryParse(File.ReadAllText(StartupDelayPath).Trim(), out int value)
                        && value >= 0)
                    {
                        return value;
                    }
                }
                catch (IOException) { }
                return DefaultStartupDelaySeconds;
            }
            set
            {
                Directory.CreateDirectory(SettingsDirectory);
                File.WriteAllText(StartupDelayPath, value.ToString());
            }
        }

        // Whether a LaunchableProgram (identified by its Name) is checked in
        // Settings' program list, and so gets started by HomePage's "Launch
        // Programs" button. A program not yet recorded (new, or the file
        // doesn't exist yet) defaults to selected.
        public static bool IsProgramSelected(string programName)
        {
            Dictionary<string, bool> selections = ReadProgramSelections();
            return !selections.TryGetValue(programName, out bool isSelected) || isSelected;
        }

        // allProgramNames is every currently known LaunchableProgram's Name
        // (LaunchablePrograms.All), so the file always records a state for
        // each of them, not just the one being changed.
        public static void SetProgramSelected(string programName, bool isSelected, IEnumerable<string> allProgramNames)
        {
            Dictionary<string, bool> selections = ReadProgramSelections();
            foreach (string name in allProgramNames)
            {
                if (!selections.ContainsKey(name))
                    selections[name] = true;
            }
            selections[programName] = isSelected;

            Directory.CreateDirectory(SettingsDirectory);
            File.WriteAllLines(SelectedProgramsPath, selections.Select(kv => $"{kv.Key}={kv.Value}"));
        }

        private static Dictionary<string, bool> ReadProgramSelections()
        {
            var selections = new Dictionary<string, bool>();
            try
            {
                if (File.Exists(SelectedProgramsPath))
                {
                    foreach (string line in File.ReadAllLines(SelectedProgramsPath))
                    {
                        string[] parts = line.Split('=', 2);
                        if (parts.Length == 2 && bool.TryParse(parts[1], out bool value))
                            selections[parts[0]] = value;
                    }
                }
            }
            catch (IOException) { }
            return selections;
        }
    }
}

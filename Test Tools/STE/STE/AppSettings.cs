using System;
using System.IO;

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
    }
}

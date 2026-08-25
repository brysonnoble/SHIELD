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

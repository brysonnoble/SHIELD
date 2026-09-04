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
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace STE
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class SettingsPage : Page
    {
        private bool _loaded;

        public ObservableCollection<ProgramSelection> Programs { get; }

        public SettingsPage()
        {
            InitializeComponent();
            StartupDelayTextBox.Text = AppSettings.StartupDelaySeconds.ToString();

            Programs = new ObservableCollection<ProgramSelection>(
                LaunchablePrograms.All.Select(p => new ProgramSelection(p.Name)));

            this.DataContext = this;
            _loaded = true;
        }

        private void StartupDelayTextBox_TextChanged(object sender, Microsoft.UI.Xaml.Controls.TextChangedEventArgs e)
        {
            if (!_loaded)
                return;

            if (int.TryParse(StartupDelayTextBox.Text, out int value) && value >= 0)
            {
                AppSettings.StartupDelaySeconds = value;
            }
        }

        private void OpenHomePage(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            this.Frame.Navigate(typeof(HomePage));
        }

        public class ProgramSelection : INotifyPropertyChanged
        {
            public event PropertyChangedEventHandler PropertyChanged;

            public string Name { get; }

            private bool _isSelected;
            public bool IsSelected
            {
                get => _isSelected;
                set
                {
                    if (_isSelected != value)
                    {
                        _isSelected = value;
                        AppSettings.SetProgramSelected(Name, value, LaunchablePrograms.All.Select(p => p.Name));
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
                    }
                }
            }

            public ProgramSelection(string name)
            {
                Name = name;
                _isSelected = AppSettings.IsProgramSelected(name);
            }
        }
    }
}

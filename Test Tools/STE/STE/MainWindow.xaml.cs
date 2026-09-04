using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.Foundation;
using Windows.Foundation.Collections;
using WinRT.Interop;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace STE
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        public bool TestScriptSelected { get; set; } = false;
        public bool TestScriptRunning { get; set; } = false;

        public MainWindow()
        {
            InitializeComponent();

            // Unpackaged (WindowsAppSDKSelfContained) apps don't pick up
            // Package.appxmanifest's tile/logo assets, so the title bar and
            // taskbar icon need to be set explicitly here rather than via
            // the manifest - <ApplicationIcon> in STE.csproj covers the
            // Explorer file icon, this covers the running window.
            IntPtr windowHandle = WindowNative.GetWindowHandle(this);
            WindowId windowId = Win32Interop.GetWindowIdFromWindow(windowHandle);
            AppWindow appWindow = AppWindow.GetFromWindowId(windowId);
            appWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));

            // Navigate the frame to the HomePage on startup
            RootFrame.Navigate(typeof(HomePage));
        }
    }
}

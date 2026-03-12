using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace SoftcurseMediaLabAI
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            ModernWpf.ThemeManager.Current.ApplicationTheme = ModernWpf.ApplicationTheme.Dark;

            // Fix ComboBox white background (ModernWpf templateRoot gradient override)
            EventManager.RegisterClassHandler(
                typeof(ComboBox),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler((s, args) =>
                {
                    if (s is ComboBox cb)
                        Application.Current.Dispatcher.BeginInvoke(new Action(() => FixComboBox(cb)));
                }));

            // Dark title bar for all windows
            EventManager.RegisterClassHandler(
                typeof(Window),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler((s, args) =>
                {
                    if (s is Window w)
                        SetDarkTitleBar(w);
                }));
        }

        private void FixComboBox(ComboBox cb)
        {
            var toggleButton = GetVisualChild<ToggleButton>(cb);
            if (toggleButton == null) return;

            var templateRoot = toggleButton.Template?.FindName("templateRoot", toggleButton) as Border;
            if (templateRoot != null)
            {
                templateRoot.Background = new SolidColorBrush(Color.FromRgb(0x0A, 0x15, 0x20));
                templateRoot.BorderBrush = new SolidColorBrush(Color.FromRgb(0x1A, 0x6A, 0x9A));
            }
        }

        private void SetDarkTitleBar(Window window)
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;

            // Enable dark mode
            int useImmersiveDarkMode = 1;
            DwmSetWindowAttribute(hwnd, 20, ref useImmersiveDarkMode, sizeof(int));
            DwmSetWindowAttribute(hwnd, 19, ref useImmersiveDarkMode, sizeof(int));

            // Set title bar background to #060D14 (COLORREF is BGR not RGB)
            int color = 0x00140D06; // BGR format of #060D14
            DwmSetWindowAttribute(hwnd, 35, ref color, sizeof(int)); // DWMWA_CAPTION_COLOR = 35

            // Set title bar text color to #9AC8D8
            int textColor = 0x00D8C89A; // BGR format of #9AC8D8
            DwmSetWindowAttribute(hwnd, 36, ref textColor, sizeof(int)); // DWMWA_TEXT_COLOR = 36
        }

        private T? GetVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T t) return t;
                var result = GetVisualChild<T>(child);
                if (result != null) return result;
            }
            return null;
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
    }
}

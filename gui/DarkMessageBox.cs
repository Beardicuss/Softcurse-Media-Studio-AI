using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace SoftcurseMediaLabAI
{
    /// <summary>
    /// Dark-themed MessageBox replacement matching the HUD cyberpunk theme.
    /// Drop-in for MessageBox.Show() calls.
    /// </summary>
    public static class DarkMessageBox
    {
        public static MessageBoxResult Show(string message, string title = "SYSTEM MESSAGE",
            MessageBoxButton buttons = MessageBoxButton.OK,
            MessageBoxImage icon = MessageBoxImage.None)
        {
            var result = MessageBoxResult.OK;

            var win = new Window
            {
                Title = title,
                Width = 460,
                Height = 220,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = new SolidColorBrush(Color.FromRgb(0x06, 0x0D, 0x14)),
                Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0xC8, 0xD8)),
                FontFamily = new FontFamily("Segoe UI"),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x1A, 0x6A, 0x9A)),
                BorderThickness = new Thickness(1),
            };

            // Try to set owner to active window
            try { win.Owner = Application.Current.MainWindow; } catch { }

            var rootGrid = new Grid();
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36) }); // Title bar
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Message
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(50) }); // Buttons

            // Title bar
            var titleBar = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x07, 0x10, 0x1A)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x0D, 0x3A, 0x5C)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(14, 0, 14, 0)
            };
            titleBar.MouseLeftButtonDown += (s, e) => win.DragMove();

            var titleGrid = new Grid();
            var titleText = new TextBlock
            {
                Text = title,
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xE5, 0xFF)),
                VerticalAlignment = VerticalAlignment.Center,
                Effect = new DropShadowEffect { Color = Color.FromRgb(0x00, 0xE5, 0xFF), BlurRadius = 8, ShadowDepth = 0, Opacity = 0.5 }
            };
            var closeBtn = new Button
            {
                Content = "✕",
                Width = 28, Height = 28,
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush(Color.FromRgb(0x3A, 0x7A, 0x8A)),
                BorderThickness = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            closeBtn.Click += (s, e) => { result = MessageBoxResult.Cancel; win.Close(); };

            titleGrid.Children.Add(titleText);
            titleGrid.Children.Add(closeBtn);
            titleBar.Child = titleGrid;
            Grid.SetRow(titleBar, 0);
            rootGrid.Children.Add(titleBar);

            // Message area
            var msgBorder = new Border { Padding = new Thickness(20, 14, 20, 14) };
            var iconPrefix = icon switch
            {
                MessageBoxImage.Error => "⚠ ",
                MessageBoxImage.Warning => "⚡ ",
                MessageBoxImage.Information => "ℹ ",
                MessageBoxImage.Question => "? ",
                _ => ""
            };
            var msgText = new TextBlock
            {
                Text = iconPrefix + message,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0xC8, 0xD8)),
                VerticalAlignment = VerticalAlignment.Center
            };
            msgBorder.Child = msgText;
            Grid.SetRow(msgBorder, 1);
            rootGrid.Children.Add(msgBorder);

            // Button area
            var btnPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 16, 0)
            };

            Button MakeBtn(string text, MessageBoxResult res, bool primary = false)
            {
                var b = new Button
                {
                    Content = text,
                    MinWidth = 90, Height = 30,
                    Margin = new Thickness(6, 0, 0, 0),
                    FontSize = 11, FontWeight = FontWeights.Bold,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    FontFamily = new FontFamily("Segoe UI"),
                    Padding = new Thickness(14, 0, 14, 0)
                };
                if (primary)
                {
                    b.Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x00, 0xE5, 0xFF));
                    b.Foreground = new SolidColorBrush(Color.FromRgb(0x06, 0x0D, 0x14));
                    b.BorderBrush = new SolidColorBrush(Color.FromRgb(0x00, 0xE5, 0xFF));
                    b.BorderThickness = new Thickness(1);
                }
                else
                {
                    b.Background = Brushes.Transparent;
                    b.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xE5, 0xFF));
                    b.BorderBrush = new SolidColorBrush(Color.FromRgb(0x1A, 0x6A, 0x9A));
                    b.BorderThickness = new Thickness(1);
                }
                b.Click += (s, e) => { result = res; win.Close(); };
                return b;
            }

            switch (buttons)
            {
                case MessageBoxButton.OK:
                    btnPanel.Children.Add(MakeBtn("OK", MessageBoxResult.OK, true));
                    break;
                case MessageBoxButton.OKCancel:
                    btnPanel.Children.Add(MakeBtn("CANCEL", MessageBoxResult.Cancel));
                    btnPanel.Children.Add(MakeBtn("OK", MessageBoxResult.OK, true));
                    break;
                case MessageBoxButton.YesNo:
                    btnPanel.Children.Add(MakeBtn("NO", MessageBoxResult.No));
                    btnPanel.Children.Add(MakeBtn("YES", MessageBoxResult.Yes, true));
                    break;
                case MessageBoxButton.YesNoCancel:
                    btnPanel.Children.Add(MakeBtn("CANCEL", MessageBoxResult.Cancel));
                    btnPanel.Children.Add(MakeBtn("NO", MessageBoxResult.No));
                    btnPanel.Children.Add(MakeBtn("YES", MessageBoxResult.Yes, true));
                    break;
            }

            Grid.SetRow(btnPanel, 2);
            rootGrid.Children.Add(btnPanel);

            win.Content = rootGrid;
            win.ShowDialog();
            return result;
        }
    }
}

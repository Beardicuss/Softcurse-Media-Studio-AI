using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace SoftcurseMediaLabAI.Views
{
    public partial class FaqPage : UserControl
    {
        public FaqPage() => InitializeComponent();

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (FaqSections is null || SearchHint is null) return;
            string query = SearchBox.Text.Trim();
            SearchHint.Visibility = query.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            string[] terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            int matches = 0;

            foreach (Border card in FaqSections.Children.OfType<Border>())
            {
                string searchable = $"{card.Tag} {CollectText(card)}";
                bool visible = terms.Length == 0 || terms.All(term =>
                    searchable.Contains(term, StringComparison.OrdinalIgnoreCase));
                card.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                if (visible) matches++;
            }

            NoResultsText.Visibility = matches == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private static string CollectText(DependencyObject root)
        {
            string result = root is TextBlock text ? text.Text + " " : string.Empty;
            for (int index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); index++)
                result += CollectText(System.Windows.Media.VisualTreeHelper.GetChild(root, index));
            return result;
        }

        private void ClearSearch_Click(object sender, RoutedEventArgs e)
        {
            SearchBox.Clear();
            SearchBox.Focus();
        }

        private void OpenSettings_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow window)
                window.OpenSettings();
        }
    }
}

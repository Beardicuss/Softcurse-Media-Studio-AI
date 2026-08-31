using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SoftcurseMediaLabAI
{
    public enum UiStatusKind { Ready, Working, Success, Warning, Error }

    public static class UiStatus
    {
        public static void Set(TextBlock target, string message, UiStatusKind kind = UiStatusKind.Ready)
        {
            string brushKey = kind switch
            {
                UiStatusKind.Success => "SuccessBrush",
                UiStatusKind.Warning => "GoldAccentBrush",
                UiStatusKind.Error => "CyberMagentaBrush",
                UiStatusKind.Working => "CyberAccentBrush",
                _ => "TextBrush"
            };
            target.Text = message;
            if (Application.Current?.TryFindResource(brushKey) is Brush brush)
                target.Foreground = brush;
        }
    }
}

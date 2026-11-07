using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Retune.App.Controls;

/// <summary>WPF-UI's caption buttons carry automation ids but no names; screen readers need the names.</summary>
internal static class TitleBarAccessibility
{
    private static readonly Dictionary<string, string> Names = new(StringComparer.Ordinal)
    {
        ["TitleBarMinimizeButton"] = "Minimize",
        ["TitleBarMaximizeButton"] = "Maximize",
        ["TitleBarRestoreButton"] = "Restore",
        ["TitleBarCloseButton"] = "Close",
        ["TitleBarHelpButton"] = "Help",
    };

    public static void NameCaptionButtons(DependencyObject root)
    {
        ArgumentNullException.ThrowIfNull(root);
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ButtonBase button
                && Names.TryGetValue(AutomationProperties.GetAutomationId(button), out var name)
                && string.IsNullOrEmpty(AutomationProperties.GetName(button)))
            {
                AutomationProperties.SetName(button, name);
            }

            NameCaptionButtons(child);
        }
    }
}

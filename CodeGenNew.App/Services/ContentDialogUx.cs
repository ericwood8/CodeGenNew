using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace CodeGenNew.App.Services;

/// <summary>
/// Applies the project-wide dialog conventions (access keys, Escape/Enter behavior, default-button focus) to any ContentDialog, standard
/// footer buttons or ad-hoc ones alike:
///   - Access keys (Alt+letter) on the standard Primary/Secondary/Close footer buttons, reached by name
///     through the visual tree since ContentDialog only exposes those buttons as template parts, not as
///     named fields (WinUI3's default ContentDialog template names them "PrimaryButton"/
///     "SecondaryButton"/"CloseButton" -- unchanged since UWP, and the standard way to reach into a
///     ContentDialog's own footer). WinUI3 shows the access-key underline only while Alt is held, same as
///     Win32/WPF's own default; a permanently-bold letter in the caption would need custom (non-string)
///     footer buttons entirely, replacing ContentDialog's built-in PrimaryButtonText/etc.
///   - Initial keyboard focus on whichever button matches DefaultButton (or the single button, for a
///     dialog with only CloseButtonText set).
///   - Escape-closes/Enter-submits already come for free from ContentDialog itself (Escape always
///     invokes Close; Enter invokes DefaultButton even from inside a focused TextBox) -- nothing to do.
/// </summary>
internal static class ContentDialogUx
{
    public static void Apply(ContentDialog dialog, string? primaryAccessKey = null, string? secondaryAccessKey = null, string? closeAccessKey = null)
    {
        dialog.Opened += (_, _) =>
        {
            var primary = FindButton(dialog, "PrimaryButton");
            var secondary = FindButton(dialog, "SecondaryButton");
            var close = FindButton(dialog, "CloseButton");

            if (primary is not null && primaryAccessKey is not null)
                primary.AccessKey = primaryAccessKey;
            if (secondary is not null && secondaryAccessKey is not null)
                secondary.AccessKey = secondaryAccessKey;
            if (close is not null && closeAccessKey is not null)
                close.AccessKey = closeAccessKey;

            var toFocus = dialog.DefaultButton switch
            {
                ContentDialogButton.Primary => primary,
                ContentDialogButton.Secondary => secondary,
                ContentDialogButton.Close => close,
                _ => null
            } ?? close ?? primary; // single-button (Close-only) dialogs land here.

            toFocus?.Focus(FocusState.Programmatic);
        };
    }

    private static Button? FindButton(DependencyObject root, string name)
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is Button button && button.Name == name)
                return button;

            var found = FindButton(child, name);
            if (found is not null)
                return found;
        }
        return null;
    }
}

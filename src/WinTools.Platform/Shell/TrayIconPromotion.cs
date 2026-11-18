using Microsoft.Win32;

namespace WinTools.Platform.Shell;

/// <summary>
/// Windows 11 parks new notification icons in the overflow menu. Explorer records each icon under
/// HKCU\Control Panel\NotifyIconSettings\&lt;id&gt; (keyed by IconGuid) and shows it on the taskbar
/// when IsPromoted is 1, picking up changes live.
/// </summary>
public static class TrayIconPromotion
{
    private const string SettingsKey = @"Control Panel\NotifyIconSettings";
    private const string PromotedValue = "IsPromoted";

    /// <summary>
    /// Shows the icon on the taskbar unless the user has already chosen a visibility for it.
    /// Returns false while Explorer has not registered the icon yet, so callers can retry later.
    /// </summary>
    public static bool PromoteUnlessUserChose(Guid iconGuid)
    {
        using var settings = Registry.CurrentUser.OpenSubKey(SettingsKey);
        if (settings is null)
        {
            return true; // not Windows 11: nothing to do
        }
        var wanted = iconGuid.ToString("B");
        foreach (var name in settings.GetSubKeyNames())
        {
            using var icon = settings.OpenSubKey(name, writable: true);
            if (icon?.GetValue("IconGuid") is not string guid ||
                !string.Equals(guid, wanted, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (icon.GetValue(PromotedValue) is null)
            {
                icon.SetValue(PromotedValue, 1, RegistryValueKind.DWord);
            }
            return true;
        }
        return false;
    }
}

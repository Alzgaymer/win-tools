using Microsoft.Win32;

namespace WinTools.Platform.Shell;

public static class TaskbarTheme
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    /// <summary>True when the taskbar uses the light theme (dark icons); Windows 10 and earlier default to dark.</summary>
    public static bool IsLight
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue("SystemUsesLightTheme") is int value && value != 0;
        }
    }
}

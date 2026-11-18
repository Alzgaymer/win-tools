using Microsoft.Win32;
using WinTools.Core.Battery;

namespace WinTools.Platform.Battery;

/// <summary>Persists the tray icon style under HKCU\Software\WinTools.</summary>
public static class BatteryIconSettings
{
    private const string Key = @"Software\WinTools";
    private const string ValueName = "BatteryIconStyle";

    public static BatteryIconStyle Style
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(Key);
            return key?.GetValue(ValueName) is string text && Enum.TryParse<BatteryIconStyle>(text, out var style)
                ? style
                : BatteryIconStyle.GlyphAndBar;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(Key);
            key.SetValue(ValueName, value.ToString());
        }
    }
}

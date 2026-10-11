namespace WinTools.Core.Shell;

public readonly record struct PixelRect(int X, int Y, int Width, int Height);

public static class FlyoutPlacement
{
    public const double WidthDip = 360;
    public const double MarginDip = 12;

    /// <summary>
    /// Places the flyout at the bottom-right corner of <paramref name="workArea"/> (physical pixels),
    /// converting effective pixels with <paramref name="scale"/> (DPI / 96).
    /// </summary>
    public static PixelRect BottomRight(PixelRect workArea, double scale, double contentHeightDip)
    {
        var width = (int)Math.Round(WidthDip * scale);
        var margin = (int)Math.Round(MarginDip * scale);
        var height = Math.Min((int)Math.Round(contentHeightDip * scale), workArea.Height - 2 * margin);

        return new PixelRect(
            workArea.X + workArea.Width - width - margin,
            workArea.Y + workArea.Height - height - margin,
            width,
            height);
    }
}

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using WinTools.Core.Battery;
using WinTools.Platform.Shell;

namespace WinTools.App.Features.Battery;

/// <summary>
/// Draws tray icons from the Windows icon font (the glyphs Settings uses for Mouse, Headphones, Keyboard),
/// monochrome in the taskbar's text color, at the exact small-icon size so nothing is rescaled.
/// </summary>
internal static class BatteryIconRenderer
{
    private const int LowPercent = 20;

    // Same code points in Segoe Fluent Icons (Windows 11) and Segoe MDL2 Assets (Windows 10).
    private const string MouseGlyph = "";
    private const string HeadphoneGlyph = "";
    private const string KeyboardGlyph = "";

    private static readonly string IconFont = PickFont("Segoe Fluent Icons", "Segoe MDL2 Assets");

    // Windows 11 shell text face, falling back to Windows 10's.
    private static readonly string NumberFont = PickFont("Segoe UI Variable Text Semibold", "Segoe UI Semibold");

    public sealed record Palette(Color Normal, Color Low, Color Charging, Color Stale);

    public static Palette CurrentPalette() => TaskbarTheme.IsLight
        ? new Palette(Color.FromArgb(0x1B, 0x1B, 0x1B), Color.FromArgb(0xC4, 0x2B, 0x1C), Color.FromArgb(0x0F, 0x7B, 0x0F), Color.FromArgb(0x80, 0x1B, 0x1B, 0x1B))
        : new Palette(Color.White, Color.FromArgb(0xFF, 0x99, 0xA4), Color.FromArgb(0x6C, 0xCB, 0x5F), Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF));

    public static int CurrentSize() => (int)Math.Round(16 * DisplayScale.ForPrimaryMonitor());

    public static Icon Render(DeviceKind kind, int? percent, bool charging, bool stale, BatteryIconStyle style, Palette palette, int size)
    {
        var color = stale || percent is null ? palette.Stale
            : charging ? palette.Charging
            : percent <= LowPercent ? palette.Low
            : palette.Normal;

        using var bitmap = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using var brush = new SolidBrush(color);
            var glyph = Glyph(kind);

            switch (style)
            {
                case BatteryIconStyle.GlyphOnly:
                    {
                        using var font = new Font(IconFont, size, FontStyle.Regular, GraphicsUnit.Pixel);
                        DrawCentered(g, glyph, font, brush, new RectangleF(0, 0, size, size));
                        break;
                    }
                case BatteryIconStyle.Percent:
                    DrawFittedText(g, percent?.ToString() ?? "?", brush, size);
                    break;
                default:
                    DrawGlyphWithBar(g, glyph, percent, stale ? palette.Stale : palette.Normal, color, size);
                    break;
            }
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var borrowed = Icon.FromHandle(handle);
            return (Icon)borrowed.Clone();
        }
        finally
        {
            IconHandle.Destroy(handle);
        }
    }

    /// <summary>
    /// Device glyph in the text color, above a thin level bar that carries the status color
    /// (red when low, green while charging) over a faint full-width track.
    /// </summary>
    private static void DrawGlyphWithBar(Graphics g, string glyph, int? percent, Color glyphColor, Color barColor, int size)
    {
        var unit = Math.Max(1, (int)MathF.Round(size / 16f));
        var barHeight = 2 * unit;
        var glyphHeight = size - barHeight - unit;

        using (var font = new Font(IconFont, glyphHeight, FontStyle.Regular, GraphicsUnit.Pixel))
        using (var glyphBrush = new SolidBrush(glyphColor))
        {
            DrawCentered(g, glyph, font, glyphBrush, new RectangleF(0, 0, size, glyphHeight));
        }

        var smoothing = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.None;
        var barTop = size - barHeight;
        var barWidth = size - 2 * unit;
        using (var track = new SolidBrush(Color.FromArgb(0x40, glyphColor.R, glyphColor.G, glyphColor.B)))
        {
            g.FillRectangle(track, unit, barTop, barWidth, barHeight);
        }
        if (percent is { } p && p > 0)
        {
            var filled = Math.Max(unit, (int)MathF.Round(barWidth * Math.Min(p, 100) / 100f));
            using var fill = new SolidBrush(barColor);
            g.FillRectangle(fill, unit, barTop, filled, barHeight);
        }
        g.SmoothingMode = smoothing;
    }

    private static string Glyph(DeviceKind kind) => kind switch
    {
        DeviceKind.Headset => HeadphoneGlyph,
        DeviceKind.Keyboard => KeyboardGlyph,
        _ => MouseGlyph,
    };

    private static void DrawCentered(Graphics g, string text, Font font, Brush brush, RectangleF box)
    {
        using var format = new StringFormat(StringFormat.GenericTypographic)
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip,
        };
        g.DrawString(text, font, brush, box, format);
    }

    /// <summary>
    /// Hinted text at the taskbar's own text size (like the input-language label), stepping the
    /// size down only when the text is wider than the icon ("100").
    /// </summary>
    private static void DrawFittedText(Graphics g, string text, Brush brush, int size)
    {
        var pixels = MathF.Round(size * 0.75f);
        while (true)
        {
            using var font = new Font(NumberFont, pixels, FontStyle.Regular, GraphicsUnit.Pixel);
            if (pixels <= size * 0.5f || g.MeasureString(text, font, PointF.Empty, StringFormat.GenericTypographic).Width <= size)
            {
                DrawCentered(g, text, font, brush, new RectangleF(0, 0, size, size + 1));
                return;
            }
            pixels--;
        }
    }

    private static string PickFont(params string[] candidates)
    {
        using var installed = new InstalledFontCollection();
        return candidates.FirstOrDefault(c => installed.Families.Any(f => f.Name == c)) ?? candidates[^1];
    }
}

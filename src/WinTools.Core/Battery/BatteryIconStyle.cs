namespace WinTools.Core.Battery;

public enum BatteryIconStyle
{
    /// <summary>Device glyph above a thin battery level bar; the percentage is in the tooltip.</summary>
    GlyphAndBar,

    /// <summary>Full-size device glyph; the percentage is in the tooltip.</summary>
    GlyphOnly,

    /// <summary>The battery percentage as text; the device name is in the tooltip.</summary>
    Percent,
}

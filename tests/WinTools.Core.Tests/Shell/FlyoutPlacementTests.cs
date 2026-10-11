using WinTools.Core.Shell;

namespace WinTools.Core.Tests.Shell;

public class FlyoutPlacementTests
{
    [Fact]
    public void Scale100_TaskbarAtBottom_SitsAboveTaskbarAtRight()
    {
        var r = FlyoutPlacement.BottomRight(new PixelRect(0, 0, 1920, 1032), scale: 1.0, contentHeightDip: 200);

        Assert.Equal(new PixelRect(1920 - 360 - 12, 1032 - 200 - 12, 360, 200), r);
    }

    [Fact]
    public void Scale150_SizesAndMarginsAreScaled()
    {
        var r = FlyoutPlacement.BottomRight(new PixelRect(0, 0, 2560, 1380), scale: 1.5, contentHeightDip: 200);

        Assert.Equal(new PixelRect(2560 - 540 - 18, 1380 - 300 - 18, 540, 300), r);
    }

    [Fact]
    public void TaskbarOnLeft_WorkAreaOffset_IsRespected()
    {
        var r = FlyoutPlacement.BottomRight(new PixelRect(48, 0, 1872, 1080), scale: 1.0, contentHeightDip: 200);

        Assert.Equal(48 + 1872 - 360 - 12, r.X);
        Assert.Equal(1080 - 200 - 12, r.Y);
    }

    [Fact]
    public void TaskbarOnTop_StaysInsideWorkArea()
    {
        var r = FlyoutPlacement.BottomRight(new PixelRect(0, 48, 1920, 1032), scale: 1.0, contentHeightDip: 200);

        Assert.Equal(48 + 1032 - 200 - 12, r.Y);
    }

    [Fact]
    public void ContentTallerThanWorkArea_IsCappedAndStaysOnScreen()
    {
        var r = FlyoutPlacement.BottomRight(new PixelRect(0, 0, 1920, 1032), scale: 2.0, contentHeightDip: 5000);

        Assert.Equal(1032 - 2 * 24, r.Height);
        Assert.Equal(24, r.Y);
    }
}

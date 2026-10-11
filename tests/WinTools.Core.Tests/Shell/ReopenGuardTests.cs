using Microsoft.Extensions.Time.Testing;
using WinTools.Core.Shell;

namespace WinTools.Core.Tests.Shell;

public class ReopenGuardTests
{
    [Fact]
    public void NeverAutoHidden_DoesNotSuppress()
    {
        var guard = new ReopenGuard(new FakeTimeProvider());

        Assert.False(guard.ShouldSuppressShow());
    }

    [Fact]
    public void RightAfterAutoHide_SuppressesShow()
    {
        var time = new FakeTimeProvider();
        var guard = new ReopenGuard(time);

        guard.NoteAutoHidden();
        time.Advance(TimeSpan.FromMilliseconds(50));

        Assert.True(guard.ShouldSuppressShow());
    }

    [Fact]
    public void AfterWindowElapses_AllowsShow()
    {
        var time = new FakeTimeProvider();
        var guard = new ReopenGuard(time);

        guard.NoteAutoHidden();
        time.Advance(ReopenGuard.Window + TimeSpan.FromMilliseconds(1));

        Assert.False(guard.ShouldSuppressShow());
    }
}

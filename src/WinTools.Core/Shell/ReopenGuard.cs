namespace WinTools.Core.Shell;

/// <summary>
/// Clicking the tray icon while the flyout is open first deactivates (and auto-hides) the
/// flyout, then delivers the click, which would immediately reopen it. The guard swallows a
/// show request that arrives right after an auto-hide.
/// </summary>
public sealed class ReopenGuard(TimeProvider time)
{
    public static readonly TimeSpan Window = TimeSpan.FromMilliseconds(300);

    private long? _autoHiddenAt;

    public void NoteAutoHidden() => _autoHiddenAt = time.GetTimestamp();

    public bool ShouldSuppressShow() =>
        _autoHiddenAt is { } hiddenAt && time.GetElapsedTime(hiddenAt) <= Window;
}

namespace WinFEBuilder.App;

/// <summary>
/// Implemented by cached pages that need to refresh their contents each time they are shown
/// (MainForm keeps one instance per page, so constructor-time loading can go stale).
/// </summary>
public interface INavigationAware
{
    /// <summary>Called by MainForm every time the page is navigated to.</summary>
    void OnNavigatedTo();
}

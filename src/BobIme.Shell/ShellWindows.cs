namespace BobIme.Shell;

/// <summary>
/// 任务栏、桌面这类 shell 窗口的类名。点托盘会让它们成为前台窗口，
/// 不能当成「用户正在用的程序」：否则名单的还原逻辑会在人打开菜单那一刻就把档位还回去。
/// </summary>
public static class ShellWindows
{
    private static readonly string[] Classes =
    [
        // 主任务栏和副屏任务栏。
        "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "SystemTray_Main",
        // 托盘溢出气泡，Win11 换成 XAML 岛之后是另外两个类名。
        "NotifyIconOverflowWindow", "TopLevelWindowForOverflowXamlIsland", "XamlExplorerHostIslandWindow",
        // 开始菜单和搜索面板。UWP 内容窗口挂在 ApplicationFrameWindow 下面，
        // 顶层直接是这两个类名的只有 shell 自己的那几个，所以不会误伤名单里的应用。
        "Windows.UI.Core.CoreWindow", "ApplicationWindow",
        // 桌面本体。
        "Progman", "WorkerW",
    ];

    /// <summary>类名在不在上面这份里；大小写不敏感，读不到类名就不算。</summary>
    public static bool IsShell(string? className) =>
        !string.IsNullOrEmpty(className)
        && Classes.Any(c => string.Equals(c, className, StringComparison.OrdinalIgnoreCase));
}

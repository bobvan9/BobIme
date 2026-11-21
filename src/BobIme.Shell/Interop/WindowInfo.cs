using System.Text;

namespace BobIme.Shell.Interop;

/// <summary>前台窗口是谁：程序名和它的完整路径。换窗口时决定用哪档输入要看它。</summary>
internal static class WindowInfo
{
    /// <summary>窗口的可执行文件名，例如 <c>Wow.exe</c>；读不到就是 <c>null</c>。</summary>
    public static string? ExeName(nint hwnd) => ProcessPath(hwnd) is { } path ? Path.GetFileName(path) : null;

    public static string? ProcessPath(nint hwnd)
    {
        if (hwnd == 0 || Native.GetWindowThreadProcessId(hwnd, out var pid) == 0)
        {
            return null;
        }

        var handle = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle == 0)
        {
            // 权限不够（对方以管理员运行）或者进程已经退了。这种窗口本来也收不到我们的按键。
            return null;
        }

        try
        {
            var buffer = new StringBuilder(1024);
            var size = (uint)buffer.Capacity;
            return Native.QueryFullProcessImageNameW(handle, 0, buffer, ref size)
                ? buffer.ToString(0, (int)size)
                : null;
        }
        finally
        {
            Native.CloseHandle(handle);
        }
    }

    /// <summary>焦点事件是不是我们自己这几个窗口发出来的。</summary>
    public static bool IsOwnProcess(nint hwnd) =>
        hwnd != 0 && Native.GetWindowThreadProcessId(hwnd, out var pid) != 0 && pid == (uint)Environment.ProcessId;

    /// <summary>前台窗口是不是任务栏、桌面这类 shell 窗口。</summary>
    public static bool IsShellWindow(nint hwnd) => ShellWindows.IsShell(ClassName(hwnd));

    /// <summary>窗口类名，例如 <c>Shell_TrayWnd</c>；读不到就是 <c>null</c>。</summary>
    public static string? ClassName(nint hwnd)
    {
        if (hwnd == 0)
        {
            return null;
        }

        var buffer = new StringBuilder(256);
        var length = Native.GetClassNameW(hwnd, buffer, buffer.Capacity);
        return length == 0 ? null : buffer.ToString(0, length);
    }
}

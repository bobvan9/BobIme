namespace BobIme.Shell.Interop;

/// <summary>
/// 听「前台窗口换人」这一件事。用系统事件而不是轮询：切窗口不多，但每次都得当场办完，
/// 晚了的话用户在游戏里敲下的第一个字母已经进了候选窗。
/// </summary>
internal sealed class FocusWatcher : IDisposable
{
    private readonly Native.WinEventProc _proc;
    private nint _handle;

    public FocusWatcher() => _proc = OnEvent;

    /// <summary>新的前台窗口句柄。回调这一路要快，别在这里读进程名以外的东西。</summary>
    public Action<nint>? OnForeground { get; init; }

    /// <summary>装不上不影响输入法本身，只是「按程序定档」的规则表不生效，所以返回而不抛。</summary>
    public bool Install()
    {
        _handle = Native.SetWinEventHook(
            Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND,
            0, _proc, 0, 0, Native.WINEVENT_OUTOFCONTEXT);
        return _handle != 0;
    }

    public void Dispose()
    {
        if (_handle == 0)
        {
            return;
        }

        Native.UnhookWinEvent(_handle);
        _handle = 0;
    }

    private void OnEvent(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        // 异常冲出回调会把发送事件的进程带倒，这条一路什么都不该做。
        try
        {
            OnForeground?.Invoke(hwnd);
        }
        catch
        {
        }
    }
}

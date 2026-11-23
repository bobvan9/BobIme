namespace BobIme.Shell.Interop;

/// <summary>
/// 接住「请这个进程退出」的三条来路：安装程序发的 WM_CLOSE、注销关机前的 WM_QUERYENDSESSION、
/// 以及随后真正要走的 WM_ENDSESSION。
/// 托盘常驻程序一个可见窗口都没有，这些消息原本没人答：安装程序问不出结果就只能干等，
/// 看着像是卡死；注销时也一样，最后被系统硬切。
/// 窗口得真造出来（不是 message-only）：重启管理器和会话广播只认桌面上的顶层窗口。
/// </summary>
internal sealed class QuitWatcher : NativeWindow, IDisposable
{
    private readonly Action _onQuit;
    private bool _quitting;

    public QuitWatcher(Action onQuit)
    {
        _onQuit = onQuit;
        // 要带 WS_VISIBLE：taskkill 和重启管理器只挨个问看得见的顶层窗口，藏起来就没人来问，
        // 只能等安装程序最后强制收掉——那样托盘图标会留在屏上不掉。
        // 尺寸 0、挪到屏幕坐标之外，加上 WS_EX_TOOLWINDOW 不进 Alt+Tab，看得见等于不上屏。
        CreateHandle(new CreateParams
        {
            Caption = "Bob 五笔",
            Style = Native.WS_VISIBLE,
            ExStyle = Native.WS_EX_TOOLWINDOW,
            X = -32000,
            Y = -32000,
            Width = 0,
            Height = 0,
        });
    }

    public void Dispose()
    {
        if (Handle != 0)
        {
            DestroyHandle();
        }
    }

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            // 不转给默认处理：那会当场拆掉这扇窗口，而钩子回调还在往消息循环里投递东西。
            case Native.WM_CLOSE:
                Quit();
                return;
            case Native.WM_QUERYENDSESSION:
                // 这里只答「我没存着没落盘的东西，可以走」。真退要等 WM_ENDSESSION，
                // 现在就退的话，别人家的关机可能被我们这一票带崩。
                m.Result = 1;
                return;
            case Native.WM_ENDSESSION:
                if (m.WParam != 0)
                {
                    Quit();
                }

                return;
        }

        base.WndProc(ref m);
    }

    // 一次退出会被问好几遍（WM_CLOSE 之后还有 WM_ENDSESSION），第二遍没什么可做的。
    private void Quit()
    {
        if (_quitting)
        {
            return;
        }

        _quitting = true;
        _onQuit();
    }
}

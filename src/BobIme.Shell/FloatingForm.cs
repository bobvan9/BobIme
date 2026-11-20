using BobIme.Shell.Interop;

namespace BobIme.Shell;

/// <summary>
/// 不抢焦点的浮层基类。候选窗和符号面板都靠它：点了也不把前台从他正在打的程序抢走，
/// 否则后面的 SendInput 会落进自己窗口里。
/// </summary>
internal abstract class FloatingForm : Form
{
    private const int WmMouseActivate = 0x21;
    private const int MaNoActivate = 3;

    protected FloatingForm()
    {
        // 关掉自动缩放，位置和尺寸都能直接用物理像素算。
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW | Native.WS_EX_TOPMOST;
            return cp;
        }
    }

    protected override void WndProc(ref Message m)
    {
        // WS_EX_NOACTIVATE 管不到客户区点击那一路，WM_MOUSEACTIVATE 得自己回 MA_NOACTIVATE。
        if (m.Msg == WmMouseActivate)
        {
            m.Result = MaNoActivate;
            return;
        }

        base.WndProc(ref m);
    }
}

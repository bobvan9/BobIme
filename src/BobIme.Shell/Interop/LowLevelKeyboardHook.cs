using System.Runtime.InteropServices;

namespace BobIme.Shell.Interop;

public readonly record struct KeyEvent(
    uint Vk, uint Scan, bool Ctrl, bool Alt, bool Shift, bool CapsLock);

/// <summary>
/// 全局低级键盘钩子。回调必须极快返回，所以只做按键翻译，不做上屏。
/// </summary>
internal sealed class LowLevelKeyboardHook : IDisposable
{
    private readonly Native.HookProc _proc;
    private readonly HashSet<uint> _downModifiers = [];
    private readonly HashSet<uint> _swallowed = [];
    private nint _handle;
    private bool _reentrant;
    private uint _tapKey;
    private bool _tapArmed;

    public LowLevelKeyboardHook() => _proc = Proc;

    /// <summary>返回 true 表示该键被吞下，不再传给目标程序。</summary>
    public required Func<KeyEvent, bool> OnKeyDown { get; init; }

    /// <summary>单敲判定要等抬起，所以单独给一条回调，用来切中/英。</summary>
    public Action? OnTap { get; init; }

    /// <summary>要监听单敲的修饰键 VK；置 0 就不监听。设置界面里改。</summary>
    public uint TapKey
    {
        get => _tapKey;
        set
        {
            _tapKey = value;
            _tapArmed = false;
        }
    }

    /// <summary>
    /// 按键录入框那个窗口的句柄。前台窗口是它时一律放行，
    /// 否则录入框要接的组合键会先被钩子吞掉；置 0 就没有这个闸门。
    /// </summary>
    public nint CaptureOwner { get; set; }

    public void Install()
    {
        _handle = Native.SetWindowsHookExW(
            Native.WH_KEYBOARD_LL, _proc, Native.GetModuleHandleW(null), 0);
        if (_handle == 0)
        {
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "安装键盘钩子失败");
        }
    }

    public void Dispose()
    {
        if (_handle == 0)
        {
            return;
        }

        Native.UnhookWindowsHookEx(_handle);
        _handle = 0;
    }

    private nint Proc(int nCode, nint wParam, nint lParam)
    {
        if (nCode < 0)
        {
            return Native.CallNextHookEx(_handle, nCode, wParam, lParam);
        }

        var raw = Marshal.PtrToStructure<Native.KBDLLHOOKSTRUCT>(lParam);
        var message = (int)wParam;
        var isDown = message is Native.WM_KEYDOWN or Native.WM_SYSKEYDOWN;
        var isUp = message is Native.WM_KEYUP or Native.WM_SYSKEYUP;

        // 注入事件照常送给目标程序，否则我们自己上屏的字符会被这道闸门吃掉。
        if ((raw.flags & Native.LLKHF_INJECTED) == 0 && !Capturing())
        {
            TrackTap(raw.vkCode, isDown, isUp);

            if (isDown && IsModifier(raw.vkCode))
            {
                _downModifiers.Add(raw.vkCode);
            }
            else if (isUp)
            {
                _downModifiers.Remove(raw.vkCode);

                // 吞过 keydown 的键要把 keyup 一起吞掉，免得目标程序收到孤立的抬起。
                if (_swallowed.Remove(raw.vkCode))
                {
                    return 1;
                }
            }

            if (isDown && !_reentrant)
            {
                _reentrant = true;
                bool swallow;
                try
                {
                    swallow = OnKeyDown(new KeyEvent(
                        raw.vkCode, raw.scanCode, CtrlDown, (raw.flags & Native.LLKHF_ALTDOWN) != 0,
                        ShiftDown, CapsLockOn));
                }
                finally
                {
                    _reentrant = false;
                }

                if (swallow)
                {
                    _swallowed.Add(raw.vkCode);
                    return 1;
                }
            }
        }

        return Native.CallNextHookEx(_handle, nCode, wParam, lParam);
    }

    // 判定「单敲」要等抬起：按住那个修饰键期间碰过任何一个别的键，就不算单敲。
    // 这段跑在钩子回调里，只能记账和投递，不许干活。
    private void TrackTap(uint vk, bool isDown, bool isUp)
    {
        if (_tapKey == 0)
        {
            return;
        }

        if (isDown)
        {
            _tapArmed = vk == _tapKey;
            return;
        }

        if (!isUp || vk != _tapKey || !_tapArmed)
        {
            return;
        }

        _tapArmed = false;
        OnTap?.Invoke();
    }

    /// <summary>只看前台窗口：Alt+Tab 切到别的程序之后，钩子照常工作。</summary>
    private bool Capturing() => CaptureOwner != 0 && Native.GetForegroundWindow() == CaptureOwner;

    private static readonly uint[] CtrlVks = [Native.VK_CONTROL, 0xA2, 0xA3];
    private static readonly uint[] ShiftVks = [Native.VK_SHIFT, 0xA0, 0xA1];

    private bool CtrlDown => CtrlVks.Any(_downModifiers.Contains);

    private bool ShiftDown => ShiftVks.Any(_downModifiers.Contains);

    // 最低位是 Caps Lock 的切换状态，现读现用。
    private static bool CapsLockOn => (Native.GetKeyState((int)Native.VK_CAPITAL) & 1) != 0;

    private static bool IsModifier(uint vk) =>
        vk is Native.VK_SHIFT or Native.VK_CONTROL or Native.VK_MENU or Native.VK_LWIN or Native.VK_RWIN
            or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5;
}

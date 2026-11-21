using System.Runtime.InteropServices;
using System.Text;

namespace BobIme.Shell.Interop;

internal static class Native
{
    public const int WH_KEYBOARD_LL = 13;
    public const int WM_KEYDOWN = 0x0100;
    public const int WM_KEYUP = 0x0101;
    public const int WM_SYSKEYDOWN = 0x0104;
    public const int WM_SYSKEYUP = 0x0105;

    public const uint LLKHF_INJECTED = 0x10;
    public const uint LLKHF_ALTDOWN = 0x20;
    public const uint LLKHF_UP = 0x80;

    public const uint VK_SHIFT = 0x10;
    public const uint VK_LSHIFT = 0xA0;
    public const uint VK_CONTROL = 0x11;
    public const uint VK_MENU = 0x12;
    public const uint VK_CAPITAL = 0x14;
    public const uint VK_LWIN = 0x5B;
    public const uint VK_RWIN = 0x5C;

    public const int SPI_GETWORKAREA = 0x0030;

    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOACTIVATE = 0x0010;
    public static readonly nint HWND_TOPMOST = -1;

    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_TOPMOST = 0x00000008;
    public const int WS_EX_NOACTIVATE = 0x08000000;

    public const int GWL_EXSTYLE = -20;
    public const uint GA_ROOT = 2;

    // 换窗口这件事用系统事件知道，不去轮询。
    public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;

    /// <summary>回调在本进程的输入队列里跑，不影响发事件的程序；自家窗口的事件也照收，过滤要自己做。</summary>
    public const uint WINEVENT_OUTOFCONTEXT = 0x0000;

    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    public const uint KEYEVENTF_UNICODE = 0x0004;
    public const uint KEYEVENTF_KEYUP = 0x0002;

    public const int ATTACH_PARENT_PROCESS = -1;

    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    // 联合体尺寸必须取最大成员 MOUSEINPUT，否则 Marshal.SizeOf<INPUT> 不等于原生 INPUT，SendInput 会直接失败。
    [StructLayout(LayoutKind.Explicit)]
    public struct INPUTUNION
    {
        [FieldOffset(0)]
        public KEYBDINPUT ki;

        [FieldOffset(0)]
        public MOUSEINPUT mi;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT
    {
        public uint type;
        public INPUTUNION U;

        public static readonly int Size = Marshal.SizeOf<INPUT>();
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GUITHREADINFO
    {
        public int cbSize;
        public int flags;
        public nint hwndActive;
        public nint hwndFocus;
        public nint hwndCapture;
        public nint hwndMenuOwner;
        public nint hwndMoveSize;
        public nint hwndCaret;
        public RECT rcCaret;
    }

    public delegate nint HookProc(int nCode, nint wParam, nint lParam);

    public delegate void WinEventProc(
        nint hWinEventHook, uint eventType, nint hwnd, int idObject, int idChild,
        uint dwEventThread, uint dwmsEventTime);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern nint SetWindowsHookExW(int idHook, HookProc lpfn, nint hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWindowsHookEx(nint hhk);

    [DllImport("user32.dll")]
    public static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    // 只取低位当 Caps Lock 的开关状态；每次按键现读，省掉自己维护一份会漂移的影子状态。
    [DllImport("user32.dll")]
    public static extern short GetKeyState(int nVirtKey);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern nint GetModuleHandleW(string? lpModuleName);

    // WinExe 默认不带控制台，自检要靠这个把输出接回调用它的终端。
    [DllImport("kernel32.dll")]
    public static extern bool AttachConsole(int dwProcessId);

    [DllImport("user32.dll")]
    public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    public static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(nint hWnd, nint lpdwProcessId);

    /// <summary>要拿进程号的调用方走这个重载，省得自己造指针。</summary>
    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    public static extern nint GetShellWindow();

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    public static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);

    [DllImport("user32.dll")]
    public static extern bool ClientToScreen(nint hWnd, ref POINT lpPoint);

    // 返回该点上 z 序最靠前的可见窗口，用它反推候选窗是不是被压住了。
    [DllImport("user32.dll")]
    public static extern nint WindowFromPoint(POINT Point);

    [DllImport("user32.dll")]
    public static extern nint GetAncestor(nint hwnd, uint gaFlags);

    /// <summary>窗口类名，用来认出任务栏这类 shell 窗口。</summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassNameW(nint hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern nint GetWindowLongPtrW(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    public static extern bool SystemParametersInfoW(int uiAction, uint uiParam, out RECT pvParam, uint fWinIni);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    /// <summary>装系统事件钩子；范围取一对相同值就只订阅一种事件。</summary>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern nint SetWinEventHook(
        uint eventMin, uint eventMax, nint hmodWinEventProc, WinEventProc lpfnWinEventProc,
        uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    public static extern bool UnhookWinEvent(nint hWinEventHook);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern nint OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool QueryFullProcessImageNameW(
        nint hProcess, uint dwFlags, StringBuilder lpExeName, ref uint lpdwSize);

    [DllImport("kernel32.dll")]
    public static extern bool CloseHandle(nint hObject);
}

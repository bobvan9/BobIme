using System.Drawing;

namespace BobIme.Shell.Interop;

internal static class CaretLocator
{
    private const int OffsetY = 4;
    private const int Margin = 8;

    /// <summary>贴着光标摆：优先系统光标，取不到退到鼠标，最后落到工作区左下。</summary>
    public static Point Locate(Size windowSize, nint self)
    {
        var work = WorkArea();
        return Place(windowSize, self, CaretAnchor(work), work);
    }

    /// <summary>按设置里的候选窗位置摆。</summary>
    public static Point Locate(Size windowSize, nint self, CandidatePlacement placement)
    {
        var work = WorkArea();
        return Place(windowSize, self, AnchorFor(placement, windowSize, work), work);
    }

    private static Point Place(Size windowSize, nint self, Point anchor, Rectangle work)
    {
        var position = Clamp(anchor, windowSize, work);

        for (var attempt = 0; attempt < 4; attempt++)
        {
            if (!TryCover(position, self, out var cover))
            {
                break;
            }

            var moved = Slide(position, windowSize, cover, work);
            if (moved == position)
            {
                break;
            }

            position = Clamp(moved, windowSize, work);
        }

        return position;
    }

    /// <summary>摆在任务栏之外的工作区正中间，给符号面板这种不跟光标的浮窗用。</summary>
    public static Point Center(Size windowSize)
    {
        var work = WorkArea();
        return Clamp(
            new Point(work.Left + (work.Width - windowSize.Width) / 2, work.Top + (work.Height - windowSize.Height) / 2),
            windowSize,
            work);
    }

    private static Point AnchorFor(CandidatePlacement placement, Size size, Rectangle work) => placement switch
    {
        CandidatePlacement.Mouse => MouseAnchor(work),
        // 钉在右下角：右下角是窗子自己的右上对齐点，减去宽高才落到工作区里。
        CandidatePlacement.BottomRight => new Point(
            work.Right - size.Width - Margin, work.Bottom - size.Height - Margin),
        _ => CaretAnchor(work),
    };

    private static Point CaretAnchor(Rectangle work)
    {
        if (TryCaret(out var caret))
        {
            return new Point(caret.X, caret.Y + OffsetY);
        }

        // 浏览器、Electron 这些不建系统光标，问 UIA 要；再取不到才落到鼠标。
        return UiaCaret.TryCaret(out var focused) ? new Point(focused.X, focused.Y + OffsetY) : MouseAnchor(work);
    }

    private static Point MouseAnchor(Rectangle work) =>
        Native.GetCursorPos(out var cursor)
            ? new Point(cursor.X, cursor.Y + 24)
            : new Point(work.Left + 40, work.Bottom - 160);

    /// <summary>
    /// 开始菜单那种窗口自己也置顶，我们压不过它，只能看出该点上压着谁。
    /// 普通程序的窗口不会挡我们：我们置顶、它不置顶。
    /// </summary>
    private static bool TryCover(Point position, nint self, out Rectangle cover)
    {
        cover = Rectangle.Empty;
        var hit = Native.WindowFromPoint(new Native.POINT { X = position.X + 2, Y = position.Y + 2 });
        var root = hit == 0 ? 0 : Native.GetAncestor(hit, Native.GA_ROOT);
        if (root == 0 || root == self)
        {
            return false;
        }

        if ((Native.GetWindowLongPtrW(root, Native.GWL_EXSTYLE).ToInt64() & Native.WS_EX_TOPMOST) == 0)
        {
            return false;
        }

        if (!Native.GetWindowRect(root, out var rect))
        {
            return false;
        }

        cover = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
        return true;
    }

    /// <summary>退到挡住我们的窗口边上：左、右、下、上，取第一个放得下的。</summary>
    private static Point Slide(Point position, Size size, Rectangle cover, Rectangle work)
    {
        var left = new Point(cover.Left - size.Width - Margin, position.Y);
        if (left.X >= work.Left)
        {
            return left;
        }

        var right = new Point(cover.Right + Margin, position.Y);
        if (right.X + size.Width <= work.Right)
        {
            return right;
        }

        var below = new Point(position.X, cover.Bottom + Margin);
        if (below.Y + size.Height <= work.Bottom)
        {
            return below;
        }

        var above = new Point(position.X, cover.Top - size.Height - Margin);
        return above.Y >= work.Top ? above : position;
    }

    /// <summary>
    /// 翻页不重新定位，位置原样留着；但窗口宽度会随每页候选长短变，
    /// 所以还得夹一次，别把右边露到屏幕外。
    /// </summary>
    public static Point Keep(Point position, Size windowSize) => Clamp(position, windowSize, WorkArea());

    private static Rectangle WorkArea() =>
        Native.SystemParametersInfoW(Native.SPI_GETWORKAREA, 0, out var work, 0)
            ? Rectangle.FromLTRB(work.Left, work.Top, work.Right, work.Bottom)
            : Rectangle.Empty;

    private static Point Clamp(Point position, Size size, Rectangle work) => new(
        Math.Clamp(position.X, work.Left, Math.Max(work.Left, work.Right - size.Width)),
        Math.Clamp(position.Y, work.Top, Math.Max(work.Top, work.Bottom - size.Height)));

    private static bool TryCaret(out Point position)
    {
        position = default;
        var foreground = Native.GetForegroundWindow();
        if (foreground == 0 || foreground == Native.GetShellWindow())
        {
            return false;
        }

        var thread = Native.GetWindowThreadProcessId(foreground, 0);
        if (TryThreadCaret(thread, out position))
        {
            return true;
        }

        // 编辑控件挂在另一个线程时，系统光标记在那个线程，跟着焦点窗口再查一次。
        var focus = new Native.GUITHREADINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.GUITHREADINFO>() };
        if (!Native.GetGUIThreadInfo(thread, ref focus) || focus.hwndFocus == 0)
        {
            return false;
        }

        var focusThread = Native.GetWindowThreadProcessId(focus.hwndFocus, 0);
        return focusThread != thread && TryThreadCaret(focusThread, out position);
    }

    private static bool TryThreadCaret(uint thread, out Point position)
    {
        position = default;
        var info = new Native.GUITHREADINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.GUITHREADINFO>() };
        if (!Native.GetGUIThreadInfo(thread, ref info) || info.hwndCaret == 0)
        {
            return false;
        }

        var point = new Native.POINT { X = info.rcCaret.Left, Y = info.rcCaret.Bottom };
        if (!Native.ClientToScreen(info.hwndCaret, ref point))
        {
            return false;
        }

        position = new Point(point.X, point.Y);
        return true;
    }
}

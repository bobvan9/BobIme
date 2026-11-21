using System.Drawing;

namespace BobIme.Shell.Interop;

/// <summary>
/// 用 UIA 问输入光标在哪。浏览器、Electron 这类程序不建系统光标，
/// <c>GetGUIThreadInfo</c> 那条路取到的是空矩形，于是候选窗只能退到鼠标位置——
/// 它们的编辑区倒是都实现了 TextPattern，选区折叠成光标时会给出一条 1 像素宽的矩形。
/// </summary>
internal static class UiaCaret
{
    /// <summary>焦点处光标的位置（矩形左下角，和系统光标同一档坐标）。</summary>
    public static bool TryCaret(out Point position)
    {
        position = default;
        if (CaretRects() is not { Length: >= 4 } rects)
        {
            return false;
        }

        // 选区跨行时会有好几块，最后一块的左下角才是接着打字的地方。
        var last = rects.Length / 4 - 1;
        var candidate = new Point(
            (int)Math.Round(rects[last * 4]),
            (int)Math.Round(rects[last * 4 + 1] + rects[last * 4 + 3]));
        if (!InsideForeground(candidate))
        {
            return false;
        }

        position = candidate;
        return true;
    }

    /// <summary>
    /// 提供方报的坐标要落在前台窗口里才算数：万一它给的是另一个 DPI 刻度或者压根是脏值，
    /// 宁可退回鼠标位，也别把候选窗甩到屏幕外。
    /// </summary>
    private static bool InsideForeground(Point position)
    {
        var foreground = Native.GetForegroundWindow();
        if (foreground == 0 || !Native.GetWindowRect(foreground, out var rect))
        {
            return false;
        }

        const int slack = 8;
        return position.X >= rect.Left - slack && position.X <= rect.Right + slack
            && position.Y >= rect.Top - slack && position.Y <= rect.Bottom + slack;
    }

    private static double[]? CaretRects()
    {
        if (Uia.FocusedElement() is not { } element
            || Uia.TextPatternOf(element) is not { } pattern)
        {
            return null;
        }

        try
        {
            pattern.GetSelection(out var ranges);
            ranges.get_Length(out var length);
            if (length == 0)
            {
                return null;
            }

            ranges.GetElement(length - 1, out var range);
            range.GetBoundingRectangles(out var rects);
            return rects;
        }
        catch
        {
            // 目标程序正在忙、或者已经退了；调用方自己退回鼠标位。
            return null;
        }
    }
}

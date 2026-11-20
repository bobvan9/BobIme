using System.Text;
using BobIme.Core;
using BobIme.Shell.Interop;

namespace BobIme.Shell;

/// <summary>无边框候选窗：不激活、不进任务栏，绘制全部自己来。</summary>
internal sealed class CandidateForm : Form
{
    private const int Inset = 7;
    private const int Gap = 10;

    /// <summary>编码和页码之间留的空，比候选之间的间隔窄。</summary>
    private const int PageGap = 12;

    /// <summary>箭头热区一档的宽度：箭头是拿来点的，不能照字形那么小。</summary>
    private const int ArrowCell = 26;

    /// <summary>数字坐在编码行框的上半截，三角跟着它们走就得从行高里让出这几像素。</summary>
    private const int ArrowLift = 4;

    private const string PrevGlyph = "◀";
    private const string NextGlyph = "▶";
    private static readonly Size MaxMeasure = new(4000, 4000);

    // 浅色面板：候选窗要压在白底文档上，深底白字那种硬对比在浅色页面上很扎眼。
    private static readonly Color PanelColor = Color.FromArgb(0xF9, 0xF9, 0xF9);
    private static readonly Color CandidateColor = Color.FromArgb(0x1F, 0x1F, 0x1F);
    private static readonly Color CodeColor = Color.FromArgb(0x0F, 0x6C, 0xBD);
    private static readonly Color BorderColor = Color.FromArgb(0xC8, 0xCC, 0xD0);
    private static readonly Color PinyinColor = Color.FromArgb(0x6E, 0x76, 0x81);
    private static readonly Color DisabledColor = Color.FromArgb(0xB9, 0xBE, 0xC4);
    private static readonly Color HoverColor = Color.FromArgb(0xE4, 0xE7, 0xEA);
    private const string Separator = " · ";

    private readonly Font _codeFont = new("Microsoft YaHei UI", 10f);
    private readonly Font _textFont = new("Microsoft YaHei UI", 13f);
    private readonly Font _arrowFont = new("Microsoft YaHei UI", 11f);
    private string _buffer = "";
    private string _wubiRun = "";
    private string _pinyinRun = "";
    private string _pageRun = "";
    private int _pinyinRunLeft;
    private int _pageRunLeft;
    private Rectangle _prevHot = Rectangle.Empty;
    private Rectangle _nextHot = Rectangle.Empty;
    private bool _canPrev;
    private bool _canNext;
    private int _hover;
    private int _arrowBoxHeight;

    /// <summary>鼠标点了翻页箭头，参数是往后还是往前。</summary>
    public event Action<bool>? PageRequested;

    public CandidateForm()
    {
        // 关掉自动缩放，位置和尺寸就能直接用 CaretLocator 拿到的物理像素。
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        BackColor = PanelColor;
        ForeColor = CandidateColor;
        DoubleBuffered = true;
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

    public void ShowComposition(string buffer, IReadOnlyList<Candidate> candidates, int page, int pages)
    {
        // 只有起新编码或窗口还没露出来才重新定位；翻页不改编码，窗体就该钉在原地。
        var relocate = !Visible || buffer != _buffer;
        _buffer = buffer;
        Format(candidates);
        // 只有一页就不标页码也不画箭头，白占一块地方。
        _pageRun = pages > 1 ? $"{page}/{pages}" : "";
        _canPrev = page > 1;
        _canNext = page < pages;
        // 翻页后鼠标多半还停在箭头上面，高亮得留着，别每点一下闪一下。
        if (relocate)
        {
            _hover = 0;
        }
        var arrowRoom = pages > 1 ? PageGap + ArrowCell * 2 : 0;

        var code = TextRenderer.MeasureText(buffer, _codeFont, MaxMeasure, TextFormatFlags.NoPadding);
        var pageText = TextRenderer.MeasureText(_pageRun, _codeFont, MaxMeasure, TextFormatFlags.NoPadding);
        var pageRoom = pageText.Width > 0 ? PageGap + pageText.Width : 0;
        var wubi = TextRenderer.MeasureText(_wubiRun, _textFont, MaxMeasure, TextFormatFlags.NoPadding);
        var pinyin = TextRenderer.MeasureText(_pinyinRun, _textFont, MaxMeasure, TextFormatFlags.NoPadding);
        _pinyinRunLeft = Inset + wubi.Width;
        _pageRunLeft = Inset + code.Width + (pageRoom > 0 ? PageGap : 0);
        ClientSize = new Size(
            Math.Max(code.Width + pageRoom + arrowRoom, wubi.Width + pinyin.Width) + Inset * 2,
            code.Height + Math.Max(wubi.Height, pinyin.Height) + Gap + Inset * 2);

        // 箭头紧跟页码排在左边这一栏，位置只跟编码宽度走。钉在窗口右缘的话，
        // 每页候选长短不一样，箭头会跟着左右跑，鼠标还得去追它。
        // 热区往下吃到行间的空，那一块本来就没人用，点起来厚一大截。
        var arrowLeft = _pageRunLeft + pageText.Width + PageGap;
        var arrowHeight = code.Height + Gap;
        _arrowBoxHeight = code.Height - ArrowLift;
        _prevHot = arrowRoom > 0
            ? new Rectangle(arrowLeft, Inset, ArrowCell, arrowHeight) : Rectangle.Empty;
        _nextHot = arrowRoom > 0
            ? new Rectangle(arrowLeft + ArrowCell, Inset, ArrowCell, arrowHeight) : Rectangle.Empty;

        var position = relocate
            ? CaretLocator.Locate(ClientSize, Handle)
            : CaretLocator.Keep(Location, ClientSize);
        Native.SetWindowPos(
            Handle, Native.HWND_TOPMOST, position.X, position.Y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);

        Invalidate();
        if (!Visible)
        {
            Show();
        }
    }

    /// <summary>
    /// 混输时候选按来源切成两段：五笔段用主色，拼音段整段降成次要色，
    /// 中间点一个间隔点，不再写「拼音」两个字。
    /// </summary>
    private void Format(IReadOnlyList<Candidate> candidates)
    {
        var firstPinyin = 0;
        while (firstPinyin < candidates.Count && candidates[firstPinyin].Source == CandidateSource.Wubi)
        {
            firstPinyin++;
        }

        _wubiRun = Join(candidates, 0, firstPinyin);
        var pinyinRun = Join(candidates, firstPinyin, candidates.Count);
        _pinyinRun = _wubiRun.Length > 0 && pinyinRun.Length > 0 ? Separator + pinyinRun : pinyinRun;
    }

    private static string Join(IReadOnlyList<Candidate> candidates, int from, int to)
    {
        var builder = new StringBuilder();
        for (var i = from; i < to; i++)
        {
            if (i > from)
            {
                builder.Append("   ");
            }

            builder.Append($"{i + 1} {candidates[i].Text}");
        }

        return builder.ToString();
    }

    public void HideComposition()
    {
        if (Visible)
        {
            Hide();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.Clear(BackColor);
        TextRenderer.DrawText(
            e.Graphics, _buffer, _codeFont, new Point(Inset, Inset),
            CodeColor, TextFormatFlags.NoPadding);
        if (_pageRun.Length > 0)
        {
            TextRenderer.DrawText(
                e.Graphics, _pageRun, _codeFont, new Point(_pageRunLeft, Inset),
                PinyinColor, TextFormatFlags.NoPadding);
            DrawArrow(e.Graphics, _prevHot, PrevGlyph, _canPrev, _hover == 1 && _canPrev);
            DrawArrow(e.Graphics, _nextHot, NextGlyph, _canNext, _hover == 2 && _canNext);
        }
        var listTop = Inset + _codeFont.Height + Gap;
        TextRenderer.DrawText(
            e.Graphics, _wubiRun, _textFont, new Point(Inset, listTop),
            ForeColor, TextFormatFlags.NoPadding);
        TextRenderer.DrawText(
            e.Graphics, _pinyinRun, _textFont, new Point(_pinyinRunLeft, listTop),
            PinyinColor, TextFormatFlags.NoPadding);
        ControlPaint.DrawBorder(e.Graphics, ClientRectangle, BorderColor, ButtonBorderStyle.Solid);
    }

    private void DrawArrow(Graphics graphics, Rectangle cell, string glyph, bool enabled, bool hovered)
    {
        // 热区往下吃了行间的空，点起来厚；画出来的那一格只占编码行，和页码齐高。
        var box = new Rectangle(cell.X, cell.Y, cell.Width, _arrowBoxHeight);
        if (hovered)
        {
            using var brush = new SolidBrush(HoverColor);
            graphics.FillRectangle(brush, box);
        }

        TextRenderer.DrawText(
            graphics, glyph, _arrowFont, box, enabled ? PinyinColor : DisabledColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        if (_canNext && _nextHot.Contains(e.Location))
        {
            PageRequested?.Invoke(true);
        }
        else if (_canPrev && _prevHot.Contains(e.Location))
        {
            PageRequested?.Invoke(false);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var hover = _canNext && _nextHot.Contains(e.Location) ? 2
            : _canPrev && _prevHot.Contains(e.Location) ? 1 : 0;
        Cursor = hover == 0 ? Cursors.Default : Cursors.Hand;
        if (hover == _hover)
        {
            return;
        }

        _hover = hover;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hover == 0)
        {
            return;
        }

        _hover = 0;
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _codeFont.Dispose();
            _textFont.Dispose();
            _arrowFont.Dispose();
        }

        base.Dispose(disposing);
    }
}

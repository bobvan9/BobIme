using System.Text;
using BobIme.Core;
using BobIme.Shell.Interop;

namespace BobIme.Shell;

/// <summary>无边框候选窗：不激活、不进任务栏，绘制全部自己来。</summary>
internal sealed class CandidateForm : Form
{
    private const int Inset = 7;
    private const int Gap = 10;
    private static readonly Size MaxMeasure = new(4000, 4000);

    // 浅色面板：候选窗要压在白底文档上，深底白字那种硬对比在浅色页面上很扎眼。
    private static readonly Color PanelColor = Color.FromArgb(0xF9, 0xF9, 0xF9);
    private static readonly Color CandidateColor = Color.FromArgb(0x1F, 0x1F, 0x1F);
    private static readonly Color CodeColor = Color.FromArgb(0x0F, 0x6C, 0xBD);
    private static readonly Color BorderColor = Color.FromArgb(0xC8, 0xCC, 0xD0);
    private static readonly Color PinyinColor = Color.FromArgb(0x6E, 0x76, 0x81);
    private const string Separator = " · ";

    private readonly Font _codeFont = new("Microsoft YaHei UI", 10f);
    private readonly Font _textFont = new("Microsoft YaHei UI", 13f);
    private string _buffer = "";
    private string _wubiRun = "";
    private string _pinyinRun = "";
    private int _pinyinRunLeft;

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

    public void ShowComposition(string buffer, IReadOnlyList<Candidate> candidates)
    {
        _buffer = buffer;
        Format(candidates);

        var code = TextRenderer.MeasureText(buffer, _codeFont, MaxMeasure, TextFormatFlags.NoPadding);
        var wubi = TextRenderer.MeasureText(_wubiRun, _textFont, MaxMeasure, TextFormatFlags.NoPadding);
        var pinyin = TextRenderer.MeasureText(_pinyinRun, _textFont, MaxMeasure, TextFormatFlags.NoPadding);
        _pinyinRunLeft = Inset + wubi.Width;
        ClientSize = new Size(
            Math.Max(code.Width, wubi.Width + pinyin.Width) + Inset * 2,
            code.Height + Math.Max(wubi.Height, pinyin.Height) + Gap + Inset * 2);

        var position = CaretLocator.Locate(ClientSize, Handle);
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
        var listTop = Inset + _codeFont.Height + Gap;
        TextRenderer.DrawText(
            e.Graphics, _wubiRun, _textFont, new Point(Inset, listTop),
            ForeColor, TextFormatFlags.NoPadding);
        TextRenderer.DrawText(
            e.Graphics, _pinyinRun, _textFont, new Point(_pinyinRunLeft, listTop),
            PinyinColor, TextFormatFlags.NoPadding);
        ControlPaint.DrawBorder(e.Graphics, ClientRectangle, BorderColor, ButtonBorderStyle.Solid);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _codeFont.Dispose();
            _textFont.Dispose();
        }

        base.Dispose(disposing);
    }
}

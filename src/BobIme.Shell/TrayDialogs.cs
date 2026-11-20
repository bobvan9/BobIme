using System.Reflection;

namespace BobIme.Shell;

internal sealed record InfoRow(string Key, string Text);

internal sealed record InfoSection(string Title, InfoRow[] Rows);

/// <summary>托盘「说明」和「关于」的文本内容。</summary>
internal static class TrayDialogs
{
    public static void ShowUsage() => Show("Bob 五笔 使用说明", Usage(), new Size(640, 560));

    public static void ShowAbout() => Show("关于 Bob 五笔", About(), new Size(540, 260));

    private static void Show(string title, InfoSection[] sections, Size clientSize)
    {
        using var dialog = new TextDialog(title, sections, clientSize);
        dialog.ShowDialog();
    }

    private static InfoSection[] About()
    {
        var assembly = typeof(TrayDialogs).Assembly;
        var product = assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? "Bob 五笔";
        var author = assembly.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company ?? "Bob";
        var copyright = assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright;

        // 源码提交号由 SDK 在生成程序集信息时拼在 InformationalVersion 的 '+' 之后。
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
        var plus = informational.IndexOf('+');
        var version = plus < 0 ? informational : informational[..plus];
        var commit = plus < 0 ? "" : informational[(plus + 1)..];
        var versionText = commit.Length == 0
            ? version
            : $"{version}　源码 {commit[..Math.Min(7, commit.Length)]}";

        var rows = new List<InfoRow>
        {
            new("名称", product),
            new("版本", versionText),
            new("作者", author),
        };
        if (!string.IsNullOrEmpty(copyright))
        {
            rows.Add(new InfoRow("版权", copyright!));
        }

        rows.Add(new InfoRow("程序目录", AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)));

        return [new InfoSection("版本信息", [.. rows])];
    }

    private static InfoSection[] Usage() =>
    [
        new("概述",
        [
            new InfoRow("词库直查", "按编码直接检索词库，不做字根拆分。"),
            new InfoRow("后台常驻", "以全局键盘钩子实现，不注册为系统输入法，输入状态由托盘图标确认。"),
        ]),
        new("模式与开关",
        [
            new InfoRow("Ctrl+Space", "启用或停用输入法。停用状态下所有按键原样交给当前程序。"),
            new InfoRow("左 Shift 单敲", "切换中文与英文模式。按住 Shift 输入大写字母、Ctrl+Shift 切换键盘布局均不触发，右 Shift 无效。"),
            new InfoRow("托盘图标", "蓝底「五」为中文模式，琥珀底「A」为英文模式。停用时图标不变，仅在提示文字中标注「已停用」。"),
        ]),
        new("按键",
        [
            new InfoRow("a–z", "输入编码，当前编码显示在候选条上方。"),
            new InfoRow("空格 / 回车", "上屏第一候选。"),
            new InfoRow("1–9", "按序号上屏对应候选，主键盘与小键盘等效。"),
            new InfoRow("Backspace", "删除最后一位编码。"),
            new InfoRow("Esc", "清除当前编码。"),
            new InfoRow("大写字母", "不作为编码，直接交给当前程序，已输入的编码保持不变。"),
            new InfoRow("其他按键", "先将第一候选上屏，再原样放行该按键，中英文混排的先后顺序因此正确。"),
        ]),
        new("候选规则",
        [
            new InfoRow("分页", "每页 9 条，五笔候选在前、拼音候选在后，两段以间隔号分隔并以浅灰区分。"),
            new InfoRow("自动上屏", "三码以内唯一全码命中时自动上屏；四码需按空格确认。"),
            new InfoRow("名额分配", "两类候选先各占半页，未占满的名额顺延给另一类，避免前缀集中的词相互挤占。"),
            new InfoRow("长编码", "编码超过四位后五笔无候选，仅显示拼音结果。"),
        ]),
        new("词库",
        [
            new InfoRow("五笔", "极爽词库 4.3 修订版，采用 86 版一级简码布局，共 87521 条。"),
            new InfoRow("拼音", "rime-ice base / 8105 / 41448 合并表，共 395529 条，按词频排序。"),
            new InfoRow("连写", "拼音支持整词连写，例如输入 zhongguo 得到「中国」。"),
        ]),
        new("标点",
        [
            new InfoRow("输出", "统一输出英文标点。词库不加载中文标点、带声调字母及假名条目。"),
        ]),
        new("使用限制",
        [
            new InfoRow("权限程序", "以管理员权限运行的程序无法接收按键，这是 Windows UIPI 的限制。"),
            new InfoRow("独占全屏", "独占全屏程序无法显示候选窗，改用无边框窗口模式即可。"),
        ]),
    ];
}

/// <summary>只读富文本窗：小标题加粗，键名占据固定一栏，折行对齐到正文栏。Esc 或「关闭」退出。</summary>
internal sealed class TextDialog : Form
{
    // 跟候选窗同一套浅底深字，两个窗口挨着看不刺眼。
    private static readonly Color PanelColor = Color.FromArgb(0xF9, 0xF9, 0xF9);
    private static readonly Color InkColor = Color.FromArgb(0x1F, 0x1F, 0x1F);
    // 小标题用候选窗编码那一档蓝，两个窗口的配色对得上。
    private static readonly Color TitleColor = Color.FromArgb(0x0F, 0x6C, 0xBD);
    private const int KeyColumn = 118;

    private readonly Font _bodyFont = new("Microsoft YaHei UI", 9.75f);
    private readonly Font _boldFont = new("Microsoft YaHei UI", 9.75f, FontStyle.Bold);

    public TextDialog(string title, IReadOnlyList<InfoSection> sections, Size clientSize)
    {
        Text = title;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        MinimumSize = new Size(400, 220);
        ClientSize = clientSize;
        BackColor = PanelColor;
        ForeColor = InkColor;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = PanelColor,
            Padding = new Padding(9, 9, 9, 6),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var text = new RichTextBox
        {
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = PanelColor,
            ForeColor = InkColor,
            Font = _bodyFont,
            Dock = DockStyle.Fill,
            TabStop = false,
            DetectUrls = false,
            HideSelection = false,
        };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Margin = new Padding(0, 6, 0, 0),
        };
        var close = new Button { Text = "关闭", AutoSize = true, DialogResult = DialogResult.OK };
        buttons.Controls.Add(close);

        layout.Controls.Add(text, 0, 0);
        layout.Controls.Add(buttons, 0, 1);
        Controls.Add(layout);

        AcceptButton = close;
        CancelButton = close;

        // 富文本的选区格式要有句柄才生效，填充前先逼它建出来。
        _ = text.Handle;
        Fill(text, sections);
    }

    private void Fill(RichTextBox box, IReadOnlyList<InfoSection> sections)
    {
        foreach (var section in sections)
        {
            if (box.TextLength > 0)
            {
                box.SelectionFont = _bodyFont;
                box.AppendText(Environment.NewLine);
            }

            box.SelectionIndent = 0;
            box.SelectionHangingIndent = 0;
            box.SelectionColor = TitleColor;
            box.SelectionFont = _boldFont;
            box.AppendText(section.Title + Environment.NewLine);

            foreach (var row in section.Rows)
            {
                // 折行的第二行落在正文栏，不会去接键名。
                box.SelectionIndent = 0;
                box.SelectionHangingIndent = KeyColumn;
                box.SelectionTabs = [KeyColumn];
                box.SelectionColor = InkColor;
                box.SelectionFont = _boldFont;
                box.AppendText($"{row.Key}\t");
                box.SelectionFont = _bodyFont;
                box.AppendText($"{row.Text}{Environment.NewLine}");
            }
        }

        box.Select(0, 0);
        box.ScrollToCaret();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _bodyFont.Dispose();
            _boldFont.Dispose();
        }

        base.Dispose(disposing);
    }
}

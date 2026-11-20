using System.Reflection;

namespace BobIme.Shell;

/// <summary>托盘「说明」和「关于」两个菜单项背后的只读文本窗。</summary>
internal static class TrayDialogs
{
    public static void ShowUsage() =>
        Show("Bob 五笔 使用说明", Usage(), new Size(600, 520));

    public static void ShowAbout() =>
        Show("关于 Bob 五笔", About(), new Size(480, 260));

    private static void Show(string title, string body, Size clientSize)
    {
        using var dialog = new TextDialog(title, body, clientSize);
        dialog.ShowDialog();
    }

    private static string About()
    {
        var assembly = typeof(TrayDialogs).Assembly;
        var product = assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? "Bob 五笔";
        var author = assembly.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company ?? "Bob";
        var copyright = assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? "";

        // 源码提交号是发布时由 SDK 拼在 InformationalVersion 的 '+' 后面带进来的。
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
        var plus = informational.IndexOf('+');
        var version = plus < 0 ? informational : informational[..plus];
        var commit = plus < 0 ? "" : informational[(plus + 1)..];
        var versionLine = commit.Length == 0
            ? version
            : $"{version}（提交 {commit[..Math.Min(7, commit.Length)]}）";

        return Lines(
            $"名称\t{product}",
            $"版本\t{versionLine}",
            $"作者\t{author}",
            copyright.Length == 0 ? null : $"版权\t{copyright}",
            "码表\t极爽词库 4.3 修订版（五笔）+ rime-ice 合并表（拼音）",
            $"运行目录\t{AppContext.BaseDirectory}");
    }

    private static string Usage() => Lines(
        "本输入法走「码表直查」：打编码查码表出候选，不拆字根。它是常驻后台的全局键盘钩子，",
        "不是系统输入法，所以打字前要在托盘确认一眼图标颜色。",
        null,
        "开关与模式",
        "· Ctrl+Space　启用或停用。停用状态下键盘一个键都不吃。",
        "· 左 Shift 单敲　切中文/英文。按住 Shift 打大写字母、Ctrl+Shift 切布局都不算单敲，",
        "  不会误触；右 Shift 不参与。",
        "· 托盘图标　蓝底「五」是中文，琥珀底「A」是英文；停用时图标不变，只在文字上标「（已停用）」。",
        null,
        "打字",
        "· a-z　组码，当前编码显示在候选条上方。",
        "· 空格 / 回车　上屏第一候选。",
        "· 1-9　按序号上屏候选，小键盘同样有效。",
        "· Backspace　退掉最后一位编码。",
        "· Esc　丢掉手上整截编码。",
        "· 标点、方向键之类　先把第一候选上屏，再把这个键原样放行，所以「中CPU」这类混排顺序是自然的。",
        "· 大写字母一律不当编码，直接交给目标程序，编码缓冲也不清。",
        null,
        "候选",
        "· 一页 9 条，五笔段在前、拼音段在后；拼音段降成次要灰、前面点一个间隔号「·」分开两段。",
        "· 三码以内唯一全码命中直接上屏；四码一律等你按空格。",
        "· 编码超过 4 位后五笔自然无解，这一段只剩拼音候选。",
        "· 五笔和拼音共用一页，名额先各分半页，没用完的顺延，免得五笔的同前缀长词把拼音挤没。",
        null,
        "码表",
        "· 五笔　极爽词库 4.3 修订版，86 版一级简码布局，87521 条。",
        "· 拼音　rime-ice 的 base / 8105 / 41448 合并表，395529 条，按词频排序。",
        "· 拼音连着打就行，比如 zhongguo 出「中国」。",
        null,
        "标点",
        "· 全程用英文标点，中文标点和带声调的字母、假名一类词条都不加载。",
        null,
        "打不出字的情况",
        "· 以管理员权限运行的程序（任务管理器等）收不到键，这是 Windows 的 UIPI 限制，不是码表问题。",
        "· 独占全屏的游戏画不出候选窗，任何外部窗口都压不进那块 surface；切「无边框窗口」模式就能用。");

    private static string Lines(params string?[] rows) =>
        string.Join(Environment.NewLine, rows.Where(row => row is not null));
}

/// <summary>只读文本窗：可以选中复制，Esc 或「关闭」退出。</summary>
internal sealed class TextDialog : Form
{
    // 跟候选窗同一套浅底深字，两个窗口挨着看不刺眼。
    private static readonly Color PanelColor = Color.FromArgb(0xF9, 0xF9, 0xF9);
    private static readonly Color InkColor = Color.FromArgb(0x1F, 0x1F, 0x1F);

    private readonly Font _bodyFont = new("Microsoft YaHei UI", 9.75f);

    public TextDialog(string title, string body, Size clientSize)
    {
        Text = title;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        MinimumSize = new Size(380, 220);
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

        var text = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            ScrollBars = ScrollBars.Vertical,
            BackColor = PanelColor,
            ForeColor = InkColor,
            Font = _bodyFont,
            Dock = DockStyle.Fill,
            TabStop = false,
            Text = body,
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
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _bodyFont.Dispose();
        }

        base.Dispose(disposing);
    }
}

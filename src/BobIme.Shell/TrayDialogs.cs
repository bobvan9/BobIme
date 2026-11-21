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
            new InfoRow("启用/停用", "默认 Ctrl+空格。停用状态下所有按键原样交给当前程序，但这个热键本身仍旧有效。"),
            new InfoRow("切换中/英", "默认单敲左 Shift，等同于在托盘菜单的「英文输入」与中文档位之间来回，圆点同步。按住 Shift 输入大写字母、Ctrl+Shift 切换键盘布局均不触发，右 Shift 无效。"),
            new InfoRow("切换标点档", "默认 Ctrl+.，只在中文输入模式下生效；切标点档不动当前编码。"),
            new InfoRow("以上三个键", "都是设置界面里的默认值，可以各自改成别的组合键或单敲，也能不绑定。"),
            new InfoRow("托盘菜单", "自上而下：符号输入；英文标点 / 中文标点；英文输入 / 五笔输入 / 拼音输入 / 混合输入；设置、说明、关于、退出。两组档位各选其一，选中项前为实心圆点。切换输入档会清除当前编码并启用输入法；未加载拼音码表时后两项不可选。"),
            new InfoRow("托盘图标", "蓝底「五」为中文模式，琥珀底「A」为英文模式。停用时图标不变，仅在提示文字中标注「已停用」。"),
        ]),
        new("设置",
        [
            new InfoRow("打开", "托盘菜单「设置…」。界面里改的是那份配置的副本，点「保存」才回写文件；「取消」或直接关窗口就丢掉。"),
            new InfoRow("启动档位", "输入档和标点档各选其一，只决定下次启动时的初始状态；当前档位随时能在托盘菜单里切。"),
            new InfoRow("快捷键", "三个录入框：在框里按下组合键当场记下；单按 Ctrl、Alt、Shift 记成「单敲」；Esc 取消，「不绑定」清空。两个功能绑到同一个键时保存会提示，不让保存。"),
            new InfoRow("候选", "每页候选数 3–9（选词靠 1–9 数字键，所以到 9 封顶）；翻页键可选 [ ]、- =、PageUp/PageDown、← →；候选窗位置可选跟随输入光标、跟随鼠标、桌面右下角。"),
            new InfoRow("符号面板", "出现位置可选屏幕正中或贴着光标；勾上后双击托盘图标直接打开面板。"),
            new InfoRow("英文窗口", "登记程序文件名，每行一个，只认文件名、不区分大小写，最多 32 个。名单内的程序获得焦点时先切到英文档，离开时还回原来那档；在其中手动切过中文则以手动那次为准。「填入」按钮取的是打开设置之前在前台的那个程序。设置界面等本程序自己的窗口，以及任务栏、开始菜单、桌面这类系统窗口不参与判断。"),
            new InfoRow("配置文件", $@"{AppSettings.FilePath}。文件缺失或内容损坏时整份回默认值，不会启动失败。"),
            new InfoRow("恢复默认", "把界面填成默认值，还要点「保存」才落盘。"),
            new InfoRow("设置开着时", "全局键盘钩子暂停，免得录入框和输入法抢同一个按键；窗口关掉就恢复。"),
        ]),
        new("标点",
        [
            new InfoRow("未按 Shift", ", → ，　. → 。　; → ；　` → ·　\\ → 、"),
            new InfoRow("按住 Shift", "< → 《　> → 》　? → ？　: → ：　~ → ～　| → ｜　_ → ——　1 → ！　6 → ……　8 → ×　9 → （　0 → ）"),
            new InfoRow("引号", "' 与 \" 各按一开一合交替，输出 ‘’ 与 “”。"),
            new InfoRow("翻页键位", "归翻页的键不参与标点对照，中文标点档也不把它们换成【】。"),
            new InfoRow("上屏顺序", "编码没挑完时先上屏第一候选再接标点，两次合成一次输出。"),
            new InfoRow("英文标点", "标点键一律原样交给当前程序，由程序自己决定半角还是全角。"),
        ]),
        new("符号面板",
        [
            new InfoRow("打开", "托盘菜单「符号输入」；设置里勾上「托盘图标双击打开符号面板」后，双击托盘图标也能打开。面板按设置里的「出现位置」摆在屏幕正中或贴着光标，自己不抢焦点，正在输入的窗口不会离开前台。"),
            new InfoRow("选字", "点上面的分组标签换类，点符号直接上屏；面板保持打开，可以连着点几个。"),
            new InfoRow("分组", "工程、标点、数学、序号、单位、希腊、拼音、平假名、片假名、箭头、杂项。"),
            new InfoRow("关闭", "点标题栏右侧的 ×。面板不接收键盘，标题栏可以拖动。"),
        ]),
        new("按键",
        [
            new InfoRow("a–z", "输入编码，当前编码显示在候选条上方。"),
            new InfoRow("空格 / 回车", "上屏第一候选。"),
            new InfoRow("1–9", "按序号上屏对应候选，主键盘与小键盘等效；序号取的是当前页。"),
            new InfoRow("翻页键", "默认 [ 往前一页、] 往后一页，设置里可换成 - =、PageUp/PageDown、← →。翻不动时同样吞掉该键，不上屏也不输出这个字符。"),
            new InfoRow("候选窗箭头", "页码右侧的 ◀ ▶ 与翻页键等效，供鼠标点击翻页；该方向已无页可翻时箭头置灰、不响应。翻页只换候选，候选窗位置不动。"),
            new InfoRow("Backspace", "删除最后一位编码。"),
            new InfoRow("Esc", "清除当前编码。"),
            new InfoRow("大写字母", "不作为编码，直接交给当前程序，已输入的编码保持不变。"),
            new InfoRow("其他按键", "未列入标点对照表的按键，先将第一候选上屏，再原样放行该按键，中英文混排的先后顺序因此正确。"),
        ]),
        new("候选规则",
        [
            new InfoRow("分页", "每页候选数在设置里定，默认 9 条、最多 9 条（选词靠 1–9 数字键），一次缓存 5 页，用翻页键或页码右侧的箭头前后翻；混输时五笔候选在前、拼音候选在后，两段以间隔号分隔并以浅灰区分。编码右侧标注当前页码，仅一页时不标。"),
            new InfoRow("自动上屏", "三码以内唯一全码命中时自动上屏；四码需按空格确认。"),
            new InfoRow("名额分配", "混输时两类候选先各占半页，未占满的名额顺延给另一类，避免前缀集中的词相互挤占。"),
            new InfoRow("长编码", "编码超过四位后五笔无候选，仅显示拼音结果。"),
            new InfoRow("窗口位置", "按设置里的「候选窗位置」摆放：跟随输入光标（浏览器、Electron 这类不建系统光标的程序改从无障碍接口取光标位置，两处都取不到才退回鼠标）、跟随鼠标（鼠标下方 24 像素）、桌面右下角（任务栏上方）。三种摆法都夹在任务栏之外，被开始菜单这类置顶窗口压住时挪到边上。翻页不重新定位。"),
        ]),
        new("词库",
        [
            new InfoRow("五笔", "极爽词库 4.3 修订版，采用 86 版一级简码布局，共 87521 条。"),
            new InfoRow("拼音", "rime-ice base / 8105 / 41448 合并表，共 395529 条，按词频排序。"),
            new InfoRow("连写", "拼音支持整词连写，例如输入 zhongguo 得到「中国」。"),
            new InfoRow("词条过滤", "码表里挂在编码上的中文标点、带声调字母、日语假名条目一律不加载，所以候选文本本身不带标点；标点半角还是全角，看的是上面「标点」一节的档位。"),
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

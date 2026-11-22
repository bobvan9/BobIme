using BobIme.Core;

namespace BobIme.Shell;

/// <summary>
/// 托盘「设置…」的界面。改的是传进来那份配置的副本：保存才回写并落盘，取消就丢掉。
/// 启动档位和启动标点档只记进文件，下次启动才生效；其余几项保存后当场接上。
/// </summary>
internal sealed class SettingsForm : Form
{
    private static readonly Color PanelColor = Color.FromArgb(0xF9, 0xF9, 0xF9);
    private static readonly Color InkColor = Color.FromArgb(0x1F, 0x1F, 0x1F);
    private static readonly Color TitleColor = Color.FromArgb(0x0F, 0x6C, 0xBD);
    private static readonly Color HintColor = Color.FromArgb(0x6E, 0x76, 0x81);

    /// <summary>说明文字换行封顶的宽度，窗口宽度由它管着，别被最长的那句撑开。</summary>
    private const int NoteWrapWidth = 980;

    private readonly Font _bodyFont = new("Microsoft YaHei UI", 9.75f);
    private readonly Font _boldFont = new("Microsoft YaHei UI", 9.75f, FontStyle.Bold);
    private readonly bool _hasPinyin;
    private readonly TableLayoutPanel _grid;
    private int _row;

    private readonly RadioButton _modeEnglish = Mode("英文输入", InputMode.English);
    private readonly RadioButton _modeWubi = Mode("五笔输入", InputMode.Wubi);
    private readonly RadioButton _modePinyin = Mode("拼音输入", InputMode.Pinyin);
    private readonly RadioButton _modeMixed = Mode("混合输入", InputMode.Mixed);
    private readonly RadioButton _punctChinese = new() { Text = "中文标点", AutoSize = true, Tag = true };
    private readonly RadioButton _punctEnglish = new() { Text = "英文标点", AutoSize = true, Tag = false };
    private readonly KeyBox _englishKey = new();
    private readonly KeyBox _punctKey = new();
    private readonly KeyBox _enableKey = new();
    private readonly NumericUpDown _pageSize = new()
    {
        Minimum = 3,
        Maximum = 9,
        Increment = 1,
        TextAlign = HorizontalAlignment.Left,
        Width = 64,
    };
    private readonly ComboBox _pageKeys = Picker();
    private readonly ComboBox _candidatePlacement = Picker();
    private readonly ComboBox _placement = Picker();
    private readonly CheckBox _trayDouble = new() { Text = "托盘图标双击打开符号面板", AutoSize = true };
    private readonly DataGridView _rules = RuleGrid();
    private readonly Button _ruleDelete = new() { Text = "删除一行", AutoSize = true };
    private readonly Label _fileHint = new() { AutoSize = true, ForeColor = HintColor };
    private readonly TextBox _orderCode = new() { Width = 112 };
    private readonly ListBox _orderWords = OrderList();
    private readonly DataGridView _orderRules = OrderGrid();
    private readonly Label _orderHint = new() { AutoSize = true, ForeColor = HintColor };
    private readonly Func<string, IReadOnlyList<Candidate>> _candidateLookup;
    private List<WordOrder> _orders = [];
    private bool _orderRefreshing;

    /// <summary>候选次序那一栏：一项一个词，从上往下就是上屏的先后。</summary>
    private static ListBox OrderList() => new()
    {
        Width = 150,
        Height = 96,
        IntegralHeight = false,
        SelectionMode = SelectionMode.One,
        HorizontalScrollbar = true,
    };

    /// <summary>
    /// 规则一览：一行一条，左边是编码、右边是排好的词序。只读，改次序走下面那一栏，
    /// 选中一行会把它的编码填进输入框。
    /// </summary>
    private static DataGridView OrderGrid()
    {
        var code = new DataGridViewTextBoxColumn
        {
            HeaderText = "编码",
            Name = "code",
            Width = 88,
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable,
        };
        var words = new DataGridViewTextBoxColumn
        {
            HeaderText = "词序",
            Name = "words",
            Width = 272,
            ReadOnly = true,
            ToolTipText = "该编码下候选的先后次序，没列出的词照词库原本的次序排在后面",
            SortMode = DataGridViewColumnSortMode.NotSortable,
        };
        return new DataGridView
        {
            Columns = { code, words },
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            AutoGenerateColumns = false,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            BackgroundColor = PanelColor,
            BorderStyle = BorderStyle.FixedSingle,
            GridColor = Color.FromArgb(0xDD, 0xDD, 0xDD),
            Width = 384,
            Height = 84,
        };
    }

    /// <summary>
    /// 规则表：一行一个程序，左边填程序名、中间选档位、右边勾「整窗放行」。
    /// 增行靠按钮、删行用 Del，所以不给表格自带的那条空白新行（它会把「当前行」始终占住）。
    /// </summary>
    private static DataGridView RuleGrid()
    {
        var name = new DataGridViewTextBoxColumn
        {
            HeaderText = "程序名",
            Name = "name",
            ToolTipText = "只认文件名，wow、Wow.exe、整条路径都算同一个",
            Width = 208,
            SortMode = DataGridViewColumnSortMode.NotSortable,
        };
        var mode = new DataGridViewComboBoxColumn
        {
            HeaderText = "档位",
            Name = "mode",
            DataSource = WindowModeInfo.DisplayNames,
            Width = 88,
            FlatStyle = FlatStyle.Flat,
            SortMode = DataGridViewColumnSortMode.NotSortable,
            Resizable = DataGridViewTriState.False,
        };
        var pass = new DataGridViewCheckBoxColumn
        {
            HeaderText = "整窗放行",
            Name = "pass",
            ToolTipText = "该程序在前台时键盘钩子不介入任何按键，切换快捷键全部交还给它",
            Width = 88,
            SortMode = DataGridViewColumnSortMode.NotSortable,
            Resizable = DataGridViewTriState.False,
        };
        var grid = new DataGridView
        {
            Columns = { name, mode, pass },
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = true,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            AutoGenerateColumns = false,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            BackgroundColor = PanelColor,
            BorderStyle = BorderStyle.FixedSingle,
            GridColor = Color.FromArgb(0xDD, 0xDD, 0xDD),
            Width = 392,
            Height = 122,
        };
        // 勾选框那格要当场提交，不然等失去焦点才落到档位那一格上，界面看着像慢半拍。
        grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (grid.IsCurrentCellDirty)
            {
                grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        };
        grid.CellValueChanged += (_, e) =>
        {
            if (e.RowIndex >= 0)
            {
                LockModeCell(grid.Rows[e.RowIndex]);
            }
        };
        return grid;
    }

    /// <summary>勾了整窗放行的那一行，档位那一列不管用，就把格子灰掉，免得填出一个自相矛盾的组合。</summary>
    private static void LockModeCell(DataGridViewRow row)
    {
        var pass = row.Cells[2].Value is true;
        row.Cells[1].ReadOnly = pass;
        row.Cells[1].Style.ForeColor = pass ? HintColor : SystemColors.WindowText;
    }

    /// <param name="lastForegroundExe">打开设置之前那个在前台程序的文件名，给「填成一条规则」按钮用；读不到就是 null。</param>
    /// <param name="candidateLookup">按编码查当前候选，给「词序调整」那一栏列出次序。</param>
    public SettingsForm(AppSettings settings, bool hasPinyin, string? lastForegroundExe, Func<string, IReadOnlyList<Candidate>> candidateLookup)
    {
        _hasPinyin = hasPinyin;
        _candidateLookup = candidateLookup;
        Settings = settings.Clone();

        Text = "Bob 五笔 设置";
        Font = _bodyFont;
        BackColor = PanelColor;
        ForeColor = InkColor;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        _grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 2,
            BackColor = PanelColor,
            Padding = new Padding(12, 10, 12, 8),
        };
        _grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        AddSection("启动档位");
        AddRow("输入档", ModePanel());
        AddRow("标点档", PunctPanel());
        AddRow("", Note("这两项在下次启动程序时生效；当前档位随时能在托盘菜单里切。"));

        AddSection("按程序定档");
        AddRow("规则表", _rules);
        AddRow("", ForegroundRow(lastForegroundExe));
        AddRow("", Note(
            "表内的程序获得焦点时先切到它那一档，离开时还回原来那档；在其中手动切过档位则以手动那次为准。"
            + "中文档只把中文模式打开，码表档仍旧用当前那一档。"
            + "勾选「整窗放行」的行不参与切档：该程序在前台时键盘钩子不介入任何按键，切换快捷键全部交还给它。"
            + $"程序名只认文件名，最多 {WindowRule.Max} 条；选中一行点「删除一行」，按 Del 也一样。"));

        AddSection("快捷键");
        AddRow("切换中英输入", KeyPanel(_englishKey));
        AddRow("切换中英标点", KeyPanel(_punctKey));
        AddRow("启用/停用输入法", KeyPanel(_enableKey));
        AddRow("", Note("在框里按下组合键即可记下；单按 Ctrl、Alt、Shift 记成「单敲」，Esc 取消。"));

        AddSection("候选");
        AddRow("每页候选数", _pageSize);
        AddRow("翻页键", _pageKeys);
        AddRow("候选窗位置", _candidatePlacement);
        AddRow("", Note("跟光标时，浏览器、Electron 这类不建系统光标的程序改从无障碍接口取位置；两处都取不到才退回鼠标。"));

        AddSection("词序调整");
        AddRow("规则", Flow(_orderRules, OrderEditor()));
        AddRow("", Note(
            "填一个编码，下面列出它当前的候选；选中一项用上移、下移调次序，调好的次序就是该编码的一条规则。"
            + $"规则只调整同一编码内候选的先后，简码先出与拼音词频不受影响；词库里不在这些席位上的词照原次序排在后面。最多 {WordOrder.Max} 条，每条 {WordOrder.MaxWords} 个词。"));

        AddSection("符号面板");
        AddRow("出现位置", _placement);
        AddRow("", _trayDouble);

        AddSection("配置文件");
        var path = new Label
        {
            Text = AppSettings.FilePath,
            AutoSize = true,
            ForeColor = HintColor,
            Margin = new Padding(0, 4, 8, 0),
        };
        var export = new Button { Text = "导出配置…", AutoSize = true };
        var import = new Button { Text = "导入配置…", AutoSize = true };
        export.Click += (_, _) => ExportToFile();
        import.Click += (_, _) => ImportFromFile();
        AddRow("", Flow(path, export, import));
        AddRow("", _fileHint);

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Anchor = AnchorStyles.Right,
            Margin = new Padding(0, 14, 0, 0),
        };
        var restore = new Button { Text = "恢复默认", AutoSize = true };
        var ok = new Button { Text = "保存", AutoSize = true };
        var cancel = new Button { Text = "取消", AutoSize = true, DialogResult = DialogResult.Cancel };
        restore.Click += (_, _) => WriteControls(new AppSettings());
        ok.Click += (_, _) => Save();
        buttons.Controls.AddRange([ok, cancel, restore]);
        AddRow("", buttons);

        Controls.Add(_grid);
        AcceptButton = ok;
        CancelButton = cancel;

        FillPageKeys();
        FillPlacements();
        WriteControls(Settings);
    }

    /// <summary>点保存时定稿的那份；对话框没走完就用原值。</summary>
    public AppSettings Settings { get; private set; }

    private static RadioButton Mode(string label, InputMode mode) =>
        new() { Text = label, AutoSize = true, Tag = mode };

    private static ComboBox Picker() => new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        AutoSize = false,
        Width = 210,
        Anchor = AnchorStyles.Left,
    };

    private Panel ModePanel()
    {
        _modePinyin.Enabled = _hasPinyin;
        _modeMixed.Enabled = _hasPinyin;
        return Flow(_modeEnglish, _modeWubi, _modePinyin, _modeMixed);
    }

    private Panel PunctPanel() => Flow(_punctChinese, _punctEnglish);

    private Panel KeyPanel(KeyBox box)
    {
        var clear = new Button { Text = "不绑定", AutoSize = true, Margin = new Padding(6, 0, 0, 0) };
        clear.Click += (_, _) => box.Binding = KeyBinding.None;
        return Flow(box, clear);
    }

    /// <summary>取打开设置之前的前台程序名：读不到就不给填，免得往表里塞一条空项。</summary>
    private Panel ForegroundRow(string? exe)
    {
        var hint = new Label
        {
            Text = exe is { Length: > 0 } ? $"打开设置前的前台程序：{exe}" : "打开设置前的前台程序：未读到",
            AutoSize = true,
            ForeColor = HintColor,
            Margin = new Padding(0, 4, 8, 0),
        };
        var fill = new Button { Text = "填成一条规则", AutoSize = true, Enabled = exe is { Length: > 0 } };
        fill.Click += (_, _) => AppendRule(new WindowRule(exe!, WindowMode.English));
        var blank = new Button { Text = "添加一行", AutoSize = true };
        blank.Click += (_, _) => AppendRule(new WindowRule("", WindowMode.English));
        _ruleDelete.Click += (_, _) => RemoveCurrentRule();
        // 空表或光标不在任何一行上时删除无从下手，按钮就别亮着。
        _rules.CurrentCellChanged += (_, _) => SyncRuleDelete();
        _rules.RowsRemoved += (_, _) => SyncRuleDelete();
        SyncRuleDelete();
        return Flow(hint, fill, blank, _ruleDelete);
    }

    private void SyncRuleDelete() => _ruleDelete.Enabled = _rules.CurrentRow is not null;

    /// <summary>删掉光标所在的那一行，光标跟着落到隔壁那一行；表格空了才没有光标。</summary>
    private void RemoveCurrentRule()
    {
        var index = _rules.CurrentRow?.Index ?? -1;
        if (index < 0)
        {
            return;
        }

        CommitEdit();
        _rules.Rows.RemoveAt(index);
        if (_rules.Rows.Count > 0)
        {
            // RemoveAt 会把光标一起抹掉，不补回去的话剩一行也显示成「无可删除」。
            var at = Math.Min(index, _rules.Rows.Count - 1);
            _rules.CurrentCell = _rules.Rows[at].Cells[0];
        }

        SyncRuleDelete();
    }

    /// <summary>新行加在表尾并把光标停在程序名上，接着就能敲。</summary>
    private void AppendRule(WindowRule rule)
    {
        CommitEdit();
        var index = _rules.Rows.Add(rule.Name, rule.Mode.Display(), rule.PassThrough);
        // 程序化填表不会触发 CellValueChanged，勾了放行的行要自己补刷一次灰度。
        LockModeCell(_rules.Rows[index]);
        _rules.CurrentCell = _rules.Rows[index].Cells[0];
        _rules.BeginEdit(true);
    }

    /// <summary>正在录入的那一格要先落定，否则读表读到的是改之前的值。</summary>
    private void CommitEdit()
    {
        if (_rules.IsCurrentCellInEditMode)
        {
            _rules.EndEdit();
        }
    }

    /// <summary>把表里的行收成规则，空名的行丢掉。</summary>
    private List<WindowRule> ReadRules()
    {
        CommitEdit();
        var rules = new List<WindowRule>();
        foreach (DataGridViewRow row in _rules.Rows)
        {
            var name = row.Cells[0].Value?.ToString()?.Trim();
            if (name is { Length: > 0 })
            {
                rules.Add(new WindowRule(name, WindowModeInfo.FromDisplay(row.Cells[1].Value?.ToString()), row.Cells[2].Value is true));
            }
        }

        return rules;
    }

    private void WriteRules(IEnumerable<WindowRule> rules)
    {
        CommitEdit();
        _rules.Rows.Clear();
        foreach (var rule in rules)
        {
            _rules.Rows.Add(rule.Name, rule.Mode.Display(), rule.PassThrough);
        }

        // 灰不灰不能指望程序化填表时那一下 CellValueChanged，自己对着勾过的行再刷一遍。
        foreach (DataGridViewRow row in _rules.Rows)
        {
            LockModeCell(row);
        }
    }

    /// <summary>
    /// 规则一览右边那一竖排：填编码列出候选，用上移、下移定次序。
    /// 和一览并排摆而不是一行一行往下堆，是因为这张窗口已经快赶上一屏高了。
    /// </summary>
    private Panel OrderEditor()
    {
        var code = Flow(ColumnName("编码"), _orderCode, _orderHint);
        var words = Flow(ColumnName("次序"), _orderWords, OrderButtons());
        var panel = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = new Padding(14, 0, 0, 0),
        };
        panel.Controls.AddRange([code, words]);
        return panel;
    }

    private static Label ColumnName(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Margin = new Padding(0, 4, 8, 0),
    };

    /// <summary>次序那一栏右边的三个按钮，事件在这里接上：控件都建好了，编码框才有得读。</summary>
    private Panel OrderButtons()
    {
        var up = new Button { Text = "上移", AutoSize = true };
        var down = new Button { Text = "下移", AutoSize = true };
        var clear = new Button { Text = "清除规则", AutoSize = true };
        up.Click += (_, _) => MoveOrder(-1);
        down.Click += (_, _) => MoveOrder(1);
        clear.Click += (_, _) => ClearOrderRule();

        _orderCode.TextChanged += (_, _) => RefreshOrderCandidates();
        _orderRules.SelectionChanged += (_, _) => PickOrderRule();

        var panel = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = new Padding(6, 0, 0, 0),
        };
        panel.Controls.AddRange([up, down, clear]);
        return panel;
    }

    /// <summary>选中规则一览里的某一行，把它的编码填进输入框，下面跟着换成一列候选。</summary>
    private void PickOrderRule()
    {
        if (_orderRefreshing)
        {
            return;
        }

        if (_orderRules.CurrentRow?.Cells[0].Value?.ToString() is { Length: > 0 } code && _orderCode.Text != code)
        {
            _orderCode.Text = code;
        }
    }

    /// <summary>按编码框里的编码列一遍候选：先查码表，再套上界面里正在改的这份规则。</summary>
    private void RefreshOrderCandidates()
    {
        _orderRefreshing = true;
        _orderWords.Items.Clear();
        var code = WordOrder.CodeKey(_orderCode.Text);
        if (code.Length > 0)
        {
            foreach (var candidate in WordOrder.Apply(_candidateLookup(code), _orders))
            {
                if (candidate.Code != code || _orderWords.Items.Count >= WordOrder.MaxWords)
                {
                    continue;
                }

                _orderWords.Items.Add(candidate.Text);
            }

            _orderWords.SelectedIndex = _orderWords.Items.Count > 0 ? 0 : -1;
        }

        _orderRefreshing = false;
        ShowOrderStatus(code);
    }

    /// <summary>把选中的一项挪一格，挪完就落成这个编码的一条规则。</summary>
    private void MoveOrder(int delta)
    {
        var items = _orderWords.Items;
        var from = _orderWords.SelectedIndex;
        var to = from + delta;
        if (from < 0 || to < 0 || to >= items.Count)
        {
            return;
        }

        var moved = items[from]!;
        items[from] = items[to]!;
        items[to] = moved;
        _orderWords.SelectedIndex = to;
        CommitOrder();
    }

    /// <summary>
    /// 当前列表的次序就是该编码的规则；只剩一个词时谈不上次序，把这条规则删掉。
    /// 一览重铺之后码表那一份还照保存时的规则走，所以这里不碰查询，只在保存后当场接上。
    /// </summary>
    private void CommitOrder()
    {
        var code = WordOrder.CodeKey(_orderCode.Text);
        string[] words = [.. _orderWords.Items.Cast<object>().Select(item => item?.ToString() ?? "").Where(text => text.Length > 0)];
        var at = _orders.FindIndex(rule => rule.Code == code);
        if (code.Length == 0)
        {
            return;
        }

        if (words.Length < 2)
        {
            if (at >= 0)
            {
                _orders.RemoveAt(at);
            }

            WriteOrderRules();
            return;
        }

        if (at < 0 && _orders.Count >= WordOrder.Max)
        {
            _orderHint.Text = $"规则已有 {WordOrder.Max} 条，先删一条再调";
            return;
        }

        if (at >= 0)
        {
            _orders[at] = new WordOrder(code, words);
        }
        else
        {
            _orders.Add(new WordOrder(code, words));
        }

        WriteOrderRules();
    }

    /// <summary>删掉这个编码的规则，列表跟着回到词库原本的次序。</summary>
    private void ClearOrderRule()
    {
        var code = WordOrder.CodeKey(_orderCode.Text);
        if (code.Length == 0)
        {
            return;
        }

        _orders.RemoveAll(rule => rule.Code == code);
        RefreshOrderCandidates();
    }

    private void WriteOrderRules()
    {
        _orderRefreshing = true;
        _orderRules.Rows.Clear();
        foreach (var rule in _orders)
        {
            _orderRules.Rows.Add(rule.Code, string.Join(",", rule.Words));
        }

        _orderRefreshing = false;
        ShowOrderStatus(WordOrder.CodeKey(_orderCode.Text));
    }

    private void ShowOrderStatus(string code) => _orderHint.Text = code.Length switch
    {
        > 0 when _orderWords.Items.Count == 0 => "词库里没有这个编码的候选",
        0 when _orderCode.Text.Trim().Length > 0 => "编码只认小写字母",
        _ => $"规则 {_orders.Count}/{WordOrder.Max} 条",
    };

    private static Panel Flow(params Control[] controls)
    {
        var panel = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0),
        };
        panel.Controls.AddRange(controls);
        return panel;
    }

    private static Label Note(string text) => new()
    {
        Text = text,
        AutoSize = true,
        // 说明文字会越写越长，不封顶的话整张窗口跟着往宽里撑，比屏幕还宽。
        MaximumSize = new Size(NoteWrapWidth, 0),
        ForeColor = HintColor,
        Margin = new Padding(0, 2, 0, 6),
    };

    private void AddSection(string title) =>
        AddRow("", new Label
        {
            Text = title,
            AutoSize = true,
            Font = _boldFont,
            ForeColor = TitleColor,
            Margin = new Padding(0, 10, 0, 2),
        });

    /// <summary>一行两列：左边标签、右边控件；标签留空就是整行说明。</summary>
    private void AddRow(string label, Control control)
    {
        if (label.Length > 0)
        {
            Place(new Label
            {
                Text = label,
                AutoSize = true,
                Margin = new Padding(0, 4, 12, 0),
            }, 0, _row);
        }

        if (label.Length > 0)
        {
            control.Margin = new Padding(0, 2, 0, 2);
        }

        Place(control, label.Length > 0 ? 1 : 0, _row);
        if (label.Length == 0)
        {
            _grid.SetColumnSpan(control, 2);
        }

        _grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _row++;
        _grid.RowCount = _row;
    }

    private void Place(Control control, int column, int row) => _grid.Controls.Add(control, column, row);

    private void FillPageKeys()
    {
        foreach (var value in Enum.GetValues<PageKeySet>())
        {
            _pageKeys.Items.Add(new Option(value.Display(), value));
        }
    }

    private void FillPlacements()
    {
        foreach (var value in Enum.GetValues<CandidatePlacement>())
        {
            _candidatePlacement.Items.Add(new Option(value.Display(), value));
        }

        _placement.Items.Add(new Option("屏幕正中", PanelPlacement.Center));
        _placement.Items.Add(new Option("贴着光标", PanelPlacement.Caret));
    }

    /// <summary>把一份配置铺到控件上，恢复默认走的就是它。</summary>
    private void WriteControls(AppSettings settings)
    {
        Settings = settings;
        var mode = settings.StartupMode;
        if (!_hasPinyin && mode is InputMode.Pinyin or InputMode.Mixed)
        {
            mode = InputMode.Wubi;
        }

        foreach (var radio in new[] { _modeEnglish, _modeWubi, _modePinyin, _modeMixed })
        {
            radio.Checked = (InputMode)radio.Tag! == mode;
        }

        _punctChinese.Checked = settings.ChinesePunctuation;
        _punctEnglish.Checked = !settings.ChinesePunctuation;
        _englishKey.Binding = settings.EnglishToggle;
        _punctKey.Binding = settings.PunctuationToggle;
        _enableKey.Binding = settings.EnabledToggle;
        _pageSize.Value = settings.PageSize;
        Select(_pageKeys, settings.PageKeys);
        Select(_candidatePlacement, settings.CandidatePlacement);
        Select(_placement, settings.SymbolPanelPlacement);
        _trayDouble.Checked = settings.TrayDoubleClickOpensSymbols;
        WriteRules(settings.WindowRules);
        _orders = [.. settings.WordOrders];
        _orderCode.Text = "";
        WriteOrderRules();
        RefreshOrderCandidates();
    }

    private static void Select(ComboBox box, Enum value)
    {
        for (var i = 0; i < box.Items.Count; i++)
        {
            if (Equals(((Option)box.Items[i]!).Value, value))
            {
                box.SelectedIndex = i;
                return;
            }
        }

        box.SelectedIndex = -1;
    }

    /// <summary>导的是界面上这一份：没点保存的改动也一起带走，换电脑时不用先存一遍。</summary>
    private void ExportToFile()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "导出配置",
            Filter = "Bob 五笔配置 (*.json)|*.json",
            FileName = "bobime-settings.json",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            ReadDraft().Save(dialog.FileName);
            _fileHint.Text = $"已导出到 {dialog.FileName}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(
                this,
                $"配置没能写入 {dialog.FileName}：{ex.Message}",
                Text,
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    /// <summary>导入只把值铺到界面上，点「保存」才落到设置文件；直接取消就什么也不改。</summary>
    private void ImportFromFile()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "导入配置",
            Filter = "Bob 五笔配置 (*.json)|*.json",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        if (!AppSettings.TryLoadFrom(dialog.FileName, out var loaded))
        {
            MessageBox.Show(
                this,
                $"「{Path.GetFileName(dialog.FileName)}」读不进去，或者不是 Bob 五笔的配置文件。",
                Text,
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        WriteControls(loaded);
        _fileHint.Text = $"已导入「{Path.GetFileName(dialog.FileName)}」，点「保存」后生效。";
    }

    private void Save()
    {
        var draft = ReadDraft();
        var conflict = draft.FindToggleConflict();
        if (conflict is not null)
        {
            MessageBox.Show(
                $"「{conflict.Value.First}」和「{conflict.Value.Second}」绑到了同一个键，请先改掉一个。",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var duplicated = WindowRule.Duplicates(ReadRules());
        if (duplicated.Count > 0)
        {
            MessageBox.Show(
                $"「{string.Join("」「", duplicated.Select(r => r.Name))}」在规则表里登记了两次，请去掉一条再保存。",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        Settings = draft;
        DialogResult = DialogResult.OK;
    }

    private AppSettings ReadDraft()
    {
        var draft = Settings.Clone();
        draft.StartupMode = (InputMode)new[] { _modeEnglish, _modeWubi, _modePinyin, _modeMixed }
            .First(r => r.Checked).Tag!;
        draft.ChinesePunctuation = _punctChinese.Checked;
        draft.EnglishToggle = _englishKey.Binding;
        draft.PunctuationToggle = _punctKey.Binding;
        draft.EnabledToggle = _enableKey.Binding;
        draft.PageSize = (int)_pageSize.Value;
        draft.PageKeys = (PageKeySet)((Option)_pageKeys.SelectedItem!).Value;
        draft.CandidatePlacement = (CandidatePlacement)((Option)_candidatePlacement.SelectedItem!).Value;
        draft.SymbolPanelPlacement = (PanelPlacement)((Option)_placement.SelectedItem!).Value;
        draft.TrayDoubleClickOpensSymbols = _trayDouble.Checked;
        draft.WindowRules = ReadRules();
        draft.WordOrders = [.. _orders];
        return draft.Normalized();
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

    /// <summary>下拉框的一项：显示用 <see cref="Text"/>，取值用 <see cref="Value"/>。</summary>
    private sealed record Option(string Text, Enum Value)
    {
        public override string ToString() => Text;
    }

    /// <summary>
    /// 按键录入框：按下组合键当场记下；单按一个修饰键要等它抬起，
    /// 中间碰过别的键就不算单敲，跟钩子里那套判定一致。
    /// </summary>
    private sealed class KeyBox : TextBox
    {
        private KeyBinding _binding = KeyBinding.None;
        private uint _pending;

        public KeyBox()
        {
            ReadOnly = true;
            TabStop = false;
            Width = 170;
            BackColor = Color.White;
            PreviewKeyDown += (_, e) => e.IsInputKey = true;
            Enter += (_, _) => Text = "请按键…（Esc 取消）";
            Leave += (_, _) => RefreshText();
            KeyDown += OnBoxKeyDown;
            KeyUp += OnBoxKeyUp;
        }

        /// <summary>设计器不参与序列化，这个属性只给代码用。</summary>
        [System.ComponentModel.Browsable(false)]
        [System.ComponentModel.DesignerSerializationVisibility(
            System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public KeyBinding Binding
        {
            get => _binding;
            set
            {
                _binding = value;
                _pending = 0;
                RefreshText();
            }
        }

        private void RefreshText() => Text = _binding.Display;

        private void OnBoxKeyDown(object? sender, KeyEventArgs e)
        {
            e.SuppressKeyPress = true;
            if (e.KeyCode == Keys.Escape)
            {
                RefreshText();
                return;
            }

            var vk = (uint)e.KeyCode;
            if (KeyBinding.IsModifierKey(vk))
            {
                _pending = vk;
                return;
            }

            _pending = 0;
            if (KeyBinding.FromKeyData(e.KeyData) is not { } combo)
            {
                Text = "要带 Ctrl / Alt / Shift";
                return;
            }

            Binding = combo;
        }

        private void OnBoxKeyUp(object? sender, KeyEventArgs e)
        {
            if (_pending != 0 && (uint)e.KeyCode == _pending)
            {
                Binding = KeyBinding.TapOf(_pending);
            }
        }
    }
}

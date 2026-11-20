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
    private readonly ComboBox _placement = Picker();
    private readonly CheckBox _trayDouble = new() { Text = "托盘图标双击打开符号面板", AutoSize = true };

    public SettingsForm(AppSettings settings, bool hasPinyin)
    {
        _hasPinyin = hasPinyin;
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

        AddSection("快捷键");
        AddRow("切换中英输入", KeyPanel(_englishKey));
        AddRow("切换中英标点", KeyPanel(_punctKey));
        AddRow("启用/停用输入法", KeyPanel(_enableKey));
        AddRow("", Note("在框里按下组合键即可记下；单按 Ctrl、Alt、Shift 记成「单敲」，Esc 取消。"));

        AddSection("候选");
        AddRow("每页候选数", _pageSize);
        AddRow("翻页键", _pageKeys);

        AddSection("符号面板");
        AddRow("出现位置", _placement);
        AddRow("", _trayDouble);
        AddRow("", Note($"设置文件：{AppSettings.FilePath}"));

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
        Select(_placement, settings.SymbolPanelPlacement);
        _trayDouble.Checked = settings.TrayDoubleClickOpensSymbols;
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
        draft.SymbolPanelPlacement = (PanelPlacement)((Option)_placement.SelectedItem!).Value;
        draft.TrayDoubleClickOpensSymbols = _trayDouble.Checked;
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

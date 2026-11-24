using BobIme.Core;

namespace BobIme.Shell;

/// <summary>
/// 一套词序规则的编辑面板：规则一览，右边填编码、列候选、用上移下移定次序。
/// 五笔和拼音各挂一块，各查各的码表、各存各的规则，同名的编码也互不影响。
/// </summary>
internal sealed class WordOrderPanel : FlowLayoutPanel
{
    private static readonly Color PanelColor = Color.FromArgb(0xF9, 0xF9, 0xF9);
    private static readonly Color HintColor = Color.FromArgb(0x6E, 0x76, 0x81);

    private readonly CandidateSource _source;
    private readonly Func<string, CandidateSource, IReadOnlyList<Candidate>> _lookup;
    private readonly DataGridView _rules = OrderGrid();
    private readonly TextBox _code = new() { Width = 112 };
    private readonly ListBox _words = OrderList();
    private readonly Label _hint = new() { AutoSize = true, ForeColor = HintColor };
    private List<WordOrder> _orders = [];
    private bool _refreshing;

    public WordOrderPanel(CandidateSource source, Func<string, CandidateSource, IReadOnlyList<Candidate>> lookup)
    {
        _source = source;
        _lookup = lookup;
        AutoSize = true;
        FlowDirection = FlowDirection.LeftToRight;
        WrapContents = false;
        Margin = new Padding(0);

        var buttons = ButtonPanel();
        Controls.AddRange([_rules, Editor(buttons)]);

        // 编码框和一览都有得读了再接事件，免得铺控件那一下就把查询触发。
        _code.TextChanged += (_, _) => RefreshCandidates();
        _rules.SelectionChanged += (_, _) => PickRule();
    }

    /// <summary>面板上现在这份规则，保存时原样交给配置。</summary>
    public IReadOnlyList<WordOrder> Rules => _orders;

    /// <summary>把配置里的规则铺上来：编码框清空，一览重填，候选跟着新规则列一遍。</summary>
    public void Load(IEnumerable<WordOrder> rules)
    {
        _orders = [.. rules];
        _code.Text = "";
        WriteRules();
        RefreshCandidates();
    }

    /// <summary>候选次序那一栏：一项一个词，从上往下就是上屏的先后。</summary>
    private static ListBox OrderList() => new()
    {
        Width = 200,
        Height = 96,
        IntegralHeight = false,
        SelectionMode = SelectionMode.One,
        HorizontalScrollbar = true,
    };

    /// <summary>
    /// 规则一览：一行一条，左边是编码、右边是排好的词序。只读，改次序走右边那一栏，
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
            Width = 440,
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
            Width = 552,
            Height = 84,
        };
    }

    /// <summary>
    /// 一览右边那一竖排：填编码列出候选，用上移、下移定次序。
    /// 和一览并排摆而不是一行一行往下堆，是因为这张窗口已经快赶上一屏高了。
    /// </summary>
    private Panel Editor(Panel buttons)
    {
        var panel = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = new Padding(14, 0, 0, 0),
        };
        panel.Controls.AddRange([Column("编码", _code, _hint), Column("次序", _words, buttons)]);
        return panel;
    }

    /// <summary>次序那一栏右边的三个按钮。</summary>
    private Panel ButtonPanel()
    {
        var up = new Button { Text = "上移", AutoSize = true };
        var down = new Button { Text = "下移", AutoSize = true };
        var clear = new Button { Text = "清除规则", AutoSize = true };
        up.Click += (_, _) => MoveWord(-1);
        down.Click += (_, _) => MoveWord(1);
        clear.Click += (_, _) => ClearRule();

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

    private static Panel Column(string title, params Control[] controls)
    {
        var name = new Label
        {
            Text = title,
            AutoSize = true,
            Margin = new Padding(0, 4, 8, 0),
        };
        var panel = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0),
        };
        panel.Controls.AddRange([name, .. controls]);
        return panel;
    }

    /// <summary>选中规则一览里的某一行，把它的编码填进输入框，下面跟着换成一列候选。</summary>
    private void PickRule()
    {
        if (_refreshing)
        {
            return;
        }

        if (_rules.CurrentRow?.Cells[0].Value?.ToString() is { Length: > 0 } code && _code.Text != code)
        {
            _code.Text = code;
        }
    }

    /// <summary>按编码框里的编码列一遍候选：先查自己那一侧的码表，再套上界面里正在改的这份规则。</summary>
    private void RefreshCandidates()
    {
        _refreshing = true;
        _words.Items.Clear();
        var code = WordOrder.CodeKey(_code.Text);
        if (code.Length > 0)
        {
            foreach (var candidate in WordOrder.Apply(_lookup(code, _source), _orders, code, _source))
            {
                if (candidate.Code != code || _words.Items.Count >= WordOrder.MaxWords)
                {
                    continue;
                }

                _words.Items.Add(candidate.Text);
            }

            _words.SelectedIndex = _words.Items.Count > 0 ? 0 : -1;
        }

        _refreshing = false;
        ShowStatus(code);
    }

    /// <summary>把选中的一项挪一格，挪完就落成这个编码的一条规则。</summary>
    private void MoveWord(int delta)
    {
        var items = _words.Items;
        var from = _words.SelectedIndex;
        var to = from + delta;
        if (from < 0 || to < 0 || to >= items.Count)
        {
            return;
        }

        var moved = items[from]!;
        items[from] = items[to]!;
        items[to] = moved;
        _words.SelectedIndex = to;
        Commit();
    }

    /// <summary>
    /// 当前列表的次序就是该编码的规则；列表空了才算没有规则，只剩一个词也照样是把它置顶。
    /// 一览重铺之后码表那一份还照保存时的规则走，所以这里不碰查询，只在保存后当场接上。
    /// </summary>
    private void Commit()
    {
        var code = WordOrder.CodeKey(_code.Text);
        string[] words = [.. _words.Items.Cast<object>().Select(item => item?.ToString() ?? "").Where(text => text.Length > 0)];
        var at = _orders.FindIndex(rule => rule.Code == code);
        if (code.Length == 0)
        {
            return;
        }

        if (words.Length == 0)
        {
            if (at >= 0)
            {
                _orders.RemoveAt(at);
            }

            WriteRules();
            return;
        }

        if (at < 0 && _orders.Count >= WordOrder.Max)
        {
            _hint.Text = $"规则已有 {WordOrder.Max} 条，先删一条再调";
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

        WriteRules();
    }

    /// <summary>删掉这个编码的规则，列表跟着回到词库原本的次序。</summary>
    private void ClearRule()
    {
        var code = WordOrder.CodeKey(_code.Text);
        if (code.Length == 0)
        {
            return;
        }

        _orders.RemoveAll(rule => rule.Code == code);
        RefreshCandidates();
    }

    private void WriteRules()
    {
        _refreshing = true;
        _rules.Rows.Clear();
        foreach (var rule in _orders)
        {
            _rules.Rows.Add(rule.Code, string.Join(",", rule.Words));
        }

        _refreshing = false;
        ShowStatus(WordOrder.CodeKey(_code.Text));
    }

    private void ShowStatus(string code) => _hint.Text = code.Length switch
    {
        > 0 when _words.Items.Count == 0 => "词库里没有这个编码的候选",
        0 when _code.Text.Trim().Length > 0 => "编码只认小写字母",
        _ => $"规则 {_orders.Count}/{WordOrder.Max} 条",
    };
}

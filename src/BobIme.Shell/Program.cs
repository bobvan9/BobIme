using BobIme.Core;
using BobIme.Shell.Interop;

namespace BobIme.Shell;

/// <summary>托盘菜单里四选一的输入档。英文档就是中文模式关掉，另三档决定挂哪本码表。</summary>
public enum InputMode
{
    English,
    Wubi,
    Pinyin,
    Mixed,
}

internal sealed class ImeApplicationContext : ApplicationContext
{
    internal const string DictArgPrefix = "--dict=";

    /// <summary>圆点跟着菜单字号走，DPI 变了不会缩成一个点。</summary>
    private static readonly int BulletSize = Math.Max(11, (SystemFonts.MenuFont?.Height ?? 20) * 3 / 5);

    private static readonly Bitmap RadioOff = Bullet(false);
    private static readonly Bitmap RadioOn = Bullet(true);

    private readonly CandidateForm _candidates;
    private readonly NotifyIcon _tray;
    private readonly ContextMenuStrip _menu;
    private readonly LowLevelKeyboardHook _hook;
    private readonly ToolStripMenuItem[] _punctItems;
    private readonly ToolStripMenuItem[] _modeItems;
    private readonly ImeController _controller;
    private AppSettings _settings;
    private SymbolForm? _symbols;
    private SettingsForm? _settingsForm;
    private readonly Icon _iconChinese;
    private readonly Icon _iconEnglish;
    private readonly bool _hasPinyin;
    private string _trayText = "";

    public ImeApplicationContext(IReadOnlyList<CodeTable> tables, AppSettings settings)
    {
        _settings = settings.Normalized();
        _controller = new ImeController(new ImeEngine(tables))
        {
            Settings = _settings,
            PageSize = _settings.PageSize,
        };
        _candidates = new CandidateForm();
        _candidates.PageRequested += next =>
        {
            if (_controller.TurnPage(next))
            {
                ShowCandidates();
            }
        };

        // 提前把句柄建出来，钩子回调里的 BeginInvoke 才有投递目标。
        _ = _candidates.Handle;

        _hasPinyin = tables.Any(t => t.Source == CandidateSource.Pinyin);
        _controller.ChinesePunctuation = _settings.ChinesePunctuation;

        _menu = new ContextMenuStrip();
        _menu.Items.Add(new ToolStripMenuItem("符号输入", null, (_, _) => ShowSymbols()));
        _menu.Items.Add(new ToolStripSeparator());
        _punctItems = [PunctItem("英文标点", false), PunctItem("中文标点", true)];
        _modeItems = [ModeItem("英文输入", InputMode.English), ModeItem("五笔输入", InputMode.Wubi),
            ModeItem("拼音输入", InputMode.Pinyin), ModeItem("混合输入", InputMode.Mixed)];
        _menu.Items.AddRange([.. _punctItems, new ToolStripSeparator(), .. _modeItems]);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(new ToolStripMenuItem("设置…", null, (_, _) => ShowSettings()));
        _menu.Items.Add(new ToolStripMenuItem("说明", null, (_, _) => TrayDialogs.ShowUsage()));
        _menu.Items.Add(new ToolStripMenuItem("关于", null, (_, _) => TrayDialogs.ShowAbout()));
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(new ToolStripMenuItem("退出", null, (_, _) => ExitThread()));

        _iconChinese = LoadIcon("app.ico") ?? SystemIcons.Application;
        _iconEnglish = LoadIcon("app-en.ico") ?? _iconChinese;

        _tray = new NotifyIcon
        {
            Icon = _iconChinese,
            Text = "Bob 五笔",
            ContextMenuStrip = _menu,
            Visible = true,
        };
        _tray.MouseDoubleClick += (_, _) =>
        {
            if (_settings.TrayDoubleClickOpensSymbols)
            {
                ShowSymbols();
            }
        };

        // 启动档要在菜单和托盘都建好之后再挑，SyncStatus 才有东西可以点亮。
        ApplyStartupMode(_settings.StartupMode);

        _hook = new LowLevelKeyboardHook { OnKeyDown = OnKeyDown, OnTap = OnTapKey };
        _hook.TapKey = TapKeyOf(_settings.EnglishToggle);
        _hook.Install();
        SyncStatus();
    }

    /// <summary>单敲形态才有监听键，组合键形态在 <see cref="ImeController"/> 里判。</summary>
    private static uint TapKeyOf(KeyBinding binding) => binding.IsTap ? binding.Vk : 0;

    /// <summary>
    /// 启动档只决定开机那一下：没挂拼音码表时拼音档和混输档都退回纯五笔，
    /// 免得圆点落在一个灰掉的菜单项上。
    /// </summary>
    private void ApplyStartupMode(InputMode mode)
    {
        SetMode(!_hasPinyin && mode is InputMode.Pinyin or InputMode.Mixed ? InputMode.Wubi : mode);
    }

    private ToolStripMenuItem PunctItem(string label, bool chinese)
    {
        var item = new ToolStripMenuItem(label)
        {
            Tag = chinese,
            ImageScaling = ToolStripItemImageScaling.None,
        };
        item.Click += (_, _) =>
        {
            _controller.ChinesePunctuation = chinese;
            SyncStatus();
        };
        return item;
    }

    private ToolStripMenuItem ModeItem(string label, InputMode mode)
    {
        var item = new ToolStripMenuItem(label)
        {
            Tag = mode,
            ImageScaling = ToolStripItemImageScaling.None,
            Enabled = mode is InputMode.English or InputMode.Wubi || _hasPinyin,
        };
        item.Click += (_, _) => SetMode(mode);
        return item;
    }

    private void SetMode(InputMode mode)
    {
        if (mode == InputMode.English)
        {
            _controller.SetChineseMode(false);
        }
        else
        {
            _controller.ActiveSource = mode switch
            {
                InputMode.Wubi => CandidateSource.Wubi,
                InputMode.Pinyin => CandidateSource.Pinyin,
                _ => null,
            };
            _controller.SetChineseMode(true);
        }

        // 挑档就是打算用，总开关顺手开回来，不然点完打字没反应像是坏了。
        _controller.Enabled = true;
        _candidates.HideComposition();
        SyncStatus();
    }

    private InputMode CurrentMode => !_controller.ChineseMode
        ? InputMode.English
        : _controller.ActiveSource switch
        {
            CandidateSource.Wubi => InputMode.Wubi,
            CandidateSource.Pinyin => InputMode.Pinyin,
            _ => InputMode.Mixed,
        };

    private static Bitmap Bullet(bool on)
    {
        var bmp = new Bitmap(BulletSize, BulletSize);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var ring = new Rectangle(0, 0, BulletSize - 1, BulletSize - 1);
        using (var pen = new Pen(SystemColors.ControlDark))
        {
            g.DrawEllipse(pen, ring);
        }

        if (on)
        {
            var pad = Math.Max(2, BulletSize / 4);
            using var brush = new SolidBrush(SystemColors.ControlText);
            g.FillEllipse(
                brush, ring.X + pad, ring.Y + pad, ring.Width - pad * 2 + 1, ring.Height - pad * 2 + 1);
        }

        return bmp;
    }

    // 托盘按系统的 SM_CXSMICON 挑 .ico 里最贴近的一档，别拿 256 的大图去缩放。
    private static Icon? LoadIcon(string file)
    {
        var path = Path.Combine(AppContext.BaseDirectory, file);
        return File.Exists(path) ? new Icon(path, SystemInformation.SmallIconSize) : null;
    }

    private void OnTapKey()
    {
        var decision = _controller.ToggleChineseMode();
        Post(() => Apply(decision));
    }

    // 钩子回调里只判断，上屏和刷窗都挪回消息循环，退出途中丢一次没关系。
    private void Post(Action work)
    {
        try
        {
            _candidates.BeginInvoke(work);
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private bool OnKeyDown(KeyEvent key)
    {
        var decision = _controller.OnKeyDown(key);
        Post(() => Apply(decision));
        return decision.Consume;
    }

    private void Apply(KeyDecision decision)
    {
        if (decision.CommitText is { Length: > 0 } text)
        {
            TextInserter.SendText(text);
        }

        if (decision.Composing)
        {
            ShowCandidates();
        }
        else
        {
            _candidates.HideComposition();
        }

        SyncStatus();
    }

    private void ShowCandidates() =>
        _candidates.ShowComposition(
            _controller.Buffer, _controller.Candidates, _controller.Page, _controller.Pages);

    private void ShowSymbols()
    {
        _candidates.HideComposition();
        _symbols ??= new SymbolForm();
        _symbols.ShowAt(_settings.SymbolPanelPlacement);
    }

    /// <summary>
    /// 设置界面开着时钩子整个放行：按键录入框要接的那个键不能先被钩子吞掉，
    /// 而且这时候在设置框里打字也不该出候选窗。
    /// </summary>
    private void ShowSettings()
    {
        if (_settingsForm is not null)
        {
            _settingsForm.Activate();
            return;
        }

        _candidates.HideComposition();
        _symbols?.Hide();
        _hook.Suspended = true;
        try
        {
            using var dialog = new SettingsForm(_settings, _hasPinyin);
            _settingsForm = dialog;
            if (dialog.ShowDialog() != DialogResult.OK)
            {
                return;
            }

            _settings = dialog.Settings;
            try
            {
                _settings.Save();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"设置没能写入 {AppSettings.FilePath}：{ex.Message}\n本次改动只在当前这次运行里有效。",
                    "Bob 五笔");
            }

            ApplySettings();
        }
        finally
        {
            _settingsForm = null;
            _hook.Suspended = false;
        }
    }

    /// <summary>不当场生效的只有启动档位和启动标点档，其余立刻接上。</summary>
    private void ApplySettings()
    {
        _controller.Settings = _settings;
        _controller.PageSize = _settings.PageSize;
        _hook.TapKey = TapKeyOf(_settings.EnglishToggle);
        SyncStatus();
    }

    private void SyncStatus()
    {
        // 圆点只看控制器现在的状态，左 Shift 单敲切的英文档这里会跟着亮。
        foreach (var item in _modeItems)
        {
            item.Image = (InputMode)item.Tag! == CurrentMode ? RadioOn : RadioOff;
        }

        foreach (var item in _punctItems)
        {
            item.Image = (bool)item.Tag! == _controller.ChinesePunctuation ? RadioOn : RadioOff;
        }

        var trayText = !_controller.Enabled
            ? "Bob 五笔（已停用）"
            : _controller.ChineseMode ? "Bob 五笔" : "Bob 五笔（英文）";
        if (trayText == _trayText)
        {
            return;
        }

        _trayText = trayText;
        _tray.Text = trayText;
        _tray.Icon = _controller.Enabled && !_controller.ChineseMode ? _iconEnglish : _iconChinese;
        if (!_controller.Enabled)
        {
            _controller.Reset();
            _candidates.HideComposition();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _hook.Dispose();
            _tray.Dispose();
            _menu.Dispose();
            _candidates.Dispose();
            _symbols?.Dispose();
        }

        base.Dispose(disposing);
    }
}

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--check"))
        {
            return SelfCheck.Run(args);
        }

        using var mutex = new Mutex(true, @"Local\BobIme.Shell", out var isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show("Bob 五笔已经在运行了，请先到托盘退出。", "Bob 五笔");
            return 0;
        }

        var dictPath = ResolveDictPath(args);
        if (dictPath is null)
        {
            MessageBox.Show(
                $"找不到码表文件。查找顺序：BOBIME_DICT 环境变量、{ImeApplicationContext.DictArgPrefix}<路径>、{DefaultDictPath}",
                "Bob 五笔");
            return 1;
        }

        try
        {
            var tables = new List<CodeTable> { CodeTableLoader.LoadFromFile(dictPath) };
            var pinyinPath = ResolvePinyinPath(args);
            if (pinyinPath is not null)
            {
                tables.Add(CodeTableLoader.LoadFromFile(pinyinPath, CodeTableFormat.Pinyin));
            }

            ApplicationConfiguration.Initialize();
            Application.Run(new ImeApplicationContext(tables, AppSettings.Load()));
            return 0;
        }
        catch (Exception ex)
        {
            // 常驻后台进程崩了不会有任何提示，这里必须把原因显示出来。
            MessageBox.Show($"启动失败：{ex.Message}", "Bob 五笔");
            return 1;
        }
    }

    internal static string DefaultDictPath => Path.Combine(AppContext.BaseDirectory, "dict", "jisuan43.tsv");

    internal static string DefaultPinyinPath => Path.Combine(AppContext.BaseDirectory, "dict", "pinyin_base.tsv");

    internal static string? ResolveDictPath(string[] args) =>
        Resolve(args, "BOBIME_DICT", ImeApplicationContext.DictArgPrefix, DefaultDictPath);

    internal static string? ResolvePinyinPath(string[] args) =>
        Resolve(args, "BOBIME_PINYIN", "--pinyin=", DefaultPinyinPath);

    private static string? Resolve(string[] args, string envVar, string argPrefix, string defaultPath)
    {
        if (Exists(Environment.GetEnvironmentVariable(envVar)))
        {
            return Environment.GetEnvironmentVariable(envVar);
        }

        var fromArgs = args.FirstOrDefault(a => a.StartsWith(argPrefix, StringComparison.Ordinal));
        if (fromArgs is not null)
        {
            var path = fromArgs[argPrefix.Length..];
            if (Exists(path))
            {
                return path;
            }
        }

        return Exists(defaultPath) ? defaultPath : null;
    }

    private static bool Exists(string? path) => path is { Length: > 0 } && File.Exists(path);
}

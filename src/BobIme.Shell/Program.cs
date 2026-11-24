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
    private readonly FocusWatcher _focus;
    private readonly QuitWatcher _quit;
    private readonly ToolStripMenuItem[] _punctItems;
    private readonly ToolStripMenuItem[] _modeItems;
    private readonly ImeController _controller;
    private AppSettings _settings;

    /// <summary>
    /// 进表时记下的那一档（离开时还回去）、我们顶上来的那一档、以及顶那一档的是哪个窗口。
    /// 三个都要记：用户在中途自己切过的话，当前档位已经不是我们顶的那一档，就不该还；
    /// 同一个窗口再报一次前台事件也不该把他切回来的那一次盖掉。
    /// </summary>
    private InputMode? _held;
    private InputMode? _forced;
    private nint _forcedWindow;

    /// <summary>
    /// 当前前台窗口在规则表里被勾了「整窗放行」。钩子回调线程要读它，所以只跟着真正的前台窗口整值改写。
    /// </summary>
    private bool _bypass;

    private nint _lastUserForeground;
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
            WubiWordOrders = _settings.WordOrders,
            PinyinWordOrders = _settings.PinyinWordOrders,
        };
        _candidates = new CandidateForm();
        _candidates.Placement = _settings.CandidatePlacement;
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

        // 安装程序和注销关机都会请我们退出，得有人答这一声；不走托盘菜单那条路，退不干净。
        _quit = new QuitWatcher(() => Post(ExitThread));

        // 启动档要在菜单和托盘都建好之后再挑，SyncStatus 才有东西可以点亮。
        ApplyStartupMode(_settings.StartupMode);

        _hook = new LowLevelKeyboardHook { OnKeyDown = OnKeyDown, OnTap = OnTapKey };
        _hook.TapKey = TapKeyOf(_settings.EnglishToggle);
        _hook.Install();

        // 窗口事件钩子只管前台换人：开机时已经在前台的那个窗口自己补一次，不等事件。
        _focus = new FocusWatcher { OnForeground = OnForegroundChanged };
        _focus.Install();
        _lastUserForeground = Native.GetForegroundWindow();
        OnForegroundChanged(_lastUserForeground);
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

    /// <summary>
    /// 换前台窗口时按「按程序定档」规则表处置。这条跑在窗口事件的回调里，只读进程名，别在这里问 UIA。
    /// 自家的窗口（设置界面、说明、消息框）不参与：它们是弹出来给人打字的，
    /// 跟着规则表走会把用户正在填的内容搅掉。任务栏、桌面这类 shell 窗口同样不参与：
    /// 点托盘就会让它们成为前台，当成「离开表」的话，菜单还没打开档位就先还回去了。
    /// </summary>
    private void OnForegroundChanged(nint hwnd)
    {
        if (hwnd == 0 || WindowInfo.IsOwnProcess(hwnd) || WindowInfo.IsShellWindow(hwnd))
        {
            // 档位的事不参与，但整窗放行的闸门要松开：在游戏里打开设置界面，
            // 那格的程序名还得照常打字。
            _bypass = false;
            return;
        }

        _lastUserForeground = hwnd;
        var rule = WindowRule.Find(_settings.WindowRules, WindowInfo.ExeName(hwnd));

        // 勾了整窗放行的窗口：键一个不碰，档位也不替他顶，手上那半截编码先丢掉，
        // 免得切出去之后某个键把它顶上屏。
        if (rule?.PassThrough ?? false)
        {
            if (!_bypass)
            {
                _controller.Reset();
                _candidates.HideComposition();
            }

            _bypass = true;
            return;
        }

        _bypass = false;
        var target = ModeOnTarget(rule);
        if (target is not null)
        {
            if (ModeOnFocus.ShouldApply(CurrentMode, target, hwnd == _forcedWindow))
            {
                // 记下的是用户自己那一档：表里两个程序来回切时别把上一个窗口顶的档当成他的选择。
                _held ??= CurrentMode;
                _forced = target.Value;
                _forcedWindow = hwnd;
                SetMode(target.Value);
            }

            return;
        }

        var restore = ModeOnFocus.ModeToRestore(_held, _forced, CurrentMode);
        _held = null;
        _forced = null;
        _forcedWindow = 0;
        if (restore is not null)
        {
            SetMode(restore.Value);
        }
    }

    /// <summary>
    /// 这条规则要顶哪一档：表里没有就返回 <c>null</c> 不插手，
    /// 英文档直接切英文，中文档要「中文模式开着、码表档原样」。
    /// 一个程序在表里只有一条（<see cref="WindowRule.Normalize"/> 按文件名去重），所以不会两头对着干。
    /// </summary>
    private InputMode? ModeOnTarget(WindowRule? rule) => rule switch
    {
        null => null,
        { Mode: WindowMode.English } => InputMode.English,
        _ => ChineseMode,
    };

    /// <summary>中文模式开着时会是哪一档——只看控制器挂着的码表，英文档期间它没被动过。</summary>
    private InputMode ChineseMode => _controller.ActiveSource switch
    {
        CandidateSource.Wubi => InputMode.Wubi,
        CandidateSource.Pinyin => InputMode.Pinyin,
        _ => InputMode.Mixed,
    };

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

    private InputMode CurrentMode => !_controller.ChineseMode ? InputMode.English : ChineseMode;

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
        if (_bypass)
        {
            return;
        }

        var decision = _controller.ToggleChineseMode();
        Post(() => Apply(decision));
    }

    // 钩子回调里只判断，上屏和刷窗都挪回消息循环，退出途中丢一次没关系。
    // 句柄没了有两种说法：Dispose 过的报 ObjectDisposedException，只是拆掉的报 InvalidOperationException。
    // 哪一个都不能让它冲出钩子回调——那是一次说不清原因的崩溃，卸载还在半路上。
    private void Post(Action work)
    {
        try
        {
            _candidates.BeginInvoke(work);
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
        {
        }
    }

    private bool OnKeyDown(KeyEvent key)
    {
        // 整窗放行时回到这儿：不判快捷键、不吃键，Ctrl+Space 那一条本来要吞键，所以闸门得排在它前面。
        if (_bypass)
        {
            return false;
        }

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
    /// 焦点在设置界面上时钩子一律放行：按键录入框要接的那个键不能先被钩子吞掉。
    /// 判据取前台窗口而不是「对话框开着」——模态框挡不住 Alt+Tab，
    /// 切到别的程序还照抄放行，就会顺手把那边的输入档、标点档改掉。
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
        try
        {
            using var dialog = new SettingsForm(
                _settings, _hasPinyin, WindowInfo.ExeName(_lastUserForeground), _controller.Peek);
            _settingsForm = dialog;
            _hook.CaptureOwner = dialog.Handle;
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
                    dialog,
                    $"设置没能写入 {AppSettings.FilePath}：{ex.Message}\n本次改动只在当前这次运行里有效。",
                    "Bob 五笔");
            }

            ApplySettings();
        }
        finally
        {
            _hook.CaptureOwner = 0;
            _settingsForm = null;
        }
    }

    /// <summary>不当场生效的只有启动档位和启动标点档，其余立刻接上。</summary>
    private void ApplySettings()
    {
        _controller.Settings = _settings;
        _controller.PageSize = _settings.PageSize;
        _controller.WubiWordOrders = _settings.WordOrders;
        _controller.PinyinWordOrders = _settings.PinyinWordOrders;
        _candidates.Placement = _settings.CandidatePlacement;
        _hook.TapKey = TapKeyOf(_settings.EnglishToggle);
        // 规则表刚改完就得对得上：刚切回来的那个程序可能正是这轮加进表里或从表里删掉的那个。
        // 拿真正的前台窗口重算，别替已经切走的窗口扳闸门。
        OnForegroundChanged(Native.GetForegroundWindow());

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
            _focus.Dispose();
            _tray.Dispose();
            _menu.Dispose();
            _candidates.Dispose();
            _symbols?.Dispose();
            _quit.Dispose();
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

    internal static string DefaultDictPath => Path.Combine(AppContext.BaseDirectory, "dict", "jidian86.tsv");

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

using BobIme.Core;
using BobIme.Shell.Interop;

namespace BobIme.Shell;

internal sealed class ImeApplicationContext : ApplicationContext
{
    internal const string DictArgPrefix = "--dict=";

    private readonly CandidateForm _candidates;
    private readonly NotifyIcon _tray;
    private readonly ContextMenuStrip _menu;
    private readonly LowLevelKeyboardHook _hook;
    private readonly ToolStripMenuItem _enableItem;
    private readonly ImeController _controller;
    private readonly Icon _iconChinese;
    private readonly Icon _iconEnglish;
    private string _trayText = "";

    public ImeApplicationContext(IReadOnlyList<CodeTable> tables)
    {
        _controller = new ImeController(new ImeEngine(tables) { PageSize = 9 });
        _candidates = new CandidateForm();

        // 提前把句柄建出来，钩子回调里的 BeginInvoke 才有投递目标。
        _ = _candidates.Handle;

        _enableItem = new ToolStripMenuItem("启用五笔·拼音混输");
        _enableItem.Click += (_, _) =>
        {
            _controller.Enabled = !_controller.Enabled;
            SyncStatus();
        };

        _menu = new ContextMenuStrip();
        _menu.Items.Add(_enableItem);
        _menu.Items.Add(new ToolStripSeparator());
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

        _hook = new LowLevelKeyboardHook { OnKeyDown = OnKeyDown, OnShiftTap = OnShiftTap };
        _hook.Install();
        SyncStatus();
    }

    // 托盘按系统的 SM_CXSMICON 挑 .ico 里最贴近的一档，别拿 256 的大图去缩放。
    private static Icon? LoadIcon(string file)
    {
        var path = Path.Combine(AppContext.BaseDirectory, file);
        return File.Exists(path) ? new Icon(path, SystemInformation.SmallIconSize) : null;
    }

    private void OnShiftTap()
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
            _candidates.ShowComposition(_controller.Buffer, _controller.Candidates);
        }
        else
        {
            _candidates.HideComposition();
        }

        SyncStatus();
    }

    private void SyncStatus()
    {
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
        _enableItem.Checked = _controller.Enabled;
        if (_controller.Enabled)
        {
            return;
        }

        _controller.Reset();
        _candidates.HideComposition();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _hook.Dispose();
            _tray.Dispose();
            _menu.Dispose();
            _candidates.Dispose();
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
            Application.Run(new ImeApplicationContext(tables));
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

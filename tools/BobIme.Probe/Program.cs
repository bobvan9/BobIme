using System.Diagnostics;
using BobIme.Core;

var args_ = Environment.GetCommandLineArgs();
var argv = args_[1..];

if (argv.Length == 0 || argv[0] is "-h" or "--help")
{
    Console.WriteLine("""
        用法:
          BobIme.Probe                        交互式敲码，实时看候选
          BobIme.Probe -q <码> [-q <码>]...   批量查询并退出
          BobIme.Probe --dict <码表路径>      指定码表文件
        """);
    return 1;
}

var dictPath = ParsePath(argv, "--dict", "jisuan43.tsv");
if (dictPath is null || !File.Exists(dictPath))
{
    Console.Error.WriteLine($"找不到码表文件：{dictPath ?? "(未找到 data/dict 下的码表)"}");
    return 1;
}

var sw = Stopwatch.StartNew();
var tables = new List<CodeTable> { CodeTableLoader.LoadFromFile(dictPath) };
Console.Error.WriteLine($"码表 {Path.GetFileName(dictPath)}：{tables[0].Count} 条，加载 {sw.ElapsedMilliseconds} ms");

var pinyinPath = ParsePath(argv, "--pinyin", "pinyin_base.tsv");
if (pinyinPath is not null)
{
    sw.Restart();
    tables.Add(CodeTableLoader.LoadFromFile(pinyinPath, CodeTableFormat.Pinyin));
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
    Console.Error.WriteLine(
        $"码表 {Path.GetFileName(pinyinPath)}：{tables[^1].Count} 条，加载 {sw.ElapsedMilliseconds} ms，" +
        $"托管堆 {GC.GetTotalMemory(false) / 1024 / 1024} MB");
}

var queries = CollectQueries(argv);
if (queries.Count > 0)
{
    foreach (var code in queries)
    {
        var engine = new ImeEngine(tables) { PageSize = 9 };
        foreach (var letter in code)
        {
            engine.Press(letter);
        }

        Console.WriteLine($"{code} -> {Format(engine.Candidates)}");
    }

    return 0;
}

return RunInteractive(tables);

static string Format(IReadOnlyList<Candidate> candidates) =>
    candidates.Count == 0
        ? "(无)"
        : string.Join("  ", candidates.Select(c => $"{c.Text}[{c.Code}]{(c.Source == CandidateSource.Pinyin ? "拼" : "")}"));

static string? ParsePath(string[] argv, string flag, string fileName)
{
    for (var i = 0; i < argv.Length - 1; i++)
    {
        if (argv[i] == flag)
        {
            return argv[i + 1];
        }
    }

    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null)
    {
        var candidate = Path.Combine(dir.FullName, "data", "dict", fileName);
        if (File.Exists(candidate))
        {
            return candidate;
        }

        dir = dir.Parent;
    }

    return null;
}

static List<string> CollectQueries(string[] argv)
{
    var codes = new List<string>();
    for (var i = 0; i < argv.Length - 1; i++)
    {
        if (argv[i] == "-q")
        {
            codes.Add(argv[i + 1].ToLowerInvariant());
        }
    }

    return codes;
}

static int RunInteractive(List<CodeTable> tables)
{
    var engine = new ImeEngine(tables) { PageSize = 9 };
    Console.OutputEncoding = System.Text.Encoding.UTF8;
    var row = Console.CursorTop;
    var done = false;
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        done = true;
    };
    Render();

    while (!done)
    {
        var key = Console.ReadKey(intercept: true);
        var result = key.Key switch
        {
            ConsoleKey.Backspace => engine.PressBackspace(),
            ConsoleKey.Escape => engine.PressEscape(),
            ConsoleKey.Spacebar => engine.SelectFirst(),
            ConsoleKey.Enter => engine.SelectFirst(),
            _ when key.KeyChar is >= '1' and <= '9' => engine.SelectAt(key.KeyChar - '1'),
            _ => engine.Press(key.KeyChar),
        };

        if (result.Status == KeyStatus.Committed)
        {
            Console.WriteLine($"\n上屏：{result.Text}");
            row = Console.CursorTop;
        }
        else if (result.Status == KeyStatus.Ignored && key.Key is not (ConsoleKey.Backspace or ConsoleKey.Escape))
        {
            Console.WriteLine($"\n放行：{key.KeyChar}");
            row = Console.CursorTop;
        }
        else if (result is { Status: KeyStatus.Updated, AutoCommit: true })
        {
            var committed = engine.SelectFirst();
            Console.WriteLine($"\n自动上屏：{committed.Text}");
            row = Console.CursorTop;
        }

        Render();
    }

    return 0;

    void Render()
    {
        Console.SetCursorPosition(0, row);
        var line = engine.IsComposing
            ? $"[{engine.Buffer}] {Format(engine.Candidates)}"
            : "待命    字母组码 / 1-9 或空格选词 / Backspace 退格 / Esc 清码 / Ctrl+C 退出";
        Console.Write(line.PadRight(Math.Max(line.Length, Console.WindowWidth - 1)));
        Console.SetCursorPosition(0, row);
    }
}

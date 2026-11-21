using System.Diagnostics;
using System.Runtime.InteropServices;
using BobIme.Core;
using BobIme.Shell.Interop;

namespace BobIme.Shell;

/// <summary>启动前的连线检查：码表、结构体尺寸、候选窗、钩子、注入通路。</summary>
internal static class SelfCheck
{
    internal static int Run(string[] args)
    {
        // 从真实终端启动时 WinExe 不继承控制台，需要 Attach；重定向场景已经有句柄，别去动它。
        if (!Console.IsOutputRedirected)
        {
            Native.AttachConsole(Native.ATTACH_PARENT_PROCESS);
        }

        Console.OutputEncoding = System.Text.Encoding.UTF8;
        ApplicationConfiguration.Initialize();

        try
        {
            return Check(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"自检异常：{ex}");
            return 2;
        }
    }

    private static int Check(string[] args)
    {
        var dictPath = Program.ResolveDictPath(args);
        if (dictPath is null)
        {
            Console.Error.WriteLine($"找不到码表：{Program.DefaultDictPath}");
            return 1;
        }

        var sw = Stopwatch.StartNew();
        var tables = new List<CodeTable> { CodeTableLoader.LoadFromFile(dictPath) };
        Console.WriteLine(
            $"码表 {Path.GetFileName(dictPath)}：{tables[0].Count} 条，加载 {sw.ElapsedMilliseconds} ms");

        var pinyinPath = Program.ResolvePinyinPath(args);
        if (pinyinPath is not null)
        {
            sw.Restart();
            tables.Add(CodeTableLoader.LoadFromFile(pinyinPath, CodeTableFormat.Pinyin));
            Console.WriteLine(
                $"码表 {Path.GetFileName(pinyinPath)}：{tables[^1].Count} 条，加载 {sw.ElapsedMilliseconds} ms");
        }

        // SendInput 的结构体尺寸不对就会整体返回 0，先把这个坑探掉。
        Console.WriteLine($"Marshal.SizeOf<INPUT> = {Native.INPUT.Size}（x64 应为 40）");

        var engine = new ImeEngine(tables) { PageSize = 9 };
        var candidates = Type(engine, "k");
        var mixed = candidates.Count(c => c.Source == CandidateSource.Pinyin);
        Console.WriteLine(
            $"查询 k：{candidates.Count} 个候选，首选 {candidates[0].Text}[{candidates[0].Code}]，拼音段 {mixed} 个");

        // 最贵的一次查询：单字母前缀在五笔里命中很多、在拼音表里要扫到词频预算尽头。
        sw.Restart();
        for (var i = 0; i < 200; i++)
        {
            engine.Press('v');
            engine.PressBackspace();
        }

        Console.WriteLine($"单键查询平均 {sw.Elapsed.TotalMilliseconds / 200:F3} ms");

        using var form = new CandidateForm();
        _ = form.Handle;
        foreach (var placement in Enum.GetValues<CandidatePlacement>())
        {
            form.Placement = placement;
            // 同一段编码只定位一次，收起再摊开才看得出这一档摆在哪儿。
            form.HideComposition();
            form.ShowComposition("k", candidates, engine.Page, engine.Pages);
            Console.WriteLine(
                $"候选窗 {placement.Display()}：{form.Width}x{form.Height} @ ({form.Location.X},{form.Location.Y})");
        }

        form.HideComposition();

        using var hook = new LowLevelKeyboardHook { OnKeyDown = _ => false };
        hook.Install();
        Console.WriteLine("低级键盘钩子安装/卸载成功");

        // 现读现用：切换 Caps Lock 之后重跑 --check，这一行应当跟着变。
        var caps = (Native.GetKeyState((int)Native.VK_CAPITAL) & 1) != 0;
        Console.WriteLine($"Caps Lock {(caps ? "开" : "关")}，开着时大写字母不组码");

        return CheckInjection() ? 0 : 3;
    }

    private static IReadOnlyList<Candidate> Type(ImeEngine engine, string code)
    {
        foreach (var letter in code)
        {
            engine.Press(letter);
        }

        return engine.Candidates;
    }

    /// <summary>
    /// 用一个只收录注入事件的临时钩子回读 SendInput。
    /// 比抢前台窗口再读文本框可靠，也不会把测试字符打到用户正在用的窗口里。
    /// </summary>
    private static bool CheckInjection()
    {
        var scans = new List<uint>();
        var proc = new Native.HookProc((nCode, _, lParam) =>
        {
            if (nCode >= 0)
            {
                var raw = Marshal.PtrToStructure<Native.KBDLLHOOKSTRUCT>(lParam);
                if ((raw.flags & Native.LLKHF_INJECTED) != 0 && (raw.flags & Native.LLKHF_UP) == 0)
                {
                    scans.Add(raw.scanCode);
                }
            }

            return 0;
        });

        var handle = Native.SetWindowsHookExW(Native.WH_KEYBOARD_LL, proc, Native.GetModuleHandleW(null), 0);
        if (handle == 0)
        {
            Console.Error.WriteLine("注入自检装不上钩子");
            return false;
        }

        try
        {
            // 全角标点和汉字一样走 Unicode 事件，顺手一起验了。
            var sample = "中国，。";
            var delivered = TextInserter.SendText(sample);
            PumpUntil(() => scans.Count >= sample.Length, 500);

            // Unicode 事件的 wScan 就是字符本身，钩子里读到的应当正好是这几个码位。
            var expected = sample.Select(c => (uint)c);
            var echoed = string.Join(" ", scans.Select(s => $"U+{s:X4}"));
            Console.WriteLine($"SendInput 接收 {delivered}/{sample.Length * 2} 个事件，钩子回读 {echoed}");

            var ok = delivered == sample.Length * 2 && scans.SequenceEqual(expected);
            Console.WriteLine(ok ? "自检通过" : "自检完成，但注入回读不符");
            return ok;
        }
        finally
        {
            Native.UnhookWindowsHookEx(handle);
            GC.KeepAlive(proc);
        }
    }

    private static void PumpUntil(Func<bool> condition, int timeoutMs)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!condition() && sw.ElapsedMilliseconds < timeoutMs)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
    }
}

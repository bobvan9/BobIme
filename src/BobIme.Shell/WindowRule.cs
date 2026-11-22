namespace BobIme.Shell;

/// <summary>规则表里这一条要把那个程序顶成哪一档。</summary>
public enum WindowMode
{
    English,
    Chinese,
}

public static class WindowModeInfo
{
    /// <summary>下拉框里的两项，顺序就是列出来的顺序。</summary>
    public static readonly string[] DisplayNames = [WindowMode.English.Display(), WindowMode.Chinese.Display()];

    public static string Token(this WindowMode mode) => mode == WindowMode.Chinese ? "chinese" : "english";

    public static string Display(this WindowMode mode) => mode == WindowMode.Chinese ? "中文档" : "英文档";

    /// <summary>下拉框选中项的文字换回档位；对不上就按英文档。</summary>
    public static WindowMode FromDisplay(string? text) =>
        text == WindowMode.Chinese.Display() ? WindowMode.Chinese : WindowMode.English;

    /// <summary>认 <c>english</c> / <c>chinese</c>，大小写无所谓；中文的拼音写法也收，手改文件时不用查拼写。</summary>
    public static bool TryParseToken(string? text, out WindowMode mode)
    {
        var value = (text ?? "").Trim().ToLowerInvariant();
        mode = value switch
        {
            "english" or "en" or "yingwen" => WindowMode.English,
            "chinese" or "zh" or "cn" or "zhongwen" => WindowMode.Chinese,
            _ => WindowMode.English,
        };
        return value is "english" or "en" or "yingwen" or "chinese" or "zh" or "cn" or "zhongwen";
    }
}

/// <summary>
/// 「按程序定档」规则表的一行：程序名 + 它获得焦点时要用的档位，外加一个「整窗放行」开关。
/// 程序名只认文件名，写成 <c>qq</c>、<c>QQ.exe</c> 或者整条路径都算同一个，大小写无所谓。
/// 勾了整窗放行，档位那一列就不起作用：那个窗口里钩子一个键都不碰。
/// </summary>
public sealed record WindowRule(string Name, WindowMode Mode, bool PassThrough = false)
{
    /// <summary>再多也没法一一核对，给设置界面留个上限。</summary>
    public const int Max = 32;

    /// <summary>短记号里表示「整窗放行」的后缀，跟在档位后面。</summary>
    private const string PassSuffix = "+pass";

    /// <summary>写进 JSON 的样子：<c>Wow.exe:english</c>、<c>wow.exe:english+pass</c>，一行一条。</summary>
    public string Token => $"{Name}:{Mode.Token()}{(PassThrough ? PassSuffix : "")}";

    /// <summary>
    /// 读 <c>程序名:档位[+pass]</c>；档位漏写就按英文档，<c>+pass</c> 也能直接缀在程序名后面。
    /// 程序名或档位不成形就返回 <c>null</c>。
    /// </summary>
    public static WindowRule? Parse(string? text)
    {
        var value = (text ?? "").Trim();
        if (value.Length == 0)
        {
            return null;
        }

        var pass = false;
        if (value.EndsWith(PassSuffix, StringComparison.OrdinalIgnoreCase))
        {
            pass = true;
            value = value[..^PassSuffix.Length];
            if (value.Length == 0)
            {
                return null;
            }
        }

        var split = value.LastIndexOf(':');
        var name = (split < 0 ? value : value[..split]).Trim();
        if (Key(name).Length == 0)
        {
            return null;
        }

        if (split < 0)
        {
            return new WindowRule(name, WindowMode.English, pass);
        }

        return WindowModeInfo.TryParseToken(value[(split + 1)..], out var mode)
            ? new WindowRule(name, mode, pass)
            : null;
    }

    /// <summary>这个程序在表里的那一条；不在表里返回 <c>null</c>。</summary>
    public static WindowRule? Find(IReadOnlyList<WindowRule>? rules, string? exeName)
    {
        var want = Key(exeName);
        return want.Length == 0 || rules is null ? null : rules.FirstOrDefault(rule => Key(rule.Name) == want);
    }

    /// <summary>空项丢掉，同一个程序的重复项只留先出现的那条，超出上限截断。</summary>
    public static List<WindowRule> Normalize(IEnumerable<WindowRule>? rules)
    {
        var seen = new HashSet<string>();
        var kept = new List<WindowRule>();
        foreach (var rule in rules ?? [])
        {
            var name = (rule?.Name ?? "").Trim();
            var key = Key(name);
            if (key.Length == 0 || !seen.Add(key) || kept.Count >= Max)
            {
                continue;
            }

            kept.Add(new WindowRule(name, rule!.Mode, rule.PassThrough));
        }

        return kept;
    }

    /// <summary>
    /// 同一个程序登记了不止一次时，返回各条撞车的规则（按表里的先后，第一条不算）。
    /// 一个程序只能有一个档位，所以设置界面拦下保存，别悄悄丢掉后一条。
    /// </summary>
    public static List<WindowRule> Duplicates(IEnumerable<WindowRule>? rules)
    {
        var seen = new HashSet<string>();
        var clash = new List<WindowRule>();
        foreach (var rule in rules ?? [])
        {
            var key = Key(rule.Name);
            if (key.Length > 0 && !seen.Add(key))
            {
                clash.Add(rule);
            }
        }

        return clash;
    }

    /// <summary>比对用的键：只留文件名、去扩展名、转小写。</summary>
    public static string Key(string? text) =>
        Path.GetFileNameWithoutExtension((text ?? "").Trim()).ToLowerInvariant();
}

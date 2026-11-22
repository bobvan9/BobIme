namespace BobIme.Core;

/// <summary>
/// 一条词序规则：某个编码下候选文字的先后次序。规则里列出的按列出的次序先出，
/// 没列出的照码表原本的次序排在后面。写进配置的短记号是 <c>qqqq:金,狗狗</c>。
/// </summary>
public sealed record WordOrder(string Code, string[] Words)
{
    /// <summary>编码长度的上限，取拼音码表那一档，五笔的编码自然落在里面。</summary>
    public const int MaxCodeLength = 16;

    /// <summary>一条规则里最多记几个词。</summary>
    public const int MaxWords = 30;

    /// <summary>再多也没法一一核对，给设置界面留个上限。</summary>
    public const int Max = 32;

    /// <summary>写进 JSON 的样子：一行一条，编码在后、次序用逗号串起来。</summary>
    public string Token => $"{Code}:{string.Join(",", Words)}";

    /// <summary>这个词在这条规则里占第几席；规则没列它就算最后一席。</summary>
    public int RankOf(string text)
    {
        var index = Words is { Length: > 0 } ? Array.IndexOf(Words, text) : -1;
        return index < 0 ? int.MaxValue : index;
    }

    /// <summary>
    /// 按规则重排候选：只挪动这条编码自己占的那几个位置，
    /// 所以简码先出、拼音按词频这些原本的整体次序一概不动。
    /// </summary>
    public static IReadOnlyList<Candidate> Apply(IReadOnlyList<Candidate>? candidates, IReadOnlyList<WordOrder>? rules)
    {
        if (candidates is not { Count: > 0 } hits || rules is not { Count: > 0 } orders)
        {
            return candidates ?? [];
        }

        var moved = hits.ToArray();
        foreach (var order in orders)
        {
            var slots = new List<int>();
            for (var i = 0; i < moved.Length; i++)
            {
                if (moved[i].Code == order.Code)
                {
                    slots.Add(i);
                }
            }

            if (slots.Count == 0)
            {
                continue;
            }

            // OrderBy 是稳定排序：规则里没列的词保持原次序，只是整体退到列出的词后面。
            var ranked = slots.Select(i => moved[i]).OrderBy(candidate => order.RankOf(candidate.Text)).ToList();
            for (var i = 0; i < slots.Count; i++)
            {
                moved[slots[i]] = ranked[i];
            }
        }

        return moved;
    }

    /// <summary>读 <c>编码:词1,词2</c>；编码或词不成形就返回 <c>null</c>。</summary>
    public static WordOrder? Parse(string? text)
    {
        var value = (text ?? "").Trim();
        var split = value.IndexOf(':');
        if (split < 0 || CodeKey(value[..split]) is not { Length: > 0 } code)
        {
            return null;
        }

        var words = Distinct(value[(split + 1)..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return words.Count == 0 ? null : new WordOrder(code, [.. words]);
    }

    /// <summary>空项丢掉，同一个编码只留先出现的那条，词去重、超数截断，条数超上限截断。</summary>
    public static List<WordOrder> Normalize(IEnumerable<WordOrder>? rules)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var kept = new List<WordOrder>();
        foreach (var rule in rules ?? [])
        {
            if (rule is null || CodeKey(rule.Code) is not { Length: > 0 } code || !seen.Add(code))
            {
                continue;
            }

            var words = Distinct([.. (rule.Words ?? []).Select(word => (word ?? "").Trim())]);
            if (words.Count == 0 || kept.Count >= Max)
            {
                continue;
            }

            kept.Add(new WordOrder(code, [.. words]));
        }

        return kept;
    }

    /// <summary>同一个词重复登记只留先出现的那个，并截到 <see cref="MaxWords"/> 个。</summary>
    private static List<string> Distinct(IEnumerable<string> words) =>
        words.Where(word => word.Length > 0).Distinct(StringComparer.Ordinal).Take(MaxWords).ToList();

    /// <summary>比对用的键：转小写，只收纯字母且不超过长度上限；不成立返回空串。</summary>
    public static string CodeKey(string? code)
    {
        var value = (code ?? "").Trim().ToLowerInvariant();
        return value.Length is > 0 and <= MaxCodeLength && value.All(c => c is >= 'a' and <= 'z') ? value : "";
    }
}

namespace BobIme.Core;

/// <summary>
/// 一条词序规则：某个编码下候选文字的先后次序。编码敲满时这个编码的词整组提到最前，
/// 组内按列出的次序先出；没列出的照原次序排在后面。五笔和拼音各记一套，规则只管自己那一侧的候选。
/// 写进配置的短记号是 <c>qqqq:金,狗狗</c>。
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
    /// 按规则重排候选，<paramref name="code"/> 是这次查的编码，<paramref name="source"/> 是这套规则管的那一侧码表。
    /// 编码正好敲满某条规则时，那个编码的词整组提到最前、组内按登记的次序；
    /// 其余规则只挪动自己编码原本占的席位，所以单字先出、简码先出、拼音词频这些整体次序一概不动。
    /// 只有和 <paramref name="source"/> 同一侧的候选才可能被挪动，五笔的规则碰不到拼音的候选，反过也一样。
    /// </summary>
    public static IReadOnlyList<Candidate> Apply(
        IReadOnlyList<Candidate>? candidates, IReadOnlyList<WordOrder>? rules, string? code, CandidateSource source)
    {
        if (candidates is not { Count: > 0 } hits || rules is not { Count: > 0 } orders)
        {
            return candidates ?? [];
        }

        var typed = CodeKey(code);
        var ranked = RankWithinSeats(hits, orders, typed, source);
        var hoisted = orders.FirstOrDefault(order => order.Code == typed);
        return hoisted is null ? ranked : MoveBlockToFront(ranked, hoisted, source);
    }

    /// <summary>置顶那条之外的规则只在自己的席位里换位：位置不动，词在这些位置上重排。</summary>
    private static IReadOnlyList<Candidate> RankWithinSeats(
        IReadOnlyList<Candidate> hits, IReadOnlyList<WordOrder> orders, string typed, CandidateSource source)
    {
        var moved = hits.ToArray();
        foreach (var order in orders)
        {
            if (order.Code == typed)
            {
                continue;
            }

            var slots = new List<int>();
            for (var i = 0; i < moved.Length; i++)
            {
                if (moved[i].Code == order.Code && moved[i].Source == source)
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

    /// <summary>把这一侧这个编码的词整组挪到最前，其余候选照原次序接在后面。</summary>
    private static IReadOnlyList<Candidate> MoveBlockToFront(IReadOnlyList<Candidate> hits, WordOrder order, CandidateSource source)
    {
        var block = new List<Candidate>();
        var rest = new List<Candidate>(hits.Count);
        foreach (var candidate in hits)
        {
            (candidate.Code == order.Code && candidate.Source == source ? block : rest).Add(candidate);
        }

        if (block.Count == 0)
        {
            return hits;
        }

        var ranked = block.OrderBy(candidate => order.RankOf(candidate.Text)).ToList();
        ranked.AddRange(rest);
        return ranked;
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
